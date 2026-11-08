using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using LiteDB;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

public sealed record GameEntry(
    string Id,
    string Name,
    string StoreKey,
    string StoreName,
    string StoreGameId,
    string? SteamAppId,
    bool Installed,
    bool Hidden,
    bool Favorite,
    int? Year,
    long Playtime,
    DateTime? Added,
    DateTime? LastActivity,
    int? CommunityScore,
    int? CriticScore,
    string? CoverImage,
    string? BackgroundImage,
    string[] Genres,
    string[] Developers,
    string[] Publishers,
    string[] Platforms,
    string[] AgeRatings,
    string? SortingName,
    DateTime? ReleaseDate,
    long PlayCount,
    string[] Tags,
    NameGuidPair[] GenreRefs,
    NameGuidPair[] PublisherRefs,
    NameGuidPair[] DeveloperRefs,
    NameGuidPair[] SeriesRefs,
    Guid PluginId,
    string[] Features,
    string? SourceName,
    string[] LinkedSteamAppIds,
    string? InstallDirectory,
    string? CompletionStatus,
    string? Icon)
{
    public bool HasCover => !string.IsNullOrEmpty(CoverImage);
}

/// <summary>A game's detail fields; CompletionStatus is null when the game has none or Playnite's default status ("Not Played").</summary>
public sealed record GameDetail(string Description, string[] Features, ExternalUrl[] Links, long? InstallSize, string? CompletionStatus);

/// <summary>
/// Reads the plugin's copy of Playnite's library (PlayniteSnapshot) read-only; from Playnite's folder only the images.
/// Never writes into the Playnite folder.
/// </summary>
public static class PlayniteReader
{
    private static readonly object CacheLock = new();
    private static string? _cacheKey;
    private static List<GameEntry>? _cacheList;
    private static string? _detailKey;
    private static readonly Dictionary<Guid, GameDetail?> _details = new();

    /// <summary>Why a Playnite folder setting is refused (the caller treats it as no Playnite).</summary>
    public const string FolderRefused = "The Playnite folder must be an absolute path that starts with a drive letter.";

    /// <summary>Why there are no games without a copy (the caller treats it as no Playnite).</summary>
    public const string NoSync = "no sync yet";

    // After a failed read the same copy is read again only after this delay or once a sync wrote a new copy.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private static string? _failedKey;
    private static DateTime _failedAt;
    private static ExceptionDispatchInfo? _failure;

    /// <summary>
    /// The Playnite folder: the setting trimmed, empty = %AppData%\Playnite; null when it is not an allowed path
    /// (LauncherPaths.IsAllowedPath), so nothing there is ever opened.
    /// </summary>
    public static string? ResolveFolder(string configured)
    {
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playnite")
            : configured.Trim();
        return LauncherPaths.IsAllowedPath(folder) ? folder : null;
    }

    /// <summary>The games shown in Jellyfin: games hidden in Playnite only when <paramref name="showHidden"/> is set.</summary>
    public static List<GameEntry> ReadVisibleGames(bool showHidden) =>
        ReadGames().Where(g => (showHidden || !g.Hidden) && !HiddenByTitle(g.Name)).ToList();

    /// <summary>The games before Hide Titles: games hidden in Playnite only when <paramref name="showHidden"/> is set (the scenario tests apply Hide Titles themselves).</summary>
    public static List<GameEntry> ReadShownGames(bool showHidden) =>
        ReadGames().Where(g => showHidden || !g.Hidden).ToList();

    // Hide Titles of the plugin's settings (nothing is hidden without a plugin instance, as in the tools).
    private static bool HiddenByTitle(string name) =>
        Plugin.Instance?.Configuration is { } config && GameGroups.HiddenByTitle(name, config);

    /// <summary>
    /// All games of every store and the custom games of the copy (PlayniteSnapshot), with resolved names; read once per copy and sync.
    /// Waits while a sync writes the copy; throws without a copy (NoSync) or when it cannot be read.
    /// </summary>
    public static List<GameEntry> ReadGames() => Load().List;

    // The games, the copy's folder and the cache key (folder and generation: a sync overwrites the same folder).
    private static (List<GameEntry> List, string Set, string Key) Load()
    {
        lock (PlayniteSnapshot.SetLock)
        {
            var set = PlayniteSnapshot.CurrentSet() ?? throw new InvalidOperationException(NoSync);
            var key = set + "|" + PlayniteSnapshot.Generation.ToString(CultureInfo.InvariantCulture);
            lock (CacheLock)
            {
                if (_cacheList is not null && _cacheKey == key)
                {
                    return (_cacheList, set, key);
                }

                // The same copy failed a moment ago: answer as then, without reading it again.
                if (_failedKey == key && DateTime.UtcNow - _failedAt < RetryDelay)
                {
                    _failure?.Throw();
                }
            }

            List<GameEntry> list;
            try
            {
                list = ReadGamesUncached(set);
            }
            catch (Exception ex)
            {
                // The copy's error, now and for the delay.
                var failure = ExceptionDispatchInfo.Capture(ex);
                lock (CacheLock)
                {
                    _failedKey = key;
                    _failedAt = DateTime.UtcNow;
                    _failure = failure;
                }

                throw;
            }

            lock (CacheLock)
            {
                _cacheKey = key;
                _cacheList = list;
            }

            return (list, set, key);
        }
    }

