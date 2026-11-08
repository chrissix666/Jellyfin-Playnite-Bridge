using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One video of a game's Steam store page (trailer, gameplay, demo): its name, thumbnail and the streams Steam serves; nothing is downloaded.</summary>
public sealed record SteamVideo(string Name, string Thumbnail, string? Hls, string? Dash);

/// <summary>The frame rate (frames per second) and size of a Steam clip, from its HLS master manifest; Width and Height 0 when the manifest names none.</summary>
public sealed record SteamFormat(double FrameRate, int Width, int Height);

/// <summary>A playable Steam Special Feature (a video with an HLS stream) as a virtual item: its stable id, the AppID it belongs to and the video.</summary>
public sealed record SteamVideoItem(Guid Id, string AppId, SteamVideo Video);

/// <summary>
/// The videos of a game's Steam store page (Steam Special Features), asked from Steam's public store API (appdetails, movies) with the game's AppID, kept in
/// the plugin's own file steamvideos.json (never in Jellyfin's database) with its fetch time: an entry with videos is never asked again, an empty one (no videos or a refused page) after 30 days. The
/// prefetch of Sync Playnite is throttled (Steam allows about 200 requests in five minutes); a game without such a page is remembered as empty. Only an
/// AppID is sent to Steam, nothing of the user. Each video with an HLS stream is a virtual item (<see cref="SteamVideoItem"/>) that Jellyfin's player plays
/// from Steam's video server; its thumbnail is read once by the server into the plugin's cache folder.
/// </summary>
public static class SteamVideos
{
    private static readonly object Gate = new();
    private static Dictionary<string, Entry>? _entries;
    private static Dictionary<Guid, SteamVideoItem>? _byId;
    private static string? _file;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // The Steam movie id in a thumbnail address ".../steam/apps/<movie id>/movie.293x165.jpg".
    private static readonly Regex MovieIdPattern = new(@"/apps/([0-9]+)/", RegexOptions.Compiled);

    /// <summary>The largest thumbnail kept (Steam's are about 15 KB).</summary>
    public const int MaxThumbnailBytes = 2 * 1024 * 1024;

    // Thumbnails: no automatic redirect (an answer that points elsewhere is no thumbnail), at most MaxThumbnailBytes read.
    private static readonly HttpClient ThumbnailHttp = new(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = MaxThumbnailBytes
    };

    // One download per thumbnail file at a time.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ThumbnailLocks = new(StringComparer.OrdinalIgnoreCase);

    // The last failure reason per item, so a failing thumbnail is logged once per state.
    private static readonly ConcurrentDictionary<Guid, string> ThumbnailFailures = new();

    // One request to Steam's store API at a time, at least Spacing apart.
    private static readonly SemaphoreSlim AskGate = new(1, 1);
    private static DateTime _lastAsk = DateTime.MinValue;

    // When each AppID was last asked again after a failed stream (in memory only).
    private static readonly Dictionary<string, DateTime> Refetched = new(StringComparer.Ordinal);

    /// <summary>How often one AppID may be asked again after a failed stream.</summary>
    public static readonly TimeSpan RefetchInterval = TimeSpan.FromHours(6);

