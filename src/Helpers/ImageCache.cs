using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The game folders &lt;cache folder&gt;\&lt;title [short id]&gt;\ (one per game, i.e. per version and twin, <see cref="GameExtras.FolderName"/>) with the images beside
/// the user's extras. Images there are found by the names, order and rules of Jellyfin's LocalImageProvider (10.10.7) for a movie in its own
/// folder (<see cref="Image"/>, <see cref="Backdrops"/>), plus the user's icon.png. A user's file there always wins over Playnite and Steam and is
/// never changed or deleted. With Artwork Copies "All" every image without a user's file is copied unchanged (bytes and time) from its source
/// under poster, backdrop, clearlogo, banner, landscape (the thumb) or icon with the source's extension (else the one its content shows) and served
/// from there; the plugin's copies are told apart from the user's files only by the list of copies
/// (<see cref="CopiesFile"/>: listed with their time and length). Images are never read from Playnite or Steam in place. The
/// only code that writes or deletes there: it writes copies, deletes only its own listed copies and empty folders. Never writes into Playnite's or Steam's folder. Image sizes are made by Jellyfin's
/// own image processor (<see cref="ProcessingOptions"/>), not here.
/// </summary>
public sealed class ImageCache
{
    public const string BackdropFile = "backdrop.jpg";
    public const string LogoFile = "clearlogo.png"; // the copy's name as fanart.tv names it; a user's logo.png still counts
    public const string BannerFile = "banner.jpg";
    public const string ThumbFile = "landscape.jpg";
    public const string IconFile = "icon.png";

    // The smaller side Playnite's icon needs to be shown in the list view.
    private const int MinIconSize = 64;

    // The kind of the list view icon in the list of copies; the other kinds are ImageType names.
    private const string IconKind = "Icon";

    // The kinds the plugin copies with Artwork Copies "All".
    private static readonly string[] CopiedKinds = { "Primary", "Backdrop", "Logo", "Banner", "Thumb", IconKind };

    // How long after its first change the list of copies is written (one timer); Sync Artwork and the clean-up write it at once.
    private static readonly TimeSpan CopiesSaveDelay = TimeSpan.FromSeconds(5);

    // The list of copies (full path -> copy) as read from CopiesFile; null = not usable. Shared by the task and the filter.
    private static readonly object CopiesLock = new();
    private static Dictionary<string, ImageCopy>? _copies;
    private static string? _copiesOf;
    private static bool _copiesDirty;
    private static long _copiesVersion;
    private static bool _copiesSaveArmed;
    private static Timer? _copiesTimer;

    // Serialises the writes of the list of copies; the file is written outside CopiesLock.
    private static readonly object CopiesWriteLock = new();

    // Jellyfin's names of the single images of a video in its own folder, first found wins (as LocalImageProvider).
    internal static readonly (ImageType Type, string[] Names)[] SingleNames =
    {
        (ImageType.Primary, new[] { "poster", "folder", "cover", "default", "movie" }),
        (ImageType.Logo, new[] { "logo", "clearlogo" }),
        (ImageType.Art, new[] { "clearart" }),
        (ImageType.Disc, new[] { "disc", "cdart", "discart" }),
        (ImageType.Banner, new[] { "banner" }),
        (ImageType.Thumb, new[] { "landscape", "thumb" })
    };

    // Jellyfin's numbered backdrop names (first name, prefix of 1 to 20) before and after the extrafanart folder (as LocalImageProvider).
    internal static readonly (string First, string Prefix)[] BackdropNames = { ("fanart", "fanart-"), ("background", "background-"), ("art", "art-") };

    private static readonly FolderImages NoImages = new(DateTime.MinValue, new Dictionary<ImageType, string>(), Array.Empty<string>(), null, null, new Dictionary<string, OwnCopy>(), Array.Empty<string>(), new Dictionary<string, string>());

    // Junctions, symbolic links and hidden folders are no game folders (the skip rule of the extras scan).
    private static readonly EnumerationOptions FolderOptions = new() { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden };

    private static readonly TimeSpan FolderReuse = TimeSpan.FromSeconds(10);

    // One lock per game and kind, so the task and an image request never copy the same file at once.
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    // Game files whose copy failed, with the source time it failed for: not tried again until the source changes.
    private static readonly ConcurrentDictionary<string, DateTime> Failed = new(StringComparer.OrdinalIgnoreCase);

    // Guards the folder list and the creation of a game folder, so one game never gets two folders.
    private static readonly object FolderLock = new();

    private readonly PluginConfiguration _config;
    private readonly string _playniteFolder;
    private readonly string? _steamFolder;
    private readonly ILogger _logger;
    // The images found per game folder name, from one listing of the folder that is read again after 10 seconds or after a copy.
    private readonly ConcurrentDictionary<string, FolderImages> _images = new(StringComparer.OrdinalIgnoreCase);
    private int _written;
    private int _replaced;
    private int _deleted;
    private int _converted;
    private int _unrecognised;
    private Dictionary<string, string>? _folders;
    private DateTime _foldersListed;

    public ImageCache(PluginConfiguration config, string playniteFolder, string cacheFolder, string? steamFolder, JellyfinFolders jellyfin, ILogger logger)
    {
        _config = config;
        _playniteFolder = playniteFolder;
        _steamFolder = steamFolder;
        _logger = logger;
        CacheFolder = cacheFolder;
        PathError = CheckPaths(cacheFolder, playniteFolder, steamFolder, jellyfin);
    }

    public string CacheFolder { get; }

    /// <summary>Why nothing may be written to the cache folder (checks 1 to 5 of <see cref="ValidateCacheFolder"/>), or null.</summary>
    public string? PathError { get; }

    /// <summary>Number of new copies this instance has written.</summary>
    public int Written => _written;

    /// <summary>Number of copies this instance has replaced (deleted and written again from the current source).</summary>
    public int Replaced => _replaced;

    /// <summary>Number of copies this instance has deleted without a new one.</summary>
    public int Deleted => _deleted;

    /// <summary>Number of icon.ico copies this instance has replaced by an icon.png.</summary>
    public int IconsConverted => _converted;

    /// <summary>Number of icon.ico files in game folders this instance found that are not the plugin's own copies and left alone.</summary>
    public int IcoNotRecognised => _unrecognised;

    /// <summary>
    /// The list of the plugin's copies in the game folders (set at start: &lt;DataPath&gt;\playnite-bridge\artwork.json). Empty or unreadable = no list: nothing is copied or deleted and every file in a game folder counts as the user's.
    /// </summary>
    public static string CopiesFile { get; set; } = string.Empty;

    /// <summary>The configured cache folder; empty = &lt;internal metadata path&gt;\Games.</summary>
    public static string ResolveCacheFolder(string configured, string internalMetadataPath) =>
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(internalMetadataPath, "Games") : configured;

    /// <summary>Steam's folder from the registry, read only; null if it is not set.</summary>
    public static string? SteamFolder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        string? path;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
        {
            path = key?.GetValue("SteamPath") as string;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            path = key?.GetValue("InstallPath") as string;
        }

        if (string.IsNullOrWhiteSpace(path) || !IsDrivePath(path))
        {
            return null;
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    /// Checks a cache folder: (1) absolute, (2) not the Playnite folder or inside it, (3) not the Steam
    /// folder or inside it, (4) not Jellyfin's People or Studio folder or inside them and not a Jellyfin library folder, inside one
    /// or containing one (<paramref name="jellyfin"/>), (6) writable (creates the folder, writes and deletes a probe file). Null =
    /// fine, else the reason.
    /// </summary>
    public static string? ValidateCacheFolder(string folder, string playniteFolder, string? steamFolder, JellyfinFolders jellyfin)
    {
        var error = CheckPaths(folder, playniteFolder, steamFolder, jellyfin);
        if (error is not null)
        {
            return error;
        }

        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $"playnite-bridge-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The game folder is not writable: " + ex.Message;
        }
    }