    /// <summary>All games of the copy folder, read anew: the sync's check of the copy it wrote, within its SetLock.</summary>
    internal static List<GameEntry> ReadSetGames(string set) => ReadGamesUncached(set);

    // Called within SetLock only (Load, the sync), so no sync writes the copy while it is read.
    private static List<GameEntry> ReadGamesUncached(string set)
    {
        var companies = Names(set, "companies.db");
        var genres = Names(set, "genres.db");
        var platforms = Names(set, "platforms.db");
        var ratings = Names(set, "ageratings.db");
        var tags = Names(set, "tags.db");
        var series = Names(set, "series.db");
        var features = Names(set, "features.db");
        var sources = Names(set, "sources.db");
        var statuses = StatusNames(set);

        string[] Resolve(BsonDocument d, string field, Dictionary<Guid, string> names) =>
            d[field].IsArray
                ? d[field].AsArray.Select(v => names.TryGetValue(v.AsGuid, out var n) ? n : null).Where(n => n is not null).Select(n => n!).ToArray()
                : Array.Empty<string>();

        NameGuidPair[] Refs(BsonDocument d, string field, Dictionary<Guid, string> names) =>
            d[field].IsArray
                ? d[field].AsArray.Where(v => names.ContainsKey(v.AsGuid)).Select(v => new NameGuidPair { Id = v.AsGuid, Name = names[v.AsGuid] }).ToArray()
                : Array.Empty<NameGuidPair>();

        DateTime? Date(BsonValue v) => v.IsDateTime ? v.AsDateTime : null;

        GameEntry ToEntry(BsonDocument d)
        {
            var released = ParseReleaseDate(d["ReleaseDate"].AsString);
            var sourceName = sources.TryGetValue(d["SourceId"].AsGuid, out var source) ? source : null;
            var store = GameQuery.Store(d["PluginId"].AsGuid, sourceName);
            var storeGameId = d["GameId"].AsString;
            return new GameEntry(
                d["_id"].AsGuid.ToString(),
                d["Name"].AsString?.Trim() ?? string.Empty,
                store.Key,
                store.Name,
                storeGameId,
                GameQuery.SteamAppId(store.Key, storeGameId),
                d["IsInstalled"].AsBoolean,
                d["Hidden"].AsBoolean,
                d["Favorite"].AsBoolean,
                released?.Year,
                d["Playtime"].AsInt64,
                Date(d["Added"]),
                Date(d["LastActivity"]),
                d["CommunityScore"].IsNumber ? d["CommunityScore"].AsInt32 : null,
                d["CriticScore"].IsNumber ? d["CriticScore"].AsInt32 : null,
                string.IsNullOrEmpty(d["CoverImage"].AsString) ? null : d["CoverImage"].AsString,
                string.IsNullOrEmpty(d["BackgroundImage"].AsString) ? null : d["BackgroundImage"].AsString,
                Resolve(d, "GenreIds", genres),
                Resolve(d, "DeveloperIds", companies),
                Resolve(d, "PublisherIds", companies),
                Resolve(d, "PlatformIds", platforms),
                Resolve(d, "AgeRatingIds", ratings),
                string.IsNullOrWhiteSpace(d["SortingName"].AsString) ? null : d["SortingName"].AsString.Trim(),
                released,
                d["PlayCount"].AsInt64,
                Resolve(d, "TagIds", tags),
                // Genre ids are derived (VirtualIds), so a genre item never shares Playnite's GUID.
                Refs(d, "GenreIds", genres).Select(r => new NameGuidPair { Id = VirtualIds.Genre(r.Id), Name = r.Name }).ToArray(),
                // Publisher ids are derived too, so a company that is developer and publisher has a person id and a studio id.
                Refs(d, "PublisherIds", companies).Select(r => new NameGuidPair { Id = VirtualIds.Publisher(r.Id), Name = r.Name }).ToArray(),
                Refs(d, "DeveloperIds", companies),
                Refs(d, "SeriesIds", series),
                d["PluginId"].AsGuid,
                Resolve(d, "FeatureIds", features),
                sourceName,
                // Only the Steam AppIDs of the links are kept (grouping rule 2), not the links themselves.
                GameGroups.SteamAppIdsFromLinks(d["Links"].IsArray
                    ? d["Links"].AsArray.Where(v => v.IsDocument).Select(v => v.AsDocument["Url"].AsString)
                    : Enumerable.Empty<string>()),
                // Read only for the direct start of GOG and Rockstar games (GameStart).
                string.IsNullOrEmpty(d["InstallDirectory"].AsString) ? null : d["InstallDirectory"].AsString,
                // Every status by name, Playnite's default status ("Not Played") included: the filter entries pb:status:<name>.
                d["CompletionStatusId"].IsGuid && statuses.TryGetValue(d["CompletionStatusId"].AsGuid, out var status) ? status : null,
                // The list view icon (ImageCache.Icon), a path relative to library\files as CoverImage.
                string.IsNullOrEmpty(d["Icon"].AsString) ? null : d["Icon"].AsString);
        }

        return WithDb(set, "games.db", db => db.GetCollection("Game").FindAll()
            .Select(ToEntry)
            .ToList());
    }