    /// <summary>The least time between two requests to Steam's store API (Steam allows about 200 in five minutes).</summary>
    public static TimeSpan Spacing { get; set; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Reads a thumbnail address (checked by <see cref="ThumbnailAllowed"/> before); null when it does not answer with 2xx, redirects, or sends more than
    /// <see cref="MaxThumbnailBytes"/>.
    /// </summary>
    public static Func<string, CancellationToken, Task<byte[]?>> Download { get; set; } = async (url, cancellationToken) =>
    {
        try
        {
            return await ThumbnailHttp.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException
            && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    };

    /// <summary>
    /// Asks Steam's store API for the movies of an AppID: the answer's text, null without a 2xx answer.
    /// </summary>
    public static Func<string, CancellationToken, Task<string?>> AskSteam { get; set; } = async (appId, cancellationToken) =>
    {
        try
        {
            using var response = await Http.GetAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&filters=movies", cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    };

    /// <summary>
    /// True for a thumbnail address the server may read: absolute https on a host of Steam's image servers (".steamstatic.com", ".akamaihd.net"; the current
    /// manifests only use shared.akamai.steamstatic.com).
    /// </summary>
    public static bool ThumbnailAllowed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="bytes"/> start like a JPEG, PNG or WebP file.</summary>
    public static bool IsImage(byte[] bytes) =>
        (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        || (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        || (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP");

    private sealed record Entry(long Fetched, List<SteamVideo> Videos);

    /// <summary>Sets the folder the file lives in (the plugin's data folder); the stored answers are read again.</summary>
    public static void UseFolder(string folder)
    {
        lock (Gate)
        {
            _file = Path.Combine(folder, "steamvideos.json");
            _entries = null;
            _byId = null;
        }
    }

    private static Dictionary<string, Entry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        try
        {
            _entries = _file is not null && File.Exists(_file)
                ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file)) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _entries = new();
        }

        return _entries;
    }

    private static void Save(ILogger? logger)
    {
        try
        {
            if (_file is null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var temporary = _file + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
            File.Move(temporary, _file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("PlayniteBridge: could not save steamvideos.json: {Message}", ex.Message);
        }
    }

    /// <summary>The stored videos of an AppID (an entry with videos never expires; an empty one is asked again after <see cref="EmptyRetry"/>), or null when it has no entry.</summary>
    public static IReadOnlyList<SteamVideo>? Stored(string appId)
    {
        lock (Gate)
        {
            return Load().TryGetValue(appId, out var entry) ? entry.Videos : null;
        }
    }

    /// <summary>The AppID whose store page a game's Steam Special Features come from: a Steam game's own, else its first linked one; null without one.</summary>
    public static string? AppIdOf(GameEntry game) => game.StoreKey == "steam" ? game.SteamAppId : game.LinkedSteamAppIds.FirstOrDefault();

    /// <summary>
    /// What identifies a video within its AppID: Steam's movie id from the thumbnail address (it stays when Steam renews the "t=" values of the
    /// addresses), else the thumbnail address without its query.
    /// </summary>
    public static string MovieKey(SteamVideo video)
    {
        var match = MovieIdPattern.Match(video.Thumbnail);
        return match.Success ? match.Groups[1].Value : video.Thumbnail.Split('?')[0];
    }

    /// <summary>The playable videos of an AppID's entry as items, in Steam's order: only those with an HLS stream; of two with one movie key the first.</summary>
    public static IReadOnlyList<SteamVideoItem> Items(string appId) =>
        (Stored(appId) ?? Array.Empty<SteamVideo>()).Where(v => !string.IsNullOrEmpty(v.Hls))
            .Select(v => new SteamVideoItem(VirtualIds.SteamVideo(appId, MovieKey(v)), appId, v))
            .GroupBy(i => i.Id).Select(g => g.First()).ToList();

    // The frame rate and size of a clip read from its HLS master manifest, per manifest address; an unreadable manifest is asked again after FormatRetry.
    private static readonly ConcurrentDictionary<string, (SteamFormat? Format, DateTime At)> Formats = new(StringComparer.Ordinal);

    /// <summary>How long a clip whose manifest could not be read (or has no FRAME-RATE) is left alone before it is read again.</summary>
    public static readonly TimeSpan FormatRetry = TimeSpan.FromMinutes(10);

    /// <summary>The manifest read gives up after this long (a clip without it is shown as before).</summary>
    public static readonly TimeSpan FormatTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Reads the text of an HLS master manifest (only a Steam image/video server address, no redirect), null without a 2xx answer.
    /// </summary>
    public static Func<string, CancellationToken, Task<string?>> AskManifest { get; set; } = async (url, cancellationToken) =>
    {
        if (!ThumbnailAllowed(url))
        {
            return null;
        }

        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(FormatTimeout);
            using var response = await ThumbnailHttp.GetAsync(url, limit.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    };

    /// <summary>
    /// The frame rate and size of the largest variant of an HLS master manifest that names a FRAME-RATE
    /// (<c>#EXT-X-STREAM-INF:...,RESOLUTION=700x394,FRAME-RATE=30</c>); null when there is none (a media playlist, garbage, no FRAME-RATE).
    /// A variant without RESOLUTION gives size 0.
    /// </summary>
    public static SteamFormat? ParseManifest(string? text)
    {
        SteamFormat? best = null;
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("#EXT-X-STREAM-INF:", StringComparison.Ordinal))
            {
                continue;
            }

            var rate = Regex.Match(trimmed, @"[:,]FRAME-RATE=([0-9]+(?:\.[0-9]+)?)(?=,|$)");
            if (!rate.Success || !double.TryParse(rate.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps) || fps <= 0)
            {
                continue;
            }

            var size = Regex.Match(trimmed, @"[:,]RESOLUTION=([0-9]+)x([0-9]+)(?=,|$)");
            var width = size.Success && int.TryParse(size.Groups[1].Value, out var w) ? w : 0;
            var height = size.Success && int.TryParse(size.Groups[2].Value, out var h) ? h : 0;
            if (best is null || (long)width * height > (long)best.Width * best.Height)
            {
                best = new SteamFormat(fps, width, height);
            }
        }

        return best;
    }

    /// <summary>The format already read for a clip's manifest address, null when not read, unreadable or without a frame rate (never a request).</summary>
    public static SteamFormat? CachedFormat(string? hls) => hls is not null && Formats.TryGetValue(hls, out var known) ? known.Format : null;

    /// <summary>
    /// Reads the formats of the clips whose manifest is not known yet (a success is kept for the life of the server, a failure for
    /// <see cref="FormatRetry"/>), all at once, each within <see cref="FormatTimeout"/>. Never throws; a clip without a format stays without.
    /// </summary>
    public static async Task EnsureFormats(IEnumerable<SteamVideoItem> items, DateTime now, CancellationToken cancellationToken)
    {
        await Task.WhenAll(items.Select(i => i.Video.Hls).Where(h => !string.IsNullOrEmpty(h)).Distinct().Select(async hls =>
        {
            if (Formats.TryGetValue(hls!, out var known) && (known.Format is not null || now - known.At < FormatRetry))
            {
                return;
            }

            string? text = null;
            try
            {
                text = await AskManifest(hls!, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException && !cancellationToken.IsCancellationRequested)
            {
            }

            Formats[hls!] = (ParseManifest(text), now);
        })).ConfigureAwait(false);
    }

    /// <summary>The item of an id among all stored entries, null for any other id (the index is built once per change of the entries).</summary>
    public static SteamVideoItem? Find(Guid id)
    {
        var index = _byId;
        if (index is null)
        {
            lock (Gate)
            {
                index = _byId ??= Load().Keys.ToList().SelectMany(Items).GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
            }
        }

        return index.GetValueOrDefault(id);
    }

    /// <summary>The image tag of a video's thumbnail: the first 12 hex digits of the SHA-256 of its address (a new address gives a new tag).</summary>
    public static string ThumbnailTag(SteamVideo video) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(video.Thumbnail)), 0, 6).ToLowerInvariant();

    /// <summary>Where the thumbnail of an item is kept: &lt;cache&gt;\playnite-bridge\steam\&lt;id&gt;-&lt;tag&gt;.jpg.</summary>
    public static string ThumbnailPath(string cachePath, SteamVideoItem item) =>
        Path.Combine(cachePath, "playnite-bridge", "steam", item.Id.ToString("N") + "-" + ThumbnailTag(item.Video) + ".jpg");

    /// <summary>How long a failed thumbnail is not announced as an image of its item (a marker file next to the thumbnail's place).</summary>
    private static readonly TimeSpan MissingFor = TimeSpan.FromHours(1);

    /// <summary>
    /// True while the thumbnail of an item is known to be missing (Steam sent none within the last hour, <see cref="Thumbnail"/> leaves a marker): the item then
    /// announces no Primary image tag, so no client asks for a picture that answers 404. A thumbnail already in the cache is never missing.
    /// </summary>
    public static bool ThumbnailMissing(string cachePath, SteamVideoItem item)
    {
        var target = ThumbnailPath(cachePath, item);
        try
        {
            return !File.Exists(target) && File.Exists(target + ".none") && DateTime.UtcNow - File.GetLastWriteTimeUtc(target + ".none") < MissingFor;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The thumbnail file of an item below <paramref name="cachePath"/>, read once from Steam by the server (<see cref="Download"/>) and kept; null when the
    /// address is not allowed (<see cref="ThumbnailAllowed"/>), Steam does not answer, or the answer is larger than <see cref="MaxThumbnailBytes"/> or no
    /// JPEG, PNG or WebP (asked again at the next request; one warning per item and reason). The browser never asks Steam for it.
    /// </summary>
    public static async Task<string?> Thumbnail(string cachePath, SteamVideoItem item, ILogger? logger, CancellationToken cancellationToken)
    {
        var target = ThumbnailPath(cachePath, item);
        if (File.Exists(target))
        {
            return target;
        }

        var gate = ThumbnailLocks.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? failure;
        try
        {
            if (File.Exists(target))
            {
                return target;
            }

            var bytes = ThumbnailAllowed(item.Video.Thumbnail) ? await Download(item.Video.Thumbnail, cancellationToken).ConfigureAwait(false) : null;
            failure = !ThumbnailAllowed(item.Video.Thumbnail) ? "the address is not one of Steam's image servers"
                : bytes is null || bytes.Length == 0 ? "no answer"
                : bytes.Length > MaxThumbnailBytes ? "larger than 2 MB"
                : !IsImage(bytes) ? "no JPEG, PNG or WebP image"
                : null;
            if (failure is null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".tmp";
                await File.WriteAllBytesAsync(temporary, bytes!, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, target, true);
                ThumbnailFailures.TryRemove(item.Id, out _);
                File.Delete(target + ".none");
                return target;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failure = ex.Message;
        }
        finally
        {
            gate.Release();
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target + ".none", Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // without the marker the item keeps announcing its tag
        }

        if (!ThumbnailFailures.TryGetValue(item.Id, out var known) || known != failure)
        {
            ThumbnailFailures[item.Id] = failure;
            logger?.LogWarning("PlayniteBridge: no Steam thumbnail of \"{Name}\" from {Address}: {Reason}", item.Video.Name, item.Video.Thumbnail.Split('?')[0], failure);
        }

        return null;
    }

    /// <summary>
    /// One request to Steam's store API for an AppID (<see cref="AskSteam"/>, at least <see cref="Spacing"/> after the previous one): Answered = Steam sent
    /// a readable answer; Videos = the page's videos when the answer says success:true (<see cref="Parse"/>), else null. An invalid AppID asks nothing.
    /// </summary>
    public static async Task<(bool Answered, List<SteamVideo>? Videos)> Fetch(string appId, CancellationToken cancellationToken)
    {
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit))
        {
            return (true, null);
        }

        string? body;
        await AskGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastAsk + Spacing - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            body = await AskSteam(appId, cancellationToken).ConfigureAwait(false);
            _lastAsk = DateTime.UtcNow;
        }
        finally
        {
            AskGate.Release();
        }

        try
        {
            return body is null ? (false, null) : (true, Parse(appId, body));
        }
        catch (JsonException)
        {
            return (false, null);
        }
    }

    /// <summary>
    /// The videos in Steam's answer for an AppID: data.movies with name, thumbnail, hls_h264 and dash_h264; empty when the page has none. Null unless the
    /// answer is an object whose entry for the AppID says success:true (a refused, removed or region-locked page, null or another shape): no videos (Keep stores an empty entry).
    /// </summary>
    public static List<SteamVideo>? Parse(string appId, string json)
    {
        var videos = new List<SteamVideo>();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(appId, out var app) || app.ValueKind != JsonValueKind.Object
            || !app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        if (!app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("movies", out var movies) || movies.ValueKind != JsonValueKind.Array)
        {
            return videos;
        }

        foreach (var movie in movies.EnumerateArray())
        {
            string? Text(string name) => movie.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var name = Text("name");
            var thumbnail = Text("thumbnail");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(thumbnail))
            {
                videos.Add(new SteamVideo(name!, thumbnail!, Text("hls_h264"), Text("dash_h264")));
            }
        }

        return videos;
    }

    /// <summary>Stores the videos of an AppID (also an empty list, asked again after EmptyRetry) and saves the file.</summary>
    public static void Store(string appId, List<SteamVideo> videos, ILogger? logger)
    {
        lock (Gate)
        {
            Load()[appId] = new Entry(DateTime.UtcNow.Ticks, videos);
            _byId = null;
            Save(logger);
        }
    }

    /// <summary>
    /// The videos of an AppID for a game page: the stored ones while the entry is not due (<see cref="Due"/>), else one request to Steam now, stored when
    /// Steam answered (<see cref="Keep"/>). Null when Steam did not answer and nothing is stored. Used by the page request; the prefetch of Sync Playnite
    /// makes the request rare.
    /// </summary>
    public static async Task<IReadOnlyList<SteamVideo>?> Get(string appId, ILogger? logger, CancellationToken cancellationToken)
    {
        if (!Due(appId, DateTime.UtcNow))
        {
            return Stored(appId);
        }

        var (answered, fetched) = await Fetch(appId, cancellationToken).ConfigureAwait(false);
        return answered ? Keep(appId, fetched, logger) : Stored(appId);
    }

    /// <summary>How long an empty entry (a refused page or one without videos) is kept before Sync Playnite or a page asks Steam again.</summary>
    public static readonly TimeSpan EmptyRetry = TimeSpan.FromDays(30);

    /// <summary>
    /// True when an AppID is to be asked from Steam at <paramref name="now"/>: it has no entry, or an empty entry fetched <see cref="EmptyRetry"/> or more ago.
    /// An entry with videos is never due (a failed stream asks again through <see cref="Refetch"/>).
    /// </summary>
    public static bool Due(string appId, DateTime now)
    {
        lock (Gate)
        {
            return !Load().TryGetValue(appId, out var entry)
                || (entry.Videos.Count == 0 && now - new DateTime(entry.Fetched, DateTimeKind.Utc) >= EmptyRetry);
        }
    }

    // What an answered request leaves in the store: the videos of a success:true answer, else (Steam refused the page) an empty entry with its fetch
    // time, asked again after EmptyRetry. An invalid AppID (no request was made) stores nothing.
    private static List<SteamVideo>? Keep(string appId, List<SteamVideo>? videos, ILogger? logger)
    {
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit))
        {
            return null;
        }

        var kept = videos ?? new List<SteamVideo>();
        Store(appId, kept, logger);
        return kept;
    }

    /// <summary>
    /// Asks Steam again for the entry of an AppID after one of its streams failed in the player: at most once per AppID in <see cref="RefetchInterval"/>
    /// (counted from the attempt, in memory), one request (<see cref="Fetch"/>); the entry is replaced only by a success:true answer, an error is never
    /// stored. True when the entry was replaced.
    /// </summary>
    public static async Task<bool> Refetch(string appId, DateTime now, ILogger? logger, CancellationToken cancellationToken)
    {
        lock (Refetched)
        {
            if (Refetched.TryGetValue(appId, out var last) && now - last < RefetchInterval)
            {
                return false;
            }

            Refetched[appId] = now;
        }

        var (_, videos) = await Fetch(appId, cancellationToken).ConfigureAwait(false);
        if (videos is null)
        {
            logger?.LogInformation("PlayniteBridge: a Steam special feature of AppID {AppId} did not play; Steam gave no new list", appId);
            return false;
        }

        Store(appId, videos, logger);
        logger?.LogInformation("PlayniteBridge: a Steam special feature of AppID {AppId} did not play; its list was asked again ({Count} videos)", appId, videos.Count);
        return true;
    }

    /// <summary>The AppIDs of <paramref name="appIds"/> that are due (<see cref="Due"/>), each once, in order: the ones Sync Playnite asks Steam for.</summary>
    public static List<string> Wanted(IEnumerable<string> appIds)
    {
        var now = DateTime.UtcNow;
        return appIds.Distinct(StringComparer.Ordinal).Where(id => Due(id, now)).ToList();
    }

    /// <summary>
    /// Asks Steam for every due AppID (<see cref="Wanted"/>), one request every <see cref="Spacing"/>; every answer is stored (<see cref="Keep"/>: a refused
    /// page as an empty entry), a request Steam did not answer stores nothing; stops quietly on a cancellation or after 5 requests Steam did not answer.
    /// </summary>
    public static async Task Prefetch(IEnumerable<string> appIds, ILogger logger, CancellationToken cancellationToken)
    {
        var wanted = Wanted(appIds);
        var answeredCount = 0;
        var failures = 0;
        foreach (var id in wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (answered, videos) = await Fetch(id, cancellationToken).ConfigureAwait(false);
            if (!answered)
            {
                if (++failures >= 5)
                {
                    logger.LogWarning("PlayniteBridge: Steam did not answer {Failures} requests for the special features, the rest waits for the next sync", failures);
                    break;
                }
            }
            else
            {
                Keep(id, videos, logger);
                answeredCount++;
            }
        }

        logger.LogInformation("PlayniteBridge: Steam special features: {Answered} of {Wanted} store pages answered", answeredCount, wanted.Count);
    }

    // ---- Still frames for Steam videos whose thumbnail is dead: Steam thumbnail -> still frame of the HLS stream (Jellyfin's ffmpeg) -> a backdrop of the game -> a neutral tile ----

    /// <summary>The most grabs (ffmpeg runs) at once; every request for a missing picture waits for it at most <see cref="FrameWait"/> in all.</summary>
    public static readonly SemaphoreSlim FrameGate = new(2, 2);

    /// <summary>How long the whole grab of one item may take, with its retries at later offsets: then the encoder process and its children are killed (<see cref="RunEncoder"/>).</summary>
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a request waits for a grab slot before it answers the next step of the chain without recording a failure.</summary>
    public static readonly TimeSpan FrameWait = TimeSpan.FromSeconds(25);

    /// <summary>The clock of the failure memory.</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>One grab: the encoder's work for the (checked) stream address; the path of a finished temporary picture, or null.</summary>
    public delegate Task<string?> FrameExtractor(string url, CancellationToken cancellationToken);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FrameLocks = new(StringComparer.OrdinalIgnoreCase);

    // When a grab of an item failed (in memory), so an unwritable folder does not make every request run ffmpeg again.
    private static readonly ConcurrentDictionary<string, DateTime> FrameFailedAt = new(StringComparer.OrdinalIgnoreCase);

    // The last failure reason per item, so a failing stream is logged once per state.
    private static readonly ConcurrentDictionary<Guid, string> FrameFailures = new();

    /// <summary>A grab that failed for a known reason: <see cref="Permanent"/> = the stream itself is gone or has no video (remembered 30 days), else the failure may pass (remembered 10 minutes). The message is ours, never ffmpeg's text or an address.</summary>
    public sealed class FrameFailure : Exception
    {
        public FrameFailure(string reason, bool permanent, bool dark = false)
            : base(reason)
        {
            Permanent = permanent;
            Dark = dark;
        }

        public bool Permanent { get; }

        /// <summary>Every frame tried was too dark (remembered <see cref="DarkRetry"/>, in a marker of its own class).</summary>
        public bool Dark { get; }
    }

    /// <summary>
    /// The fixed argument list of one grab (no shell, the address is ONE argument after "-i"): quiet, no stdin, only the protocols an https HLS needs, the first frame after
    /// <paramref name="seek"/> seconds (an OUTPUT seek: after "-i", ffmpeg reads and decodes up to there; an input seek before "-i" gives an empty result on Steam's streams), 640 px wide
    /// JPEG. Null when the address is not exactly what <see cref="FrameUrl"/> returns (so never an option, never a space or quote) or the output is not an absolute path.
    /// </summary>
    public static IReadOnlyList<string>? FrameArguments(string url, string outputFile, string seek = "5")
    {
        if (FrameUrl(url) != url || !Path.IsPathRooted(outputFile) || outputFile.StartsWith('-') || seek is not ("0" or "5" or "12" or "25" or "40"))
        {
            return null;
        }

        return new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-protocol_whitelist", "https,tls,tcp,crypto", "-i", url,
            "-ss", seek, "-frames:v", "1", "-vf", "scale=640:-2", "-q:v", "3", "-f", "image2", outputFile
        };
    }