    /// <summary>
    /// The options for Jellyfin's <see cref="IImageProcessor.ProcessImage"/> as Jellyfin's ImageController builds them, without item and
    /// without the played, blur, colour and layer parameters: sizes from the request, quality 100 when none is given, output formats from
    /// the format parameter, else from the Accept header. The original's width and height are read from its file header, since without
    /// them Jellyfin hands back the original for a request with only fillWidth/fillHeight; they stay 0 when they cannot be read.
    /// </summary>
    public static ImageProcessingOptions ProcessingOptions(
        IImageProcessor processor,
        string path,
        ImageType type,
        int? width,
        int? height,
        int? maxWidth,
        int? maxHeight,
        int? fillWidth,
        int? fillHeight,
        int? quality,
        ImageFormat? format,
        string[] acceptTypes,
        string? acceptParam,
        string userAgent)
    {
        var image = new ItemImageInfo { Path = path, Type = type, DateModified = File.GetLastWriteTimeUtc(path) };
        try
        {
            var size = processor.GetImageDimensions(path);
            image.Width = size.Width;
            image.Height = size.Height;
        }
        catch (Exception)
        {
            // Width and height stay 0: Jellyfin then decides without the original's size.
        }

        return new ImageProcessingOptions
        {
            Image = image,
            Width = width,
            Height = height,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            FillWidth = fillWidth,
            FillHeight = fillHeight,
            Quality = quality ?? 100,
            SupportedOutputFormats = OutputFormats(format, acceptTypes, acceptParam, userAgent)
        };
    }