    /// <summary>
    /// A game's detail fields from the copy the games come from (ReadGames); cached per game, copy and sync (the item DTO and games/{id}
    /// ask for the same game). Read within SetLock, as ReadGames.
    /// </summary>
    public static GameDetail? ReadDetail(Guid gameId)
    {
        lock (PlayniteSnapshot.SetLock)
        {
            var (_, set, key) = Load();
            lock (CacheLock)
            {
                if (_detailKey != key)
                {
                    _details.Clear();
                    _detailKey = key;
                }
                else if (_details.TryGetValue(gameId, out var cached))
                {
                    return cached;
                }
            }

            var detail = ReadDetailUncached(set, gameId);
            lock (CacheLock)
            {
                if (_detailKey == key)
                {
                    _details[gameId] = detail;
                }
            }

            return detail;
        }
    }

    /// <summary>
    /// The links of every game of the copy by game id, in one pass over games.db (the Curator asks for all games; ReadDetail would open the databases once per game).
    /// Read within SetLock, as ReadGames.
    /// </summary>
    public static Dictionary<string, ExternalUrl[]> ReadAllLinks()
    {
        lock (PlayniteSnapshot.SetLock)
        {
            var (_, set, _) = Load();
            return WithDb(set, "games.db", db => db.GetCollection("Game").FindAll().ToDictionary(
                d => d["_id"].AsGuid.ToString(),
                d => d["Links"].IsArray
                    ? d["Links"].AsArray.Where(v => v.IsDocument).Select(v => new ExternalUrl { Name = v.AsDocument["Name"].AsString, Url = v.AsDocument["Url"].AsString }).ToArray()
                    : Array.Empty<ExternalUrl>()));
        }
    }

    private static GameDetail? ReadDetailUncached(string set, Guid gameId)
    {
        var doc = WithDb(set, "games.db", db =>
        {
            var d = db.GetCollection("Game").FindById(gameId);
            return d is null
                ? null
                : new
                {
                    Description = d["Description"].AsString ?? string.Empty,
                    Features = d["FeatureIds"].IsArray ? d["FeatureIds"].AsArray.Select(v => v.AsGuid).ToArray() : Array.Empty<Guid>(),
                    Links = d["Links"].IsArray
                        ? d["Links"].AsArray.Where(v => v.IsDocument).Select(v => new ExternalUrl { Name = v.AsDocument["Name"].AsString, Url = v.AsDocument["Url"].AsString }).ToArray()
                        : Array.Empty<ExternalUrl>(),
                    InstallSize = d["InstallSize"].IsNumber && d["InstallSize"].AsInt64 > 0 ? d["InstallSize"].AsInt64 : (long?)null,
                    Status = d["CompletionStatusId"].IsGuid ? d["CompletionStatusId"].AsGuid : Guid.Empty
                };
        });
        if (doc is null)
        {
            return null;
        }

        var features = Names(set, "features.db");
        return new GameDetail(
            doc.Description,
            doc.Features.Select(g => features.TryGetValue(g, out var n) ? n : null).Where(n => n is not null).Select(n => n!).ToArray(),
            doc.Links,
            doc.InstallSize,
            CompletionStatus(set, doc.Status));
    }