    /// <summary>How the text ffmpeg printed to stderr is read: Steam says the stream is gone (403, 404, 410) or has no video = permanent; an unreachable server, a timeout, 5xx or anything unknown = may pass.</summary>
    public static FrameFailure Classify(string errorText)
    {
        var text = errorText ?? string.Empty;
        if (Regex.IsMatch(text, @"Server returned (403|404|410)|HTTP error (403|404|410)", RegexOptions.IgnoreCase))
        {
            return new FrameFailure("Steam no longer has the stream", true);
        }

        if (Regex.IsMatch(text, @"does not contain any stream|matches no streams|Output file is empty|nothing was encoded|no video", RegexOptions.IgnoreCase))
        {
            return new FrameFailure("the stream has no usable video", true);
        }

        return new FrameFailure("the stream could not be read now", false);
    }

    /// <summary>The seconds in at which a grab is tried, in order, until a frame is bright enough (Steam's streams often start with a black or fading intro).</summary>
    public static readonly string[] FrameSeeks = { "5", "12", "25", "40" };

    /// <summary>A frame whose mean brightness (0 to 255, <see cref="JpegLuma"/>) is below this is "too dark": black frames of Steam clips measure below 3, genuinely dark scenes about 8 to 10, the rest 16 and up.</summary>
    public const double DarkLimit = 5.0;