    /// <summary>Image tag from the file's last write time and length; null if the file does not exist.</summary>
    public static string? Tag(string? path)
    {
        var info = path is null ? null : new FileInfo(path);
        if (info is null || !info.Exists)
        {
            return null;
        }

        var bytes = BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks).Concat(BitConverter.GetBytes(info.Length)).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes), 0, 6).ToLowerInvariant();
    }

    // Content fingerprints by full path, valid while the file's length and write time are the same.
    private static readonly ConcurrentDictionary<string, (long Length, long Ticks, string Hash)> ContentKeys = new(StringComparer.OrdinalIgnoreCase);
    private static long _contentHashed;

    /// <summary>Number of files whose content <see cref="ContentKey"/> has read so far (not counting answers from its cache).</summary>
    public static long ContentHashed => Interlocked.Read(ref _contentHashed);

    /// <summary>
    /// Content fingerprint of a file: the SHA-256 of its bytes (streamed) as lower-case hex, cached by length and write time. Not the image
    /// <see cref="Tag"/> (that one follows time and length, so identical copies in two game folders differ). Null for a missing file or any failure
    /// (locked, unreadable, gone while reading); never throws. Accepted limit: a replacement of the same size that keeps the write time is not noticed
    /// until the next restart (the image <see cref="Tag"/> has the same property). Hashing runs synchronously on the caller's thread: the first
    /// switch of a game whose backdrops have equal sizes pays one cold read per file (accepted; later calls come from the cache).
    /// </summary>
    public static string? ContentKey(string? path)
    {
        try
        {
            var info = path is null ? null : new FileInfo(path);
            if (info is null || !info.Exists)
            {
                return null;
            }

            var length = info.Length;
            var ticks = info.LastWriteTimeUtc.Ticks;
            if (ContentKeys.TryGetValue(info.FullName, out var known) && known.Length == length && known.Ticks == ticks)
            {
                return known.Hash;
            }

            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            Interlocked.Increment(ref _contentHashed);
            // A file that changed while it was read is not cached under the old length and time.
            info.Refresh();
            if (info.Length == length && info.LastWriteTimeUtc.Ticks == ticks)
            {
                ContentKeys[info.FullName] = (length, ticks, hash);
            }

            return hash;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The image of <paramref name="type"/> of a version for an image request, read only: as <see cref="Image"/> or for the backdrop the first of
    /// <see cref="Backdrops"/>. Copies are made and replaced only by Sync Artwork.
    /// </summary>
    public string? Get(GameGroup group, GameEntry game, ImageType type)
    {
        return type == ImageType.Backdrop ? Backdrops(group, game).FirstOrDefault() : Image(group, game, type);
    }

    /// <summary>
    /// The file name (without extension) of the plugin's copy of a kind, from the Sources table: the chosen Jellyfin name, or the own name while the choice
    /// is "Own"; an empty or unusable own name falls back to the default. Only a plain name: no path, no characters Windows does not allow.
    /// </summary>
    internal string CopyName(string kind)
    {
        var (chosen, own, fallback) = kind switch
        {
            "Primary" => (_config.CoverFileName, _config.CoverOwnName, "poster"),
            "Backdrop" => (_config.BackdropFileName, _config.BackdropOwnName, "backdrop"),
            "Logo" => (_config.LogoFileName, _config.LogoOwnName, "clearlogo"),
            "Banner" => (_config.BannerFileName, _config.BannerOwnName, "banner"),
            "Thumb" => (_config.ThumbFileName, _config.ThumbOwnName, "landscape"),
            IconKind => (_config.IconFileName, _config.IconOwnName, "icon"),
            _ => (kind.ToLowerInvariant(), string.Empty, kind.ToLowerInvariant())
        };
        var name = string.Equals(chosen, "Own", StringComparison.OrdinalIgnoreCase) ? own : chosen;
        name = (name ?? string.Empty).Trim();
        return name.Length == 0 || name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') ? fallback : name;
    }

    /// <summary>True when a copy of a kind carries the name the settings ask for now (a renamed setting makes it be replaced by Sync Artwork).</summary>
    private bool NameFits(string path, string kind) =>
        Path.GetFileNameWithoutExtension(path).Equals(CopyName(kind), StringComparison.OrdinalIgnoreCase);

    /// <summary>The configured file names of a Jellyfin image type that a user's file may carry too (besides Jellyfin's own names).</summary>
    private IEnumerable<string> ConfiguredNames(ImageType type) => type switch
    {
        ImageType.Primary => new[] { CopyName("Primary") },
        ImageType.Logo => new[] { CopyName("Logo") },
        ImageType.Banner => new[] { CopyName("Banner") },
        ImageType.Thumb => new[] { CopyName("Thumb") },
        ImageType.Art => new[] { ConfiguredName(_config.ClearartFileName, _config.ClearartOwnName, "clearart") },
        ImageType.Disc => new[] { ConfiguredName(_config.DiscFileName, _config.DiscOwnName, "disc") },
        _ => Array.Empty<string>()
    };

    internal static string ConfiguredName(string chosen, string own, string fallback)
    {
        var name = (string.Equals(chosen, "Own", StringComparison.OrdinalIgnoreCase) ? own : chosen)?.Trim() ?? string.Empty;
        return name.Length == 0 || name.Length > 100 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') ? fallback : name;
    }

    /// <summary>
    /// The games of a group that have an item of their own: its versions and each twin left out of it (<paramref name="visible"/> = the visible
    /// games by id; without it only the versions). Each has its own game folder.
    /// </summary>
    public static IEnumerable<GameEntry> GamesOf(GameGroup group, IReadOnlyDictionary<string, GameEntry>? visible = null) =>
        group.Versions.Concat(visible is null ? Enumerable.Empty<GameEntry>() : (group.Twins?.Keys ?? Enumerable.Empty<string>()).Select(id => visible[id]));

    /// <summary>Creates the game folder of every visible game (each version and twin of every group) that has none (Sync Playnite); a folder that cannot be made is warned about once and skipped.</summary>
    public int EnsureFolders(IEnumerable<GameGroup> groups, IReadOnlyDictionary<string, GameEntry>? visible = null)
    {
        var made = 0;
        if (PathError is not null)
        {
            return 0;
        }

        foreach (var game in groups.SelectMany(g => GamesOf(g, visible)))
        {
            try
            {
                EnsureFolder(game);
                made++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                _logger.LogWarning("PlayniteBridge: cannot create the game folder of {Id} in \"{Folder}\": {Message}", game.Id, CacheFolder, ex.Message);
            }
        }

        return made;
    }

    /// <summary>
    /// Creates the folder of every game of a group (<see cref="GamesOf"/>) that has none; with Artwork Copies "All" makes, in each game's own
    /// folder, every missing copy of its own source picture and, per the kind's mode, replaces or deletes every copy that no longer fits (other or
    /// changed source, source deselected, a user's file of the kind).
    /// </summary>
    public void BuildGroup(GameGroup group, IImageProcessor processor, IReadOnlyDictionary<string, GameEntry>? visible = null)
    {
        foreach (var game in GamesOf(group, visible))
        {
            try
            {
                EnsureFolder(game);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Warned once per game and run (e.g. a path longer than Windows allows); its images are not tried.
                _logger.LogWarning("PlayniteBridge: cannot create the game folder of {Id} in \"{Folder}\": {Message}", game.Id, CacheFolder, ex.Message);
                continue;
            }

            foreach (var kind in CopiedKinds)
            {
                Ensure(group, game, kind, true, processor);
            }
        }
    }

    /// <summary>
    /// The image of a single type of a version, read only: the user's file Jellyfin's rules find in the version's own folder, whatever the type's source
    /// setting; else for Primary, Logo, Banner and Thumb the plugin's copy in it, whatever its source says now; null otherwise.
    /// </summary>
    public string? Image(GameGroup group, GameEntry game, ImageType type)
    {
        var found = PathError is null ? Find(game) : NoImages;
        return type is ImageType.Primary or ImageType.Logo or ImageType.Banner or ImageType.Thumb
            ? Answer(found, type.ToString())
            : found.Single.GetValueOrDefault(type);
    }

    /// <summary>
    /// The full path of the existing folder of a game (found as <see cref="FindFolder"/> finds it); null without one or a writable cache
    /// folder. Creates nothing.
    /// </summary>
    public string? GameFolder(GameEntry game)
    {
        var folder = PathError is null ? FindFolder(game, false) : null;
        var path = folder is null ? null : Path.Combine(CacheFolder, folder);
        return path is not null && Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// The backdrops of a version in order, read only: the user's files Jellyfin's rules find in its own folder, else the plugin's copy while it
    /// is there; empty when there is none. A copy keeps the time and length of its source, so its tag equals the source's.
    /// </summary>
    public IReadOnlyList<string> Backdrops(GameGroup group, GameEntry game)
    {
        var found = PathError is null ? Find(game) : NoImages;
        if (found.Backdrops.Count > 0)
        {
            return found.Backdrops;
        }

        var single = Answer(found, ImageType.Backdrop.ToString());
        return single is null ? Array.Empty<string>() : new[] { single };
    }

    /// <summary>The box picture of a version ("box3d", "boxfront", "boxback" or "boxspine"): the user's file in its own folder (the name from the Sources table); null without one or for any other key. Never copied.</summary>
    public string? Box(GameEntry game, string key) => (PathError is null ? Find(game) : NoImages).Boxes.GetValueOrDefault(key);

    /// <summary>
    /// The disc files of a version without repeats: <see cref="Discs"/> in its order, a file whose bytes equal an earlier one's left out (disc.png and cdart.png of the same
    /// picture count once). Files of different length are never equal, so only equal lengths are hashed.
    /// </summary>
    public IReadOnlyList<string> DistinctDiscs(GameGroup group, GameEntry game)
    {
        var kept = new List<string>();
        foreach (var file in Discs(group, game))
        {
            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var sameSize = kept.Where(k => { try { return new FileInfo(k).Length == length; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; } }).ToList();
            var key = sameSize.Count == 0 ? null : ContentKey(file);
            if (key is null || !sameSize.Any(k => ContentKey(k) == key))
            {
                kept.Add(file);
            }
        }

        return kept;
    }

    /// <summary>All disc files of a version in order (the first is <see cref="Image"/> of Disc), read only: plain names first, then the numbered ones; empty without one.</summary>
    public IReadOnlyList<string> Discs(GameGroup group, GameEntry game) => (PathError is null ? Find(game) : NoImages).Discs;

    /// <summary>
    /// The list view icon of a version: icon.png of the user in its own folder (any size), else the plugin's copy of Playnite's icon; null otherwise.
    /// </summary>
    public string? Icon(GameGroup group, GameEntry game)
    {
        return Answer(PathError is null ? Find(game) : NoImages, IconKind);
    }

    /// <summary>The square list view image of a version: the user's square file (name from the Sources table) in its own folder; null without one. Never copied.</summary>
    public string? Square(GameEntry game) => (PathError is null ? Find(game) : NoImages).Square;

    /// <summary>Writes the list of copies when it changed since it was last written (the end of Sync Artwork).</summary>
    public void Flush()
    {
        SaveCopies();
        Interlocked.Increment(ref _flushes);
    }

    private static int _flushes;

    /// <summary>How many times a Sync Artwork ended (the list of copies was flushed) since the server started (GlobeLink.DataStamp).</summary>
    public static int Flushes => Volatile.Read(ref _flushes);

    /// <summary>
    /// A file of Steam's library cache for the game: librarycache\&lt;AppID&gt;\&lt;name&gt;, else the newest one exactly one
    /// sub-folder deeper (no junctions followed). Null if Steam is unknown, the game is no Steam game (other stores' ids can be
    /// numeric too), the AppID is not numeric or there is no such file.
    /// </summary>
    public string? SteamFile(GameEntry game, string name)
    {
        if (_steamFolder is null || game.StoreKey != "steam" || string.IsNullOrEmpty(game.SteamAppId) || !game.SteamAppId.All(char.IsAsciiDigit))
        {
            return null;
        }

        var dir = Path.Combine(_steamFolder, "appcache", "librarycache", game.SteamAppId);
        var flat = Path.Combine(dir, name);
        if (File.Exists(flat))
        {
            return flat;
        }

        if (!Directory.Exists(dir))
        {
            return null;
        }

        return new DirectoryInfo(dir).EnumerateDirectories()
            .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
            .Select(d => new FileInfo(Path.Combine(d.FullName, name)))
            .Where(f => f.Exists)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Deletes the plugin's listed copies (<see cref="Delete"/>) with Artwork Copies "Nothing" and in folders whose short id belongs to none of
    /// <paramref name="games"/> (a listed file the user changed or removed only leaves the list), then every game folder that is empty and whose
    /// short id belongs to none of them; never another file. Only direct sub-folders named "... [8 hex]", never a junction. Deletes nothing when
    /// the list is empty or the cache folder is a link. Returns the number of folders deleted (copies: <see cref="Deleted"/>).
    /// </summary>
    public int CleanUp(IReadOnlyCollection<GameEntry> games)
    {
        if (PathError is not null || !Directory.Exists(CacheFolder))
        {
            return 0;
        }

        // Never deletes through a link: the cache folder itself must not be a junction or symbolic link.
        if (IsLink(Path.GetFullPath(CacheFolder)))
        {
            _logger.LogWarning("PlayniteBridge: the game folder \"{Folder}\" lies behind a link, it is not cleaned up", CacheFolder);
            return 0;
        }

        if (games.Count == 0)
        {
            _logger.LogWarning("PlayniteBridge: no games read from Playnite, the game folders are not cleaned up");
            return 0;
        }

        var keep = games.Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet();
        List<KeyValuePair<string, ImageCopy>> listed;
        lock (CopiesLock)
        {
            listed = Copies()?.ToList() ?? new List<KeyValuePair<string, ImageCopy>>();
        }

        var root = Path.GetFullPath(CacheFolder).TrimEnd('\\', '/');
        foreach (var (path, copy) in listed)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is null || !string.Equals(Path.GetDirectoryName(dir), root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Owned(new FileInfo(path), copy))
            {
                Drop(path);
            }
            else if ((_config.ArtworkCopy == ArtworkCopyMode.Nothing || GameExtras.ShortId(Path.GetFileName(dir)) is not string id || !keep.Contains(id))
                && Delete(path))
            {
                Interlocked.Increment(ref _deleted);
            }
        }

        SaveCopies();
        var folders = 0;
        var everything = new EnumerationOptions { AttributesToSkip = 0 };
        foreach (var dir in new DirectoryInfo(CacheFolder).EnumerateDirectories().ToList())
        {
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0 || GameExtras.ShortId(dir.Name) is not string shortId)
            {
                continue;
            }

            if (!keep.Contains(shortId) && !dir.EnumerateFileSystemInfos("*", everything).Any())
            {
                // Not recursive: a folder that is no longer empty is never deleted.
                try
                {
                    DeleteEmptyFolder(dir);
                    folders++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning("PlayniteBridge: could not delete the empty folder \"{Folder}\": {Message}", dir.FullName, ex.Message);
                }
            }
        }

        return folders;
    }

    // An empty folder of a gone game; one that is only read-only (Windows folder customisation sets +r) gets the bit cleared and one more try.
    private static void DeleteEmptyFolder(DirectoryInfo dir)
    {
        try
        {
            dir.Delete(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dir.Refresh();
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReadOnly) == 0)
            {
                throw;
            }

            var before = dir.Attributes;
            dir.Attributes = before & ~FileAttributes.ReadOnly;
            try
            {
                dir.Delete(false);
            }
            catch (Exception)
            {
                dir.Attributes = before;
                throw;
            }
        }
    }

    /// <summary>
    /// The name of the folder of a game in the cache folder (the one with its own short id), from a short id index (<see cref="GameExtras.FolderIndex"/>)
    /// of a listing read again after 10 seconds or when <paramref name="fresh"/>; null if it has none.
    /// </summary>
    private string? FindFolder(GameEntry game, bool fresh)
    {
        lock (FolderLock)
        {
            if (fresh || _folders is null || DateTime.UtcNow - _foldersListed > FolderReuse)
            {
                _folders = GameExtras.FolderIndex(Directory.Exists(CacheFolder)
                    ? new DirectoryInfo(CacheFolder).EnumerateDirectories("*", FolderOptions).Select(d => d.Name)
                    : Enumerable.Empty<string>());
                _foldersListed = DateTime.UtcNow;
            }

            return _folders.GetValueOrDefault(GameExtras.ShortIdOf(game.Id));
        }
    }

    /// <summary>
    /// The folder of a game, created by <see cref="GameExtras.FolderName"/> when the cache folder (read again) has none; an existing folder is
    /// always used as it is, never renamed.
    /// </summary>
    private string EnsureFolder(GameEntry game)
    {
        lock (FolderLock)
        {
            var found = FindFolder(game, false) ?? FindFolder(game, true);
            if (found is not null)
            {
                return found;
            }

            var name = GameExtras.FolderName(game.Name, game.Id);
            Directory.CreateDirectory(Path.Combine(CacheFolder, name));
            _folders![GameExtras.ShortIdOf(game.Id)] = name;
            return name;
        }
    }

    /// <summary>
    /// The images in the folder of a game from one listing of it, reused for up to 10 seconds; none without a folder. Read as Jellyfin's
    /// LocalImageProvider reads a movie's own folder: only direct files with one of <see cref="BaseItem.SupportedImageExtensions"/>, ordered by
    /// that list, empty files skipped, the name without extension compared whole and case-insensitively; hidden files and links skipped. The
    /// plugin's copies (listed with their current time and length) are kept apart: they are no user's files.
    /// </summary>
    private FolderImages Find(GameEntry game)
    {
        var folder = FindFolder(game, false);
        if (folder is null)
        {
            return NoImages;
        }

        if (_images.TryGetValue(folder, out var known) && DateTime.UtcNow - known.Listed <= FolderReuse)
        {
            return known;
        }

        var listed = DateTime.UtcNow;
        var dir = new DirectoryInfo(Path.Combine(CacheFolder, folder));
        List<FileSystemInfo> entries;
        try
        {
            entries = dir.Exists ? dir.EnumerateFileSystemInfos("*", FolderOptions).ToList() : new List<FileSystemInfo>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            entries = new List<FileSystemInfo>();
        }

        var own = new Dictionary<string, OwnCopy>();
        var userFiles = new List<FileInfo>();
        foreach (var file in entries.OfType<FileInfo>())
        {
            if (CopyOf(file.FullName) is ImageCopy copy && Owned(file, copy))
            {
                own.TryAdd(copy.Kind, new OwnCopy(file.FullName, copy));
            }
            else
            {
                userFiles.Add(file);
            }
        }

        // Ordered as LocalImageProvider orders them (case-sensitive index, so an upper-case extension comes first, as in Jellyfin).
        var files = userFiles.Where(f => IsImage(f) && f.Length > 0)
            .OrderBy(f => Array.IndexOf(BaseItem.SupportedImageExtensions, f.Extension)).ToList();
        string? Named(string name) => files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))?.FullName;

        var single = new Dictionary<ImageType, string>();
        foreach (var (type, names) in SingleNames)
        {
            // Disc: the plain names first (disc, cdart, discart, the configured one), then 1 to 20 appended to each of them in that order.
            var plain = names.Concat(ConfiguredNames(type)).ToList();
            var discs = type == ImageType.Disc ? plain.SelectMany(n => Enumerable.Range(1, 20).Select(i => n + i.ToString(CultureInfo.InvariantCulture))) : Enumerable.Empty<string>();
            if (plain.Concat(discs).Select(Named).FirstOrDefault(p => p is not null) is string path)
            {
                single[type] = path;
            }
        }

        // All discs of the folder in the order the single Disc is chosen: the plain names that exist (disc, cdart, discart, the configured one), then their numbered
        // forms 1 to 20 name by name; the first of the list is the single Disc.
        var discPlain = SingleNames.First(n => n.Type == ImageType.Disc).Names.Concat(ConfiguredNames(ImageType.Disc)).ToList();
        var discFiles = discPlain.Select(Named).OfType<string>()
            .Concat(discPlain.SelectMany(n => Enumerable.Range(1, 20).Select(i => Named(n + i.ToString(CultureInfo.InvariantCulture))).OfType<string>()))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // As LocalImageProvider.PopulateBackdrops: the first name, then the numbered ones until three numbers in total were missing.
        var backdrops = new List<string>();
        void Numbered(string first, string prefix)
        {
            if (Named(first) is string path)
            {
                backdrops.Add(path);
            }

            var unfound = 0;
            for (var i = 1; i <= 20; i++)
            {
                if (Named(prefix + i.ToString(CultureInfo.InvariantCulture)) is string numbered)
                {
                    backdrops.Add(numbered);
                }
                else if (++unfound >= 3)
                {
                    break;
                }
            }
        }

        foreach (var (first, prefix) in BackdropNames)
        {
            Numbered(first, prefix);
        }

        // The direct image files of extrafanart\, ordered by name.
        if (entries.OfType<DirectoryInfo>().FirstOrDefault(d => d.Name.Equals("extrafanart", StringComparison.OrdinalIgnoreCase)) is DirectoryInfo extra)
        {
            try
            {
                backdrops.AddRange(extra.EnumerateFiles("*", FolderOptions).Where(f => IsImage(f) && f.Length > 0)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => f.FullName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An unreadable extrafanart folder adds no backdrop.
            }
        }

        Numbered("backdrop", "backdrop");
        var backdropName = CopyName("Backdrop");
        if (!backdropName.Equals("backdrop", StringComparison.OrdinalIgnoreCase) && Named(backdropName) is string namedBackdrop)
        {
            backdrops.Add(namedBackdrop);
        }

        var iconName = CopyName(IconKind);
        var icon = userFiles.FirstOrDefault(f => f.Name.Equals(IconFile, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(f.Name).Equals(iconName, StringComparison.OrdinalIgnoreCase) && IsImage(f))?.FullName;
        var square = Named(ConfiguredName(_config.SquareFileName, _config.SquareOwnName, "square"));
        var boxes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, chosen, ownName) in new[]
        {
            ("box3d", _config.Box3dFileName, _config.Box3dOwnName), ("boxfront", _config.BoxFrontFileName, _config.BoxFrontOwnName),
            ("boxback", _config.BoxBackFileName, _config.BoxBackOwnName), ("boxspine", _config.BoxSpineFileName, _config.BoxSpineOwnName)
        })
        {
            if (Named(ConfiguredName(chosen, ownName, key)) is string boxFile)
            {
                boxes[key] = boxFile;
            }
        }

        var images = new FolderImages(listed, single, backdrops, icon, square, own, discFiles, boxes);
        _images[folder] = images;
        return images;
    }

    private static bool IsImage(FileInfo file) => BaseItem.SupportedImageExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase);

    /// <summary>Why a game folder is refused when it is one of the library folders <paramref name="locations"/>, lies inside one or contains one; null otherwise.</summary>
    public static string? LibraryError(string cacheFolder, IEnumerable<string> locations)
    {
        var full = Path.GetFullPath(cacheFolder);
        return locations.Select(FullPathOrNull).OfType<string>().Any(l => IsSameOrInside(full, l) || IsSameOrInside(l, full))
            ? "The game folder must not be inside a Jellyfin library or contain one." : null;
    }

    // A library location that cannot be resolved to a full path is skipped; it cannot hold the game folder.
    private static string? FullPathOrNull(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Why a game folder is refused for Jellyfin's own folders: People or Studio (or inside them), a library folder (inside one or containing
    /// one); null otherwise, also for a path that is no valid path (the other checks of <see cref="ValidateCacheFolder"/> refuse it).
    /// </summary>
    public static string? JellyfinError(string folder, JellyfinFolders jellyfin)
    {
        try
        {
            var full = Path.GetFullPath(folder);

            // Jellyfin's own folders of people and studios are never the plugin's.
            if (IsSameOrInside(full, jellyfin.PeoplePath) || IsSameOrInside(full, jellyfin.StudioPath))
            {
                return "The game folder must not be inside Jellyfin's People or Studio folder.";
            }

            // Jellyfin's library scan would take the game folders and their extras into its database.
            return LibraryError(full, jellyfin.LibraryLocations);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>Checks 1 to 5 of <see cref="ValidateCacheFolder"/> without the write probe (also for the extras folder, GameExtras.ExtrasFolderError); null = fine.</summary>
    public static string? CheckPaths(string folder, string playniteFolder, string? steamFolder, JellyfinFolders jellyfin)
    {
        // Only plain drive-letter paths: device paths (\\?\, \\.\), UNC paths and admin shares are refused unseen.
        if (string.IsNullOrWhiteSpace(folder) || !IsDrivePath(folder) || folder.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            return "The game folder must be an absolute path that starts with a drive letter.";
        }

        if (AreaCommon.PathPartError(folder) is string partError)
        {
            return partError;
        }

        var full = Path.GetFullPath(folder);
        if (IsLink(full))
        {
            return "The game folder must not be a junction or symbolic link.";
        }

        if (JellyfinError(full, jellyfin) is string jellyfinError)
        {
            return jellyfinError;
        }

        if (IsSameOrInside(full, playniteFolder))
        {
            return "The game folder must not be inside the Playnite folder.";
        }

        if (steamFolder is not null && IsSameOrInside(full, steamFolder))
        {
            return "The game folder must not be inside the Steam folder.";
        }

        return null;
    }

    private static bool IsDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    private static bool IsSameOrInside(string fullPath, string root)
    {
        var parent = Path.GetFullPath(root);
        var path = fullPath.TrimEnd('\\', '/');
        parent = parent.TrimEnd('\\', '/');
        return path.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True if the folder exists and is a junction or symbolic link (folders above it may be links).</summary>
    private static bool IsLink(string fullPath) =>
        Directory.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0;

    /// <summary>The sources of a kind in the order they are tried: P = Playnite's own file, S = Steam's, E = Playnite's ExtraMetadata (the logo only); none = Off.</summary>
    private IReadOnlyList<char> Order(string kind) => (kind switch
    {
        "Primary" => Tokens(_config.CoverSource),
        "Backdrop" => Tokens(_config.BackdropSource),
        IconKind => Tokens(_config.IconSource),
        "Logo" => _config.LogoSource switch
        {
            LogoImageSource.Steam => "S",
            LogoImageSource.ExtraMetadata => "E",
            LogoImageSource.ExtraMetadataThenSteam => "ES",
            LogoImageSource.SteamThenExtraMetadata => "SE",
            _ => string.Empty
        },
        "Banner" => _config.BannerSource == OptionalImageSource.Steam ? "S" : string.Empty,
        "Thumb" => _config.ThumbSource == OptionalImageSource.Steam ? "S" : string.Empty,
        _ => string.Empty
    }).ToCharArray();

    private static string Tokens(ImageSourceChoice choice) => choice switch
    {
        ImageSourceChoice.Playnite => "P",
        ImageSourceChoice.Steam => "S",
        ImageSourceChoice.SteamThenPlaynite => "SP",
        ImageSourceChoice.PlayniteThenSteam => "PS",
        _ => string.Empty
    };

    /// <summary>Steam's file of a kind: cover, hero, logo, header (Banner and Thumb) or the small client icon.</summary>
    private string? SteamKindFile(GameEntry game, string kind) => kind switch
    {
        "Primary" => SteamFile(game, "library_600x900.jpg"),
        "Backdrop" => SteamFile(game, "library_hero.jpg"),
        "Logo" => SteamFile(game, "logo.png"),
        "Banner" or "Thumb" => SteamFile(game, "header.jpg"),
        IconKind => SteamIconFile(game),
        _ => null
    };

    /// <summary>Steam's client icon of a game: the smallest file named with 40 hex digits in its library cache folder (a 32 x 32 jpg).</summary>
    private string? SteamIconFile(GameEntry game)
    {
        if (_steamFolder is null || game.StoreKey != "steam" || string.IsNullOrEmpty(game.SteamAppId) || !game.SteamAppId.All(char.IsAsciiDigit))
        {
            return null;
        }

        var dir = Path.Combine(_steamFolder, "appcache", "librarycache", game.SteamAppId);
        try
        {
            return Directory.Exists(dir)
                ? new DirectoryInfo(dir).EnumerateFiles()
                    .Where(f => f.Length > 0 && System.Text.RegularExpressions.Regex.IsMatch(f.Name, @"^[0-9a-f]{40}\.(jpg|png)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    .OrderBy(f => f.Length).FirstOrDefault()?.FullName
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The file of a Steam (S) or ExtraMetadata (E) source of a kind for this version only (never a sibling's), with its path; null when it has none.</summary>
    private (GameEntry Version, string Path)? Chosen(GameEntry game, string kind, char token) =>
        PathError is not null ? null
            : (token == 'E' ? ExtraMetadataFile(game) : SteamKindFile(game, kind)) is string path ? (game, path) : null;

    /// <summary>
    /// The full path of the Logo.png of a version in &lt;Playnite folder&gt;\ExtraMetadata\games\&lt;game id&gt;\ (the Playnite extension Extra
    /// Metadata Loader), read only; null when it is missing, cannot be read, lies outside ExtraMetadata\games or it or its game folder is a junction
    /// or symbolic link.
    /// </summary>
    private string? ExtraMetadataFile(GameEntry game)
    {
        try
        {
            var root = Path.GetFullPath(ExtraMetadataFolder()) + Path.DirectorySeparatorChar;
            var dir = Path.GetFullPath(Path.Combine(root, game.Id));
            var file = Path.Combine(dir, "Logo.png");
            return dir.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir) && !IsLink(dir) && File.Exists(file)
                && (File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0 ? file : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private string ExtraMetadataFolder() => Path.Combine(_playniteFolder, "ExtraMetadata", "games");

    /// <summary>Playnite's file of a kind of a version, relative to library\files: cover, background, icon; null for the other kinds.</summary>
    private static string? PlayniteFile(GameEntry game, string kind) => kind switch
    {
        "Primary" => game.CoverImage,
        "Backdrop" => game.BackgroundImage,
        IconKind => game.Icon,
        _ => null
    };

    /// <summary>The full path of a Playnite file inside library\files as PlayniteReader.ImagePath finds it, also when it does not exist; null outside.</summary>
    private string? PlaynitePath(string? relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return null;
        }

        var filesDir = Path.GetFullPath(Path.Combine(_playniteFolder, "library", "files")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(filesDir, relative));
        return full.StartsWith(filesDir, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// The current source of a kind for a version, existing files only: the sources of the kind in their order (Playnite's, Steam's or ExtraMetadata's
    /// file of <paramref name="game"/> itself, never of another version); with the id of the version it belongs to. None for Off.
    /// </summary>
    private (string? Path, bool Steam, string GameId) Wanted(GameGroup group, GameEntry game, string kind)
    {
        foreach (var token in Order(kind))
        {
            if (token == 'P')
            {
                if (PlayniteReader.ImagePath(_playniteFolder, PlayniteFile(game, kind)) is string own)
                {
                    return (own, false, game.Id);
                }
            }
            else if (Chosen(game, kind, token) is { } chosen)
            {
                return (chosen.Path, true, chosen.Version.Id);
            }
        }

        return (null, false, game.Id);
    }

    /// <summary>
    /// True while a copy fits its source for a version. A Steam or ExtraMetadata copy: its source is still one of the kind's sources, no Playnite file
    /// before it in the order exists, and the current file of that source is the copied one unchanged (or there is none any more). A Playnite copy: only for
    /// the version it was copied from, while Playnite is a source, no Steam or ExtraMetadata file before it exists, and its Playnite file is the copied
    /// one and unchanged (or gone, e.g. Playnite's image folder removed).
    /// </summary>
    private bool Fits(ImageCopy copy, GameGroup group, GameEntry game, string kind)
    {
        var order = Order(kind);
        if (copy.Steam)
        {
            var token = IsSameOrInside(copy.Source, ExtraMetadataFolder()) ? 'E' : 'S';
            if (!order.Contains(token))
            {
                return false;
            }

            foreach (var before in order.TakeWhile(t => t != token))
            {
                if (before == 'P' && PlayniteReader.ImagePath(_playniteFolder, PlayniteFile(game, kind)) is not null)
                {
                    return false;
                }
            }

            var current = Chosen(game, kind, token)?.Path;
            return current is null || (current.Equals(copy.Source, StringComparison.OrdinalIgnoreCase) && Unchanged(current, copy));
        }

        if (!order.Contains('P'))
        {
            return false;
        }

        foreach (var before in order.TakeWhile(t => t != 'P'))
        {
            if (Chosen(game, kind, before) is not null)
            {
                return false;
            }
        }

        var path = PlaynitePath(PlayniteFile(game, kind));
        return copy.GameId == game.Id && path is not null && path.Equals(copy.Source, StringComparison.OrdinalIgnoreCase) && Unchanged(path, copy);
    }

    /// <summary>True when the source file is gone or still has the time and length it had when it was copied.</summary>
    private static bool Unchanged(string source, ImageCopy copy)
    {
        var info = new FileInfo(source);
        return !info.Exists || (info.Length == copy.SourceLength && info.LastWriteTimeUtc.Ticks == copy.SourceTicks);
    }

    /// <summary>The user's file of a kind in a folder listing: the first backdrop, icon.png, or the single image of the type.</summary>
    private static string? UserFile(FolderImages found, string kind) => kind switch
    {
        "Backdrop" => found.Backdrops.FirstOrDefault(),
        IconKind => found.Icon,
        _ => found.Single.GetValueOrDefault(Enum.Parse<ImageType>(kind))
    };

    /// <summary>The mode of a copied kind (Sources table); the other kinds have no copies.</summary>
    private ArtworkMode ModeOf(string kind) => kind switch
    {
        "Primary" => _config.CoverMode,
        "Backdrop" => _config.BackdropMode,
        "Logo" => _config.LogoMode,
        "Banner" => _config.BannerMode,
        "Thumb" => _config.ThumbMode,
        IconKind => _config.IconMode,
        _ => ArtworkMode.Keep
    };

    /// <summary>
    /// The image of a kind for a version, read only: the user's file in its own folder, else the plugin's copy in it, whatever its source says now;
    /// null otherwise. Sources are only imported (Sync Artwork), never read in place.
    /// </summary>
    private string? Answer(FolderImages found, string kind) =>
        UserFile(found, kind) is string user ? user : found.Own.TryGetValue(kind, out var own) ? own.Path : null;

    /// <summary>
    /// With Artwork Copies "All" and a usable list of copies: the copy of a kind of a version in its own folder, made from its own source when the
    /// kind has no user's file and no copy. With <paramref name="replace"/> (Sync Artwork) a copy that no longer fits is replaced, but only when there is
    /// a current source that can be copied. The kind's mode limits that: Keep fills what is missing and never replaces. Source Off imports nothing.
    /// A copy is never deleted here (see Cleanup). Sources without a copy extension (<see cref="CopyExtension"/>; except the icon) and icons under 64 px
    /// are not copied.
    /// </summary>
    private void Ensure(GameGroup group, GameEntry game, string kind, bool replace, IImageProcessor? processor)
    {
        var mode = ModeOf(kind);
        if (_config.ArtworkCopy != ArtworkCopyMode.All || PathError is not null || Order(kind).Count == 0)
        {
            return;
        }

        lock (CopiesLock)
        {
            if (Copies() is null)
            {
                return;
            }
        }

        // Sync Artwork only: an icon.ico copy of an earlier version becomes an icon.png; one that could not be converted stays as it is.
        if (kind == IconKind && replace && MigrateIcon(group, game, mode, processor))
        {
            return;
        }

        var found = Find(game);
        var own = found.Own.GetValueOrDefault(kind);
        if (UserFile(found, kind) is not null || (own is not null && (mode == ArtworkMode.Keep
            || (Fits(own.Copy, group, game, kind) && NameFits(own.Path, kind)) || !replace)))
        {
            return;
        }

        var (source, steam, gameId) = Wanted(group, game, kind);
        if (source is null || !(kind == IconKind ? IconCopyable(source, processor) : CopyExtension(source) is not null) || (own is not null && !Delete(own.Path)))
        {
            return;
        }

        if (Copy(game, kind, source, steam, gameId))
        {
            Interlocked.Increment(ref own is null ? ref _written : ref _replaced);
        }
        else if (own is not null)
        {
            Interlocked.Increment(ref _deleted);
        }
    }

    /// <summary>
    /// Copies a source unchanged (bytes and time) into the game's own folder as &lt;poster|backdrop|logo|banner|landscape|icon&gt;&lt;extension&gt;
    /// (<see cref="CopyExtension"/>, else the source's own) and lists it; an icon that is an .ico is written as .png (<see cref="IcoConverter"/>) with
    /// the source's time. With <paramref name="legacy"/> the bytes come from that older copy of the icon and the listed source data from its entry.
    /// Never over an existing file (it wins). False when nothing was copied (one warning per file and source time, not tried again until the source changes).
    /// </summary>
    private bool Copy(GameEntry game, string kind, string source, bool steam, string gameId, (string Path, ImageCopy Copy)? legacy = null)
    {
        var from = legacy?.Path ?? source;
        DateTime time;
        long length;
        if (legacy is { } old)
        {
            (time, length) = (new DateTime(old.Copy.SourceTicks, DateTimeKind.Utc), old.Copy.SourceLength);
        }
        else
        {
            var info = new FileInfo(source);
            (time, length) = (info.LastWriteTimeUtc, info.Length);
        }

        // The key of the copy lock and of the failure memory per game (its own folder) and kind; not a path on disk.
        var key = Path.Combine(CacheFolder, game.Id, kind);
        if (Failed.TryGetValue(key, out var failedTime) && failedTime == time)
        {
            return false;
        }

        lock (Locks.GetOrAdd(key, _ => new object()))
        {
            string? tmp = null;
            try
            {
                var icon = kind == IconKind && IcoConverter.IsIco(from);
                var folder = EnsureFolder(game);
                var target = Path.GetFullPath(Path.Combine(CacheFolder, folder, CopyName(kind) + (icon ? ".png" : CopyExtension(source) ?? Path.GetExtension(source).ToLowerInvariant())));
                if (File.Exists(target))
                {
                    return false;
                }

                // An own temporary file of a new name, never one that exists (e.g. a user's poster.jpg.tmp): it is ours, and deleted, only once
                // this call created it.
                var created = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target) + ".pb-" + Guid.NewGuid().ToString("N") + ".tmp");
                if (icon)
                {
                    var png = IcoConverter.ToPng(File.ReadAllBytes(from)) ?? throw new InvalidDataException("the icon is damaged or of a kind that is not converted");
                    tmp = created;
                    File.WriteAllBytes(created, png);
                    if (!File.ReadAllBytes(created).AsSpan().SequenceEqual(png))
                    {
                        throw new IOException("the converted icon could not be written correctly");
                    }
                }
                else
                {
                    File.Copy(source, created, false);
                    tmp = created;
                }

                File.SetLastWriteTimeUtc(tmp, time);
                try
                {
                    // Never over a file that appeared meanwhile (e.g. the user's): that file wins, the copy is dropped.
                    File.Move(tmp, target, false);
                }
                catch (IOException) when (File.Exists(target))
                {
                    File.Delete(tmp);
                    return false;
                }
                finally
                {
                    // The folder's images are listed again on the next request.
                    _images.TryRemove(folder, out _);
                }

                var made = new FileInfo(target);
                lock (CopiesLock)
                {
                    if (Copies() is { } copies)
                    {
                        copies[target] = new ImageCopy(kind, gameId, steam, source, time.Ticks, length, made.LastWriteTimeUtc.Ticks, made.Length);
                        Changed();
                    }
                }

                Failed.TryRemove(key, out _);
                return true;
            }
            catch (Exception ex)
            {
                // Warned once per file and source time; not tried again until the source changes.
                if (!Failed.TryGetValue(key, out var known) || known != time)
                {
                    _logger.LogWarning("PlayniteBridge: cannot copy the {Kind} image for game {Id} from \"{Source}\": {Message}", kind, game.Id, source, ex.Message);
                }

                Failed[key] = time;
            }

            try
            {
                if (tmp is not null)
                {
                    File.Delete(tmp);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The own temporary file stays; a later attempt uses a new name and never touches it.
            }

            return false;
        }
    }

    /// <summary>
    /// An icon is copied when its smaller side is at least 64 px: for an .ico the largest frame of its directory (a damaged one is tried, so that
    /// its failure is warned about once), else as Jellyfin's image processor measures it.
    /// </summary>
    private static bool IconCopyable(string source, IImageProcessor? processor) =>
        IcoConverter.IsIco(source)
            ? IcoConverter.LargestSize(source) is not { } size || Math.Min(size.Width, size.Height) >= MinIconSize
            : processor is not null && LargeIcon(source, processor);

    /// <summary>
    /// Sync Artwork, Artwork Copies "All", icon kind in mode Update or Keep: the icon.ico of an earlier version in the game's folder becomes an icon.png.
    /// It is the plugin's own copy when it is listed with its current time and length or its bytes equal Playnite's icon file of the game; any other
    /// icon.ico is left alone and counted. The icon.png is made from the current source (Update, when it can be copied) else from the icon.ico, and the
    /// icon.ico is deleted only after the icon.png is written; an icon.png or a user's icon already there wins and the icon.ico just goes. With the
    /// source Off the icon.ico goes as any copy does. True when an own icon.ico was converted or stays because it could not be
    /// converted: the kind is then left alone for this run.
    /// </summary>
    private bool MigrateIcon(GameGroup group, GameEntry game, ArtworkMode mode, IImageProcessor? processor)
    {
        var folder = FindFolder(game, false);
        if (folder is null)
        {
            return false;
        }

        var legacyPath = Path.GetFullPath(Path.Combine(CacheFolder, folder, CopyName(IconKind) + ".ico"));
        var legacy = new FileInfo(legacyPath);
        if (!legacy.Exists || (legacy.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0)
        {
            return false;
        }

        var listed = CopyOf(legacyPath) is { Kind: IconKind } entry && Owned(legacy, entry) ? entry : null;
        var playnite = PlaynitePath(game.Icon);
        if (listed is null && !(playnite is not null && File.Exists(playnite) && SameBytes(playnite, legacyPath)))
        {
            Interlocked.Increment(ref _unrecognised);
            return false;
        }

        bool Remove()
        {
            try
            {
                if (listed is not null)
                {
                    return Delete(legacyPath);
                }

                legacy.Delete();
                _images.TryRemove(folder, out _);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("PlayniteBridge: cannot delete the image copy \"{File}\": {Message}", legacyPath, ex.Message);
                return false;
            }
        }

        var png = Path.GetFullPath(Path.Combine(CacheFolder, folder, CopyName(IconKind) + ".png"));
        if (Order(IconKind).Count == 0 || File.Exists(png) || UserFile(Find(game), IconKind) is not null)
        {
            if (Remove())
            {
                Interlocked.Increment(ref _deleted);
            }

            return false;
        }

        var (current, steam, gameId) = Wanted(group, game, IconKind);
        bool done;
        if (mode == ArtworkMode.Update && current is not null && IconCopyable(current, processor)
            && (!IcoConverter.IsIco(current) || IcoConverter.ToPng(File.ReadAllBytes(current)) is not null))
        {
            done = Copy(game, IconKind, current, steam, gameId);
        }
        else if (listed is not null)
        {
            done = Copy(game, IconKind, listed.Source, listed.Steam, listed.GameId, (legacyPath, listed));
        }
        else
        {
            done = Copy(game, IconKind, playnite!, false, game.Id);
        }

        if (!done)
        {
            return File.Exists(legacyPath);
        }

        Interlocked.Increment(ref _converted);
        Remove();
        return true;
    }

    private static bool SameBytes(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes one of the plugin's copies and takes it off the list: only while it is listed with its current time and length and its folder is no
    /// link. False when the file stays (one warning when it cannot be deleted).
    /// </summary>
    private bool Delete(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (CopyOf(path) is not ImageCopy copy || !Owned(file, copy) || (File.GetAttributes(file.DirectoryName!) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            file.Delete();
            Drop(path);
            _images.TryRemove(Path.GetFileName(file.DirectoryName!), out _);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("PlayniteBridge: cannot delete the image copy \"{File}\": {Message}", path, ex.Message);
            return false;
        }
    }

    /// <summary>A copy's listed state equals the file: it exists with the time and length it had when it was copied.</summary>
    private static bool Owned(FileInfo file, ImageCopy copy) => file.Exists && file.Length == copy.CopyLength && file.LastWriteTimeUtc.Ticks == copy.CopyTicks;

    /// <summary>
    /// The extension of a copy of <paramref name="source"/>: its own in lower case when it is one of Jellyfin's image extensions
    /// (BaseItem.SupportedImageExtensions), else the one its first bytes show when that is .jpg, .png, .gif or .webp (e.g. a file without an
    /// extension or a .jfif); null otherwise (a BMP too: Jellyfin does not take a .bmp as a local image).
    /// </summary>
    private static string? CopyExtension(string source)
    {
        var own = Path.GetExtension(source);
        if (BaseItem.SupportedImageExtensions.Contains(own, StringComparer.OrdinalIgnoreCase))
        {
            return own.ToLowerInvariant();
        }

        return ContentExtension(source) is (".jpg" or ".png" or ".gif" or ".webp") and var found ? found : null;
    }

    /// <summary>
    /// The image format of a file by its first bytes (at most 12, read only): ".jpg" (FF D8 FF), ".png" (89 50 4E 47 0D 0A 1A 0A), ".gif"
    /// ("GIF87a" or "GIF89a"), ".webp" ("RIFF" at 0 and "WEBP" at 8), ".bmp" ("BM"); null for anything else, a file shorter than the signature or
    /// one that cannot be read.
    /// </summary>
    public static string? ContentExtension(string path)
    {
        var head = new byte[12];
        int read;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            read = stream.ReadAtLeast(head, head.Length, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        ReadOnlySpan<byte> bytes = head.AsSpan(0, read);
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }))
        {
            return ".jpg";
        }

        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
        {
            return ".gif";
        }

        if (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return bytes.StartsWith("BM"u8) ? ".bmp" : null;
    }

    /// <summary>The smaller side of an icon, measured by Jellyfin's image processor, is at least 64 px; an icon that cannot be measured is too small.</summary>
    private static bool LargeIcon(string path, IImageProcessor processor)
    {
        try
        {
            var size = processor.GetImageDimensions(path);
            return Math.Min(size.Width, size.Height) >= MinIconSize;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The listed copy of a full path; null when it is not listed or there is no usable list.</summary>
    private ImageCopy? CopyOf(string path)
    {
        lock (CopiesLock)
        {
            return Copies()?.GetValueOrDefault(path);
        }
    }

    /// <summary>Takes a path off the list of copies (the file is not touched).</summary>
    private void Drop(string path)
    {
        lock (CopiesLock)
        {
            if (Copies()?.Remove(path) == true)
            {
                Changed();
            }
        }
    }

    /// <summary>
    /// The list of copies, read once per <see cref="CopiesFile"/> (missing file = empty list); null without a file name or when the file cannot be
    /// read (one warning). Called under <see cref="CopiesLock"/>.
    /// </summary>
    private Dictionary<string, ImageCopy>? Copies()
    {
        if (CopiesFile.Length == 0)
        {
            return null;
        }

        if (_copiesOf != CopiesFile)
        {
            _copiesOf = CopiesFile;
            _copiesDirty = false;
            try
            {
                var read = File.Exists(CopiesFile)
                    ? JsonSerializer.Deserialize<Dictionary<string, ImageCopy>>(File.ReadAllText(CopiesFile)) ?? throw new JsonException("empty list")
                    : new Dictionary<string, ImageCopy>();
                _copies = new Dictionary<string, ImageCopy>(read, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                _copies = null;
                _logger.LogWarning("PlayniteBridge: cannot read the list of artwork copies \"{File}\"; every image in the game folders counts as the user's, nothing is copied or deleted: {Message}", CopiesFile, ex.Message);
            }
        }

        return _copies;
    }

    /// <summary>
    /// Marks the list of copies changed and, unless already armed, starts the one timer that writes it <see cref="CopiesSaveDelay"/> later.
    /// Called under <see cref="CopiesLock"/>.
    /// </summary>
    private void Changed()
    {
        _copiesDirty = true;
        _copiesVersion++;
        if (!_copiesSaveArmed)
        {
            _copiesSaveArmed = true;
            _copiesTimer?.Dispose();
            _copiesTimer = new Timer(_ => SaveCopies(), null, CopiesSaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Writes the list of copies when it changed (one warning on failure): a snapshot is taken under <see cref="CopiesLock"/>, serialised and
    /// written outside it, so image and list requests do not wait on the disk.
    /// </summary>
    private void SaveCopies()
    {
        lock (CopiesWriteLock)
        {
            string file;
            long version;
            Dictionary<string, ImageCopy> snapshot;
            lock (CopiesLock)
            {
                _copiesSaveArmed = false;
                if (!_copiesDirty || _copies is null || _copiesOf is null)
                {
                    return;
                }

                (file, version, snapshot) = (_copiesOf, _copiesVersion, new Dictionary<string, ImageCopy>(_copies));
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var tmp = file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot));
                File.Move(tmp, file, true);
                lock (CopiesLock)
                {
                    // A change made while writing keeps the list dirty (its timer writes it).
                    if (_copiesVersion == version && _copiesOf == file)
                    {
                        _copiesDirty = false;
                    }
                }
            }
            catch (Exception ex)
            {
                // Any failure, not only IO: this runs on a timer thread, where an escaped exception would end Jellyfin. The list stays dirty
                // and the next change writes it again.
                _logger.LogWarning("PlayniteBridge: cannot write the list of artwork copies \"{File}\": {Message}", file, ex.Message);
            }
        }
    }

    /// <summary>
    /// As Jellyfin's ImageController.GetOutputFormats: the format parameter alone, else WebP (when accepted), JPEG, PNG and GIF (when
    /// accepted, */* included).
    /// </summary>
    private static ImageFormat[] OutputFormats(ImageFormat? format, string[] acceptTypes, string? acceptParam, string userAgent)
    {
        if (format.HasValue)
        {
            return new[] { format.Value };
        }

        // Remove charsets etc. (anything after the semicolon).
        var types = acceptTypes.Select(t => t.IndexOf(';', StringComparison.Ordinal) is var i and >= 0 ? t[..i] : t).ToArray();
        bool Supports(ImageFormat f, bool acceptAll) =>
            types.Contains(f.GetMimeType())
            || (acceptAll && types.Contains("*/*"))
            || string.Equals(acceptParam, f.ToString().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase);

        var formats = new List<ImageFormat>(4);
        if (Supports(ImageFormat.Webp, false)
            || (userAgent.Contains("crosswalk", StringComparison.OrdinalIgnoreCase) && userAgent.Contains("android", StringComparison.OrdinalIgnoreCase)))
        {
            formats.Add(ImageFormat.Webp);
        }

        formats.Add(ImageFormat.Jpg);
        formats.Add(ImageFormat.Png);
        if (Supports(ImageFormat.Gif, true))
        {
            formats.Add(ImageFormat.Gif);
        }

        return formats.ToArray();
    }

    /// <summary>
    /// The images found in a game folder at <paramref name="Listed"/>: the user's files (one per single type, the backdrops in Jellyfin's order,
    /// icon.png) and the plugin's copies per kind.
    /// </summary>
    private sealed record FolderImages(DateTime Listed, IReadOnlyDictionary<ImageType, string> Single, IReadOnlyList<string> Backdrops, string? Icon,
        string? Square, IReadOnlyDictionary<string, OwnCopy> Own, IReadOnlyList<string> Discs, IReadOnlyDictionary<string, string> Boxes);

    /// <summary>A copy of the plugin found in a game folder (listed with its current time and length).</summary>
    private sealed record OwnCopy(string Path, ImageCopy Copy);
}

/// <summary>
/// One copy of the plugin in the list of copies (key: its full path, i.e. game folder and file name): its kind (ImageType name or "Icon"), the
/// id of the version whose image it is, whether the source is a chosen file (Steam's, or Playnite's ExtraMetadata logo, told apart by the
/// source's path) rather than Playnite's file of the version, the source's path, time and length when copied, and the copy's time and
/// length (equal to the source's, they tell the copy from a file the user put there).
/// </summary>
internal sealed record ImageCopy(string Kind, string GameId, bool Steam, string Source, long SourceTicks, long SourceLength, long CopyTicks, long CopyLength);

/// <summary>Jellyfin's folders a game folder must stay out of: People and Studio (or inside them) and every library folder (inside or containing one).</summary>
public sealed record JellyfinFolders(string PeoplePath, string StudioPath, IReadOnlyList<string> LibraryLocations)
{
    /// <summary>The folders as Jellyfin has them now.</summary>
    public static JellyfinFolders Of(IServerApplicationPaths paths, ILibraryManager libraryManager) =>
        new(paths.PeoplePath, paths.StudioPath, libraryManager.GetVirtualFolders().SelectMany(f => f.Locations ?? Array.Empty<string>()).ToList());
}