    /// <summary>
    /// Name of a completion status from completionstatuses.db; null for none, an unknown id or Playnite's default status
    /// (CompletionStatusSettings.DefaultStatus, "Not Played"). The file holds the statuses and a settings collection.
    /// </summary>
    private static string? CompletionStatus(string set, Guid statusId)
    {
        if (statusId == Guid.Empty || !File.Exists(Path.Combine(set, "completionstatuses.db")))
        {
            return null;
        }

        return WithDb(set, "completionstatuses.db", db =>
        {
            string? name = null;
            var defaultStatus = Guid.Empty;
            foreach (var collection in db.GetCollectionNames())
            {
                foreach (var d in db.GetCollection(collection).FindAll())
                {
                    if (d["DefaultStatus"].IsGuid)
                    {
                        defaultStatus = d["DefaultStatus"].AsGuid;
                    }
                    else if (d["_id"].IsGuid && d["_id"].AsGuid == statusId && d["Name"].IsString)
                    {
                        name = d["Name"].AsString;
                    }
                }
            }

            return statusId == defaultStatus ? null : name;
        });
    }

    /// <summary>Id to name of the completion statuses (collection CompletionStatus of completionstatuses.db, which also holds a settings collection); empty if the file is missing.</summary>
    private static Dictionary<Guid, string> StatusNames(string set) =>
        !File.Exists(Path.Combine(set, "completionstatuses.db"))
            ? new Dictionary<Guid, string>()
            : WithDb(set, "completionstatuses.db", db => db.GetCollection("CompletionStatus").FindAll()
                .Where(d => d["_id"].IsGuid && d["Name"].IsString)
                .ToDictionary(d => d["_id"].AsGuid, d => d["Name"].AsString));

    /// <summary>Full path of an image path relative to library\files (as in CoverImage), only if it stays inside library\files and exists; no database access.</summary>
    public static string? ImagePath(string playniteFolder, string? relative)
    {
        if (string.IsNullOrEmpty(relative))
        {
            return null;
        }

        var filesDir = Path.GetFullPath(Path.Combine(playniteFolder, "library", "files")) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(filesDir, relative));
        return full.StartsWith(filesDir, StringComparison.OrdinalIgnoreCase) && File.Exists(full) ? full : null;
    }

    /// <summary>
    /// Playnite's release date string: "yyyy-M-d", "yyyy-M" or "yyyy" (PlayniteSDK ReleaseDate.TryDeserialize). Years after 2100 are
    /// import placeholders (e.g. Xbox's 9998-12-30) and count as no release date.
    /// </summary>
    public static DateTime? ParseReleaseDate(string? value)
    {
        foreach (var format in new[] { "yyyy-M-d", "yyyy-M", "yyyy" })
        {
            if (DateTime.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date.Year > 2100 ? null : date;
            }
        }

        return null;
    }

    /// <summary>
    /// Plain text from Playnite's HTML description for Jellyfin's Markdown overview: tags removed, entities decoded. Opening and closing
    /// p, div, h1-h6, ul, ol, li, blockquote, table and tr tags are paragraph breaks ("\n\n"); a &lt;br&gt; is a Markdown line break
    /// ("  \n"), dropped next to a paragraph break and at the start or end. No Markdown escaping.
    /// </summary>
    public static string? HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        // Placeholders that survive the white space steps: \u0001 = line break, \u0002 = paragraph break (raw ones become spaces).
        var text = Regex.Replace(html, @"[\s\u0001\u0002]+", " ");
        text = Regex.Replace(text, @"<br\b[^>]*>", "\u0001", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</?(p|div|h[1-6]|ul|ol|li|blockquote|table|tr)\b[^>]*>", "\u0002", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]*>", string.Empty);
        // Decoded between the placeholders, so an entity never becomes one.
        text = Regex.Replace(text, "[^\u0001\u0002]+", m => WebUtility.HtmlDecode(m.Value).Replace('\u0001', ' ').Replace('\u0002', ' '));
        text = Regex.Replace(text, @"[^\S\n]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, "[ \n\u0001]*\u0002[ \n\u0001\u0002]*", "\n\n");
        text = Regex.Replace(text, " *\u0001 *", "  \n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Id to name of a Playnite lookup file (companies.db, genres.db, ...); empty if the file is missing.</summary>
    private static Dictionary<Guid, string> Names(string set, string file)
    {
        if (!File.Exists(Path.Combine(set, file)))
        {
            return new Dictionary<Guid, string>();
        }

        return WithDb(set, file, db =>
        {
            var collection = db.GetCollectionNames().FirstOrDefault();
            return collection is null
                ? new Dictionary<Guid, string>()
                : db.GetCollection(collection).FindAll().ToDictionary(d => d["_id"].AsGuid, d => d["Name"].AsString);
        });
    }

    // Opens a file of a copy folder (PlayniteSnapshot), never one in Playnite's folder.
    private static T WithDb<T>(string set, string file, Func<LiteDatabase, T> read)
    {
        var dbPath = Path.Combine(set, file);
        if (!File.Exists(dbPath))
        {
            throw new FileNotFoundException(file + " not found", dbPath);
        }

        using var db = new LiteDatabase($"Filename={dbPath};Mode=ReadOnly");
        return read(db);
    }
}