    /// <summary>How long one run of the encoder may take (the whole grab with its retries has <see cref="FrameTimeout"/>).</summary>
    public static readonly TimeSpan FrameRunTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a grab whose frames were all too dark is remembered (a marker of its own class, ".dark").</summary>
    public static readonly TimeSpan DarkRetry = TimeSpan.FromDays(1);

    // One run of the encoder at <seek> seconds: the temporary picture, or null when it ended without one (exit 0); a failure throws.
    private static async Task<string?> RunOnce(string encoder, IReadOnlyList<string> leading, string url, string outputDirectory, string seek, CancellationToken cancellationToken)
    {
        var output = Path.Combine(outputDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        var arguments = FrameArguments(url, output, seek) ?? throw new FrameFailure("the stream address is not allowed", true);
        var start = new System.Diagnostics.ProcessStartInfo { FileName = encoder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in leading.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        run.CancelAfter(FrameRunTimeout);
        string errorText;
        using (var process = new System.Diagnostics.Process { StartInfo = start })
        {
            try
            {
                process.Start();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new FrameFailure("ffmpeg could not be started", false);
            }

            var error = process.StandardError.ReadToEndAsync();
            var standard = process.StandardOutput.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(run.Token).ConfigureAwait(false);
                var text = await error.ConfigureAwait(false);
                errorText = text.Length > 4000 ? text[^4000..] : text;
                _ = await standard.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already gone
                }

                DeleteQuietly(output);
                throw new FrameFailure("ffmpeg took too long", false);
            }

            if (process.ExitCode != 0)
            {
                DeleteQuietly(output);
                throw Classify(errorText);
            }
        }

        if (File.Exists(output) && new FileInfo(output).Length > 0)
        {
            return output;
        }

        DeleteQuietly(output);
        return null;
    }

    /// <summary>
    /// One grab by the encoder binary itself (<paramref name="encoder"/> + <paramref name="leading"/> + <see cref="FrameArguments"/> in a <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>): the finished
    /// temporary picture inside <paramref name="outputDirectory"/>, or a <see cref="FrameFailure"/>. At 5 seconds in (at 0 when the stream is shorter); a frame that is too dark
    /// (<see cref="DarkLimit"/>, <see cref="JpegLuma"/>) is tried again at 12, 25 and 40 seconds, and when all are too dark no frame is kept and the grab fails with a failure of its own class (dark,
    /// <see cref="DarkRetry"/>). Each run has <see cref="FrameRunTimeout"/>; a cancelled <paramref name="cancellationToken"/> (the whole grab's limit) kills the process and its children. stderr is read for
    /// <see cref="Classify"/> and never kept or logged.
    /// </summary>
    public static async Task<string?> RunEncoder(string encoder, IReadOnlyList<string> leading, string url, string outputDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(encoder) || (leading.Count == 0 && !File.Exists(encoder)))
        {
            throw new FrameFailure("no ffmpeg is available", false);
        }

        Directory.CreateDirectory(outputDirectory);
        var dark = false;
        foreach (var seek in FrameSeeks)
        {
            var picture = await RunOnce(encoder, leading, url, outputDirectory, seek, cancellationToken).ConfigureAwait(false);
            var shorter = picture is null && seek == "5";
            if (shorter)
            {
                // the stream is shorter than 5 s: its start
                picture = await RunOnce(encoder, leading, url, outputDirectory, "0", cancellationToken).ConfigureAwait(false);
            }

            if (picture is null)
            {
                // the end of the stream
                break;
            }

            var mean = JpegLuma.Mean(picture);
            if (mean is null || mean >= DarkLimit)
            {
                return picture;
            }

            // too dark: no frame is kept of this one, a later offset may be real
            DeleteQuietly(picture);
            dark = true;
            if (shorter)
            {
                break;
            }
        }

        throw dark ? new FrameFailure("every frame tried is too dark", false, true) : new FrameFailure("the stream has no usable video", true);
    }

    /// <summary>The one host a still frame is grabbed from (the player and every stored master address use it, and so do the playlists and segments of a clip).</summary>
    public const string FrameHost = "video.akamai.steamstatic.com";

    /// <summary>How long a failure that may pass (no network, a timeout, Steam 5xx, no encoder) is remembered; a stream that is gone or has no video is remembered <see cref="EmptyRetry"/> (30 days).</summary>
    public static readonly TimeSpan TransientRetry = TimeSpan.FromMinutes(10);

    /// <summary>The image tag of a still frame: "f" and the first 12 hex digits of the SHA-256 of the stream address (a new address gives a new frame).</summary>
    public static string FrameTag(SteamVideo video) => "f" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(video.Hls ?? string.Empty)), 0, 6).ToLowerInvariant();

    /// <summary>Where the still frame of an item is kept: &lt;cache&gt;\playnite-bridge\steam\&lt;id&gt;-f&lt;tag&gt;.jpg; a marker file ".none" next to it remembers a failed grab.</summary>
    public static string FramePath(string cachePath, SteamVideoItem item) =>
        Path.Combine(cachePath, "playnite-bridge", "steam", item.Id.ToString("N") + "-" + FrameTag(item.Video) + ".jpg");

    /// <summary>
    /// The address ffmpeg may be given: the stored HLS address when it is absolute https on <see cref="FrameHost"/> (no port, no user info) and holds no space, control
    /// character, quote or backslash (the address is put in quotes on ffmpeg's command line); returned in its escaped form. Null for anything else.
    /// </summary>
    public static string? FrameUrl(string? hls)
    {
        if (string.IsNullOrEmpty(hls) || hls.Length > 2000 || hls.Any(c => c <= ' ' || c == '"' || c == '\\' || c == '\'' || c == '`' || c > '~')
            || !hls.StartsWith("https://" + FrameHost + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // exactly Steam's video server over https: no other host (also no *.akamaihd.net), no port, no user info
        return Uri.TryCreate(hls, UriKind.Absolute, out var uri) && uri.Host == FrameHost && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
    }

    /// <summary>Failures that may pass in a row after which an item is remembered for <see cref="TransientPromoted"/> instead of <see cref="TransientRetry"/>.</summary>
    public const int TransientLimit = 3;

    /// <summary>How long an item is remembered after <see cref="TransientLimit"/> failures that may pass in a row (a success clears the count).</summary>
    public static readonly TimeSpan TransientPromoted = TimeSpan.FromDays(1);

    private static readonly ConcurrentDictionary<string, int> TransientCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How long a failed grab is remembered: 30 days for a stream that is gone, one day when every frame was too dark, 10 minutes for a failure that may pass (one day from the <see cref="TransientLimit"/>th in a row).</summary>
    public static TimeSpan RetryFor(bool permanent, bool dark, int transientCount) =>
        permanent ? EmptyRetry : dark ? DarkRetry : transientCount >= TransientLimit ? TransientPromoted : TransientRetry;

    /// <summary>Counts one more failure that may pass for the item's picture file <paramref name="target"/> (a ".tries" file beside it, and in memory if the file cannot be written); returns the count.</summary>
    public static int NoteTransient(string target)
    {
        var path = target + ".tries";
        var count = TransientCounts.GetValueOrDefault(target);
        try
        {
            if (File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out var stored))
            {
                count = Math.Max(count, stored);
            }

            count++;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(path, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the memory below keeps the count
        }

        TransientCounts[target] = count;
        return count;
    }

    // True while a failed grab of the item is remembered (a fresh marker file, else the in-memory time) and no frame exists.
    private static bool FrameFailureActive(string target)
    {
        var now = Clock();
        try
        {
            foreach (var marker in new[] { target + ".none", target + ".dark" })
            {
                if (File.Exists(marker) && now - File.GetLastWriteTimeUtc(marker) < EmptyRetry)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the memory below
        }

        return FrameFailedAt.TryGetValue(target, out var until) && now < until;
    }

    /// <summary>
    /// The still frame of an item, grabbed once from its HLS stream by <paramref name="extract"/> and kept in the cache folder (single flight per item, at most two grabs at a time
    /// (<see cref="FrameGate"/>), the wait for a slot is bounded by <see cref="FrameWait"/>); null when the stream address is not allowed, a grab failed within the last 30 days
    /// (<see cref="EmptyRetry"/>: a marker file and a memory, one warning per item and reason), or no slot was free in time (no failure recorded then). The grab does not depend on the request that
    /// asked for it.
    /// </summary>
    public static async Task<string?> Frame(string cachePath, SteamVideoItem item, FrameExtractor extract, ILogger? logger)
    {
        var url = FrameUrl(item.Video.Hls);
        if (url is null)
        {
            return null;
        }

        var target = FramePath(cachePath, item);
        if (File.Exists(target))
        {
            return target;
        }

        if (FrameFailureActive(target))
        {
            return null;
        }

        using var wait = new CancellationTokenSource(FrameWait);
        var gate = FrameLocks.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        try
        {
            await gate.WaitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            if (File.Exists(target))
            {
                return target;
            }

            if (FrameFailureActive(target))
            {
                return null;
            }

            try
            {
                await FrameGate.WaitAsync(wait.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }

            string? temporary = null;
            string? failure;
            var permanent = false;
            var dark = false;
            try
            {
                using var timeout = new CancellationTokenSource(FrameTimeout);
                temporary = await extract(url, timeout.Token).ConfigureAwait(false);
                var length = temporary is not null && File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
                failure = temporary is null ? "ffmpeg made no picture" : length == 0 ? "the picture is empty" : length > MaxThumbnailBytes ? "the picture is larger than 2 MB"
                    : !IsImage(await ReadHead(temporary).ConfigureAwait(false)) ? "the picture is no JPEG, PNG or WebP" : null;
                permanent = false;
            }
            catch (FrameFailure ex)
            {
                failure = ex.Message;
                permanent = ex.Permanent;
                dark = ex.Dark;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // our own wording: an exception's text can hold an address or a path
                failure = ex is OperationCanceledException ? "ffmpeg took too long" : "the grab failed";
            }
            finally
            {
                FrameGate.Release();
            }

            if (failure is null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Move(temporary!, target, true);
                    FrameFailures.TryRemove(item.Id, out _);
                    FrameFailedAt.TryRemove(target, out _);
                    foreach (var marker in new[] { target + ".none", target + ".dark", target + ".tries" })
                    {
                        if (File.Exists(marker))
                        {
                            File.Delete(marker);
                        }
                    }

                    TransientCounts.TryRemove(target, out _);
                    return target;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failure = "the picture could not be kept";
                }
            }

            DeleteQuietly(temporary);
            // 30 days for a stream that is gone (".none"), 1 day when every frame was too dark (".dark"), 10 minutes for a failure that may pass (".none"): the marker's time is set so that it
            // counts as 30 days old when the shorter time is up
            // a failure that may pass is counted per item (sidecar ".tries"): the third one in a row is remembered like a dark picture (one day), so a clip that always times out is not tried every ten minutes for ever
            var tries = permanent || dark ? 0 : NoteTransient(target);
            var retry = RetryFor(permanent, dark, tries);
            var markerPath = target + (dark ? ".dark" : ".none");
            FrameFailedAt[target] = Clock() + retry;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(markerPath, Array.Empty<byte>());
                File.SetLastWriteTimeUtc(markerPath, Clock() + retry - EmptyRetry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the memory above keeps the failure
            }

            if (!FrameFailures.TryGetValue(item.Id, out var known) || known != failure)
            {
                FrameFailures[item.Id] = failure;
                logger?.LogWarning("PlayniteBridge: cannot make a still frame of the Steam video {Id}: {Reason}", item.Id.ToString("N"), failure);
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<byte[]> ReadHead(string path)
    {
        var head = new byte[12];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var read = await stream.ReadAsync(head).ConfigureAwait(false);
        return head[..read];
    }

    private static void DeleteQuietly(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a leftover temporary file is the encoder's temp folder's business
        }
    }

    /// <summary>
    /// The backdrop of the game that stands in for the picture of a video (the last resort): none for no backdrop; the only one for one; with several NOT the first (it is sometimes the
    /// game's main one) but the second, else the next existing one; the first only when no other file exists.
    /// </summary>
    public static string? PickBackdrop(IReadOnlyList<string> backdrops, Func<string, bool> exists)
    {
        for (var i = 1; i < backdrops.Count; i++)
        {
            if (exists(backdrops[i]))
            {
                return backdrops[i];
            }
        }

        return backdrops.Count > 0 && exists(backdrops[0]) ? backdrops[0] : null;
    }

    /// <summary>A neutral picture for a video without any: a dark 16:9 rectangle with a play triangle, no text (nothing to translate).</summary>
    public static byte[] NeutralTile() => Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 320 180\"><rect width=\"320\" height=\"180\" fill=\"#2a2e38\"/><circle cx=\"160\" cy=\"90\" r=\"34\" fill=\"#3a3f4c\"/><path d=\"M148 72l30 18-30 18z\" fill=\"#e9ecf2\"/></svg>");

    /// <summary>What a request for the picture of a Steam video gets: Kind = Thumbnail or Frame (final, kept under the item's tag), Backdrop (the game's own file) or Neutral (Path null, <see cref="NeutralTile"/>).</summary>
    public sealed record SteamPicture(string Kind, string? Path);

    /// <summary>
    /// The picture of a Steam video in the order Steam thumbnail, still frame, backdrop of the game, neutral tile; never null. The thumbnail is not asked from Steam again within the hour
    /// <see cref="ThumbnailMissing"/> remembers; the frame is only grabbed when there is no thumbnail (never for a healthy item).
    /// </summary>
    public static async Task<SteamPicture> Picture(string cachePath, SteamVideoItem item, FrameExtractor? extract, Func<IReadOnlyList<string>> backdrops, ILogger? logger)
    {
        if (!ThumbnailMissing(cachePath, item) && await Thumbnail(cachePath, item, logger, CancellationToken.None).ConfigureAwait(false) is { } thumbnail)
        {
            return new SteamPicture("Thumbnail", thumbnail);
        }

        if (extract is not null && await Frame(cachePath, item, extract, logger).ConfigureAwait(false) is { } frame)
        {
            return new SteamPicture("Frame", frame);
        }

        return PickBackdrop(backdrops(), File.Exists) is { } backdrop ? new SteamPicture("Backdrop", backdrop) : new SteamPicture("Neutral", null);
    }
}
