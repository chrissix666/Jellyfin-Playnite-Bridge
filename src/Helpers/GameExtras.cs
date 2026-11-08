using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using Jellyfin.Extensions;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>What a local extra is answered as: theme song, theme video, local trailer or special feature.</summary>
public enum ExtraKind
{
    ThemeSong,
    ThemeVideo,
    Trailer,
    Special,
    /// <summary>A track of the game's folder "soundtrack": played from its own row, never an extra of the four kinds above.</summary>
    Soundtrack
}

/// <summary>
/// One local extra file: its stable id, the path relative to its game folder ("/"), name and overview (.nfo for trailers and specials);
/// the image files next to a video extra as Jellyfin finds them for a video in a mixed folder (<see cref="GameExtras"/>; none for theme songs):
/// ImagePath = the Primary file, Images = the Logo, Art, Disc, Banner and Thumb files, Backdrops = the backdrop files in order (null or empty
/// without one); Trickplay = the layouts of the folder &lt;base name&gt;.trickplay next to a video extra (empty without one, null for theme songs).
/// </summary>
public sealed record GameExtra(Guid Id, string ShortId, string RelativePath, string Path, ExtraKind Kind, ExtraType ExtraType, string Name, string? Overview,
    string Container, long Size, string? ImagePath = null, IReadOnlyList<TrickplaySet>? Trickplay = null,
    IReadOnlyDictionary<ImageType, string>? Images = null, IReadOnlyList<string>? Backdrops = null, int Slot = 0, DateTime Modified = default, int Block = 0);

/// <summary>
/// One trickplay layout next to a video extra, as Jellyfin saves it with the media (TrickplayManager):
/// &lt;base name&gt;.trickplay\&lt;width&gt; - &lt;cols&gt;x&lt;rows&gt;\0.jpg, 1.jpg ...; Sheets = their paths in order, Lengths = their lengths.
/// </summary>
public sealed record TrickplaySet(int Width, int Cols, int Rows, IReadOnlyList<string> Sheets, IReadOnlyList<long> Lengths);

/// <summary>The values of a trickplay layout with the fields of Jellyfin's TrickplayInfo (no Jellyfin.Data type, to avoid the package dependency).</summary>
public sealed record TrickplayValues(int Width, int Height, int TileWidth, int TileHeight, int ThumbnailCount, int Interval, int Bandwidth);

/// <summary>A game folder of the cache folder: its name, the short id at its end and its extras.</summary>
public sealed record ExtrasFolder(string Name, string ShortId, IReadOnlyList<GameExtra> Extras);

/// <summary>Everything found in the game folders; Error = the first read error (that folder or the whole root left out), null without one; Root = the folder that was read (null for an empty index).</summary>
public sealed record ExtrasIndex(IReadOnlyList<ExtrasFolder> Folders, IReadOnlyDictionary<Guid, GameExtra> ById, string? Error, string? Root = null,
    IReadOnlyDictionary<string, int>? UnusedSlotFolders = null)
{
    public static readonly ExtrasIndex Empty = new(Array.Empty<ExtrasFolder>(), new Dictionary<Guid, GameExtra>(), null);

    /// <summary>
    /// The extras of a game group: every folder whose short id is the first 8 hex digits of one of the group's versions or twins,
    /// sorted by name, then relative path.
    /// </summary>
    public IReadOnlyList<GameExtra> ForGroup(GameGroup group)
    {
        var ids = GameExtras.GroupShortIds(group);
        return Folders.Where(f => ids.Contains(f.ShortId)).SelectMany(f => f.Extras)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The soundtrack of the game page of <paramref name="currentId"/>: the tracks of the shown version's folder "soundtrack"; when it has none and
    /// SoundtrackOfOtherVersions allows, those of the first other version that has one (main order). Files in name order; the list order is made with their tags.
    /// </summary>
    public IReadOnlyList<GameExtra> SoundtrackOf(GameGroup group, string currentId, PluginConfiguration config)
    {
        var (shown, ordered) = Shown(group, currentId);
        foreach (var version in ordered)
        {
            if (version != shown && config.SoundtrackOfOtherVersions == OtherVersionsMode.ThisVersionOnly)
            {
                break;
            }

            var tracks = OfVersion(group, version).Where(e => e.Kind == ExtraKind.Soundtrack).OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
            if (tracks.Count > 0)
            {
                return tracks;
            }
        }

        return Array.Empty<GameExtra>();
    }

    // The version of the group an id stands for (a twin stands for the version that was kept), then every version: the shown one first, the others in main order.
    internal static (GameEntry Shown, List<GameEntry> Ordered) Shown(GameGroup group, string currentId)
    {
        var kept = group.Twins is not null && group.Twins.TryGetValue(currentId, out var k) ? k : currentId;
        var shown = group.Versions.FirstOrDefault(v => v.Id == kept) ?? group.Versions[0];
        return (shown, new[] { shown }.Concat(group.Versions.Where(v => v != shown)).ToList());
    }

    private IEnumerable<GameExtra> OfVersion(GameGroup group, GameEntry version)
    {
        var ids = new HashSet<string> { GameExtras.ShortIdOf(version.Id) };
        foreach (var (twin, kept) in group.Twins ?? new Dictionary<string, string>())
        {
            if (kept == version.Id)
            {
                ids.Add(GameExtras.ShortIdOf(twin));
            }
        }

        return Folders.Where(f => ids.Contains(f.ShortId)).SelectMany(f => f.Extras)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The extras of the game page of <paramref name="currentId"/> in its <paramref name="group"/> (the detail page's version list): the shown version's own
    /// first, then, as far as TrailersOfOtherVersions (trailers) and ExtrasOfOtherVersions (everything else) allow, those of the other versions in main
    /// order; an extra with the same kind and name as an earlier one is left out, so the shown version's wins, else the one of the earlier version.
    /// </summary>
    public IReadOnlyList<GameExtra> ForVersion(GameGroup group, string currentId, PluginConfiguration config)
    {
        var (shown, ordered) = Shown(group, currentId);
        var seen = new HashSet<(ExtraKind, string, string)>();
        var result = new List<GameExtra>();
        foreach (var version in ordered)
        {
            var block = ordered.IndexOf(version);
            foreach (var extra in OfVersion(group, version))
            {
                var mode = extra.Kind == ExtraKind.Trailer ? config.TrailersOfOtherVersions : config.ExtrasOfOtherVersions;
                if (version != shown && mode == OtherVersionsMode.ThisVersionOnly)
                {
                    continue;
                }

                if (seen.Add((extra.Kind, extra.Kind == ExtraKind.Special ? VideoRows.RowKey(extra) : string.Empty, extra.Name.ToLowerInvariant())))
                {
                    result.Add(extra with { Block = block });
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The theme songs of the game page of <paramref name="currentId"/>: the shown version's own (same names once); when it has none and
    /// <paramref name="source"/> is FallBackToGroup, those of one other version: the one that has the song named <paramref name="playingName"/> (the song
    /// that plays already goes on), else the first with any in main order; otherwise none.
    /// </summary>
    public IReadOnlyList<GameExtra> ThemeTracks(GameGroup group, string currentId, ThemeSourceMode source, string? playingName, ExtraKind kind = ExtraKind.ThemeSong)
    {
        var (shown, ordered) = Shown(group, currentId);
        List<GameExtra> Songs(GameEntry v) => OfVersion(group, v).Where(e => e.Kind == kind)
            .GroupBy(e => e.Name.ToLowerInvariant()).Select(g => g.First()).ToList();
        var own = Songs(shown);
        if (own.Count > 0 || source == ThemeSourceMode.ThisVersionOnly)
        {
            return own;
        }

        var others = ordered.Skip(1).Select(Songs).Where(l => l.Count > 0).ToList();
        return others.FirstOrDefault(l => playingName is not null && l.Any(e => string.Equals(e.Name, playingName, StringComparison.OrdinalIgnoreCase)))
            ?? others.FirstOrDefault() ?? new List<GameExtra>();
    }
}

/// <summary>
/// The game folders &lt;cache folder&gt;\&lt;title [short id]&gt;\ and the local extras in them, classified by Jellyfin's own naming rules
/// for movies (Emby.Naming); the images of a video extra by the names, order and rules of Jellyfin's LocalImageProvider (10.10.7) for a video
/// in a mixed folder (<see cref="LocalImages"/>). Read only: nothing here creates, writes, moves or deletes a file (ImageCache writes the plugin's own images).
/// </summary>
public static class GameExtras
{
    /// <summary>
    /// The extra types shown as special features; copy of BaseItem.DisplayExtraTypes (Jellyfin 10.10.7), kept here
    /// so the class works without Jellyfin.Controller's runtime.
    /// </summary>
    public static readonly IReadOnlyCollection<ExtraType> DisplayExtraTypes = new HashSet<ExtraType>
    {
        ExtraType.Unknown,
        ExtraType.BehindTheScenes,
        ExtraType.Clip,
        ExtraType.DeletedScene,
        ExtraType.Interview,
        ExtraType.Sample,
        ExtraType.Scene,
        ExtraType.Featurette,
        ExtraType.Short
    };

    /// <summary>
    /// The characters Jellyfin replaces with a space in a file name; copy of ManagedFileSystem._invalidPathCharacters
    /// (Jellyfin 10.10.7), kept here so the class works without Jellyfin's runtime.
    /// </summary>
    internal static readonly char[] InvalidPathCharacters =
    {
        '\"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7, (char)8, (char)9, (char)10,
        (char)11, (char)12, (char)13, (char)14, (char)15, (char)16, (char)17, (char)18, (char)19, (char)20,
        (char)21, (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28, (char)29, (char)30,
        (char)31, ':', '*', '?', '\\', '/'
    };

    // Only formats a browser plays directly: the plugin answers every extra as direct play, without transcoding.
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".m4a", ".aac", ".ogg", ".oga", ".opus", ".flac", ".wav" };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".webm", ".mov", ".mkv" };

    /// <summary>
    /// The image extensions in Jellyfin's order; copy of BaseItem.SupportedImageExtensions (Jellyfin 10.10.7), kept here
    /// so the class works without Jellyfin.Controller's runtime.
    /// </summary>
    public static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".tbn", ".gif", ".svg" };

    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(10);

    // One scan at a time; the buffer is checked again inside.
    private static readonly object ScanLock = new();

    private static readonly NamingOptions Naming = new();

    private static readonly Regex ShortIdPattern = new(@"\[([0-9a-fA-F]{8})\]\s*$", RegexOptions.Compiled);

    // Jellyfin's layout folder name "<width> - <cols>x<rows>": no leading zero, width 1-99999, cols and rows 1-999.
    private static readonly Regex TrickplayLayoutPattern = new(@"^([1-9][0-9]{0,4}) - ([1-9][0-9]{0,2})x([1-9][0-9]{0,2})\z", RegexOptions.Compiled);

    // A sheet "<n>.jpg", n without a leading zero, case ignored.
    private static readonly Regex SheetPattern = new(@"^(0|[1-9][0-9]{0,8})\.jpg\z", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Junctions, symbolic links and hidden entries are skipped; an unreadable folder is an error, not silently left out.
    private static readonly EnumerationOptions Options = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false
    };

    private static CachedIndex? _cache;

    private static int _scans;

    /// <summary>Number of scans run.</summary>
    public static int Scans => _scans;

    /// <summary>The short id of a game id: its first 8 characters, lower case.</summary>
    public static string ShortIdOf(string gameId) => (gameId.Length > 8 ? gameId[..8] : gameId).ToLowerInvariant();

    /// <summary>
    /// The name of a new game folder: "&lt;title&gt; [&lt;short id&gt;]", the title cleaned as Jellyfin cleans the names of its People and Studio
    /// folders (GetValidFilename, then Trim and TrimEnd('.')); "[&lt;short id&gt;]" alone when nothing of the title is left.
    /// </summary>
    public static string FolderName(string title, string gameId)
    {
        var clean = CleanName(title);
        var suffix = "[" + ShortIdOf(gameId) + "]";
        return clean.Length == 0 ? suffix : clean + " " + suffix;
    }

    /// <summary>A title as a folder name without the short id (the first part of <see cref="FolderName"/>): invalid path characters become a space, then Trim and TrimEnd('.'); may be empty.</summary>
    public static string CleanName(string title)
    {
        var chars = title.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(InvalidPathCharacters, chars[i]) >= 0)
            {
                chars[i] = ' ';
            }
        }

        return new string(chars).Trim().TrimEnd('.');
    }

    /// <summary>The short ids of a game group: of every version and of every twin left out of it.</summary>
    public static HashSet<string> GroupShortIds(GameGroup group) =>
        group.Versions.Select(v => v.Id).Concat(group.Twins?.Keys ?? Enumerable.Empty<string>()).Select(ShortIdOf).ToHashSet();

    /// <summary>
    /// Short id -> folder name of <paramref name="folderNames"/>: names without a short id left out; of several folders with one short id the
    /// first by name (ordinal, case ignored).
    /// </summary>
    public static Dictionary<string, string> FolderIndex(IEnumerable<string> folderNames)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in folderNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (ShortId(name) is string id)
            {
                index.TryAdd(id, name);
            }
        }

        return index;
    }

    /// <summary>The short id at the end of a folder name ("[" 8 hex digits "]", trailing spaces allowed), lower case; null for any other name.</summary>
    public static string? ShortId(string folderName)
    {
        var match = ShortIdPattern.Match(folderName);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    /// <summary>
    /// The one place that decides where local extras are read. SeparateExtrasFolder off: the game folder <paramref name="gameFolder"/>, or no root
    /// (and no error) when <paramref name="gameFolderError"/> refuses it. On: only ExtrasFolder, never the game folder; no root and the reason when
    /// <see cref="ExtrasFolderError"/> refuses it or it does not exist. No root = no extras.
    /// </summary>
    public static (string? Root, string? Error) Root(PluginConfiguration config, string gameFolder, string? gameFolderError, Func<string, string?> checkPaths)
    {
        if (!config.SeparateExtrasFolder)
        {
            return (gameFolderError is null ? gameFolder : null, null);
        }

        if (ExtrasFolderError(config.ExtrasFolder, gameFolder, checkPaths) is string error)
        {
            return (null, error);
        }

        return Directory.Exists(config.ExtrasFolder) ? (config.ExtrasFolder, null) : (null, "The extras folder does not exist.");
    }

    /// <summary>
    /// Why an extras folder is refused, without writing anything: empty, the checks of <paramref name="checkPaths"/> (ImageCache.CheckPaths: absolute,
    /// no link, not Jellyfin's People, Studio or library folders, not the Playnite or Steam folder), no valid path, or the game folder
    /// <paramref name="gameFolder"/> itself, inside it or containing it; null = fine. A folder that does not exist is no reason.
    /// </summary>
    public static string? ExtrasFolderError(string extrasFolder, string gameFolder, Func<string, string?> checkPaths)
    {
        if (string.IsNullOrWhiteSpace(extrasFolder))
        {
            return "Enter the extras folder or turn Separate Extras Folder off.";
        }

        try
        {
            if (checkPaths(extrasFolder) is string error)
            {
                return error.Replace("The game folder", "The extras folder", StringComparison.Ordinal);
            }

            var full = Path.GetFullPath(extrasFolder).TrimEnd('\\', '/');
            var game = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
            return full.Equals(game, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(game + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || game.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? "The extras folder must not be the game folder, inside it or contain it." : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "The extras folder is no valid path.";
        }
    }

    /// <summary>The folder of a game folder that holds the actors' pictures (Kodi's local information); never read as extras.</summary>
    public const string ActorsFolder = ".actors";

    /// <summary>
    /// The index of <paramref name="root"/>. With <paramref name="allowScan"/> it is read again when older than 10 seconds, one scan at a time;
    /// without it the existing index is returned even when it is older, or <see cref="ExtrasIndex.Empty"/> when there is none, and nothing is read.
    /// </summary>
    public static ExtrasIndex Index(string root, bool allowScan, string? skipFolder = null, string soundtrackFolder = Soundtrack.DefaultFolder, string? skipFolder2 = null, IReadOnlyList<VideoSlot>? slots = null)
    {
        var slotKey = VideoRows.SlotKey(slots ?? Array.Empty<VideoSlot>());
        var cached = _cache;
        if (cached is not null && cached.Root == root && cached.Skip == skipFolder && cached.Skip2 == skipFolder2 && cached.Sound == soundtrackFolder && cached.SlotKey == slotKey && (!allowScan || DateTime.UtcNow - cached.Created < Reuse))
        {
            return cached.Index;
        }

        if (!allowScan)
        {
            return ExtrasIndex.Empty;
        }

        lock (ScanLock)
        {
            cached = _cache;
            if (cached is not null && cached.Root == root && cached.Skip == skipFolder && cached.Skip2 == skipFolder2 && cached.Sound == soundtrackFolder && cached.SlotKey == slotKey && DateTime.UtcNow - cached.Created < Reuse)
            {
                return cached.Index;
            }

            var index = Scan(root, skipFolder, soundtrackFolder, skipFolder2, slots);
            _cache = new CachedIndex(root, DateTime.UtcNow, index, skipFolder, soundtrackFolder, skipFolder2, slotKey);
            return index;
        }
    }

    /// <summary>The names of the documents and wallpapers folders while their areas are on and of the Curator's links folder while Generate Links is on (a folder of that name is an ordinary extras folder while the area is off; the screenshots folder is always left out) as the one <c>skipFolder2</c> argument: names joined by '|' (no folder name has one).</summary>
    public static string OtherAreas(Configuration.PluginConfiguration config) => (config.EnableDocuments ? Documents.AreaName(config) : string.Empty) + "|" + (config.EnableWallpapers ? Wallpapers.AreaName(config) : string.Empty)
        + (config.CuratorLinksEnabled && Curator.LinksFolderError(config) is null ? "|" + config.CuratorLinksFolder : string.Empty);

    /// <summary>
    /// Reads the direct sub folders of <paramref name="root"/> with a short id, sorted by name; per folder its own files and the files of
    /// its direct sub folders, nothing deeper; the only exception: the layout folders of &lt;base name&gt;.trickplay next to a video extra.
    /// A direct sub folder named <paramref name="skipFolder"/> (case ignored; the screenshots folder) is not read.
    /// </summary>
    public static ExtrasIndex Scan(string root, string? skipFolder = null, string soundtrackFolder = Soundtrack.DefaultFolder, string? skipFolder2 = null, IReadOnlyList<VideoSlot>? slots = null)
    {
        Interlocked.Increment(ref _scans);
        slots ??= Array.Empty<VideoSlot>();
        // the folders the own video slots read are not read as ordinary extras folders
        if (slots.Count > 0)
        {
            skipFolder2 = (skipFolder2 is null ? string.Empty : skipFolder2 + "|") + VideoRows.SkipNames(slots);
        }

        var unused = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return ExtrasIndex.Empty;
        }

        List<DirectoryInfo> folders;
        try
        {
            folders = new DirectoryInfo(root).EnumerateDirectories("*", Options).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExtrasIndex.Empty with { Error = ex.Message };
        }

        var result = new List<ExtrasFolder>();
        var byId = new Dictionary<Guid, GameExtra>();
        string? error = null;
        foreach (var folder in folders)
        {
            var shortId = ShortId(folder.Name);
            if (shortId is null)
            {
                continue;
            }

            // Classified inside the try: a read error of a trickplay folder leaves the game folder out like any other read error.
            List<GameExtra> found;
            try
            {
                var subfolders = folder.EnumerateDirectories("*", Options).ToList();
                var soundtrack = subfolders.FirstOrDefault(d => d.Name.Equals(soundtrackFolder, StringComparison.OrdinalIgnoreCase));
                var files = folder.EnumerateFiles("*", Options)
                    .Concat(subfolders.Where(d => d != soundtrack && !d.Name.Equals(ActorsFolder, StringComparison.OrdinalIgnoreCase) && (skipFolder is null || !d.Name.Equals(skipFolder, StringComparison.OrdinalIgnoreCase)) && (skipFolder2 is null || !skipFolder2.Split('|').Contains(d.Name, StringComparer.OrdinalIgnoreCase)))
                        .SelectMany(d => d.EnumerateFiles("*", Options)))
                    .ToList();
                // The image files of each folder as LocalImageProvider lists them: one of the image extensions (case ignored), ordered
                // by that list (case-sensitive index, so an upper-case extension comes first, as in Jellyfin); empty files skipped.
                var images = files.Where(f => SupportedImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.Length > 0)
                    .OrderBy(f => Array.IndexOf(SupportedImageExtensions, f.Extension))
                    .ToLookup(f => f.DirectoryName!, StringComparer.OrdinalIgnoreCase);
                found = files.Select(f => Classify(folder, shortId, f, images)).OfType<GameExtra>().Concat(soundtrack is null ? Enumerable.Empty<GameExtra>() : SoundtrackTracks(folder, shortId, soundtrack)).Concat(SlotFiles(folder, shortId, slots, unused)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                error ??= ex.Message;
                continue;
            }

            var extras = new List<GameExtra>();
            foreach (var extra in found)
            {
                if (byId.TryAdd(extra.Id, extra))
                {
                    extras.Add(extra);
                }
            }

            result.Add(new ExtrasFolder(folder.Name, shortId, extras));
        }

        return new ExtrasIndex(result, byId, error, root, unused);
    }

    /// <summary>
    /// The videos of the own video slots in a game folder: per slot the video files directly in Parent\Folder (or Folder), nothing deeper, empty files left out, each a special feature of that slot
    /// (its type line is the file name's ending read by Jellyfin's rules, <see cref="VideoRows.SlotType"/>). Sub folders of a Parent in use that no slot reads are counted in <paramref name="unused"/> (path -> games), not read.
    /// </summary>
    private static IEnumerable<GameExtra> SlotFiles(DirectoryInfo game, string shortId, IReadOnlyList<VideoSlot> slots, Dictionary<string, int> unused)
    {
        var found = new List<GameExtra>();
        foreach (var parent in slots.Where(s => s.Parent.Length > 0).Select(s => s.Parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var parentPath = Path.Combine(game.FullName, parent);
            if (!AreaCommon.IsPlainDirectory(parentPath))
            {
                continue;
            }

            foreach (var sub in new DirectoryInfo(parentPath).EnumerateDirectories("*", Options))
            {
                if (!slots.Any(s => s.Parent.Equals(parent, StringComparison.OrdinalIgnoreCase) && s.Folder.Equals(sub.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var key = parent + "\\" + sub.Name;
                    unused[key] = unused.GetValueOrDefault(key) + 1;
                }
            }
        }

        foreach (var slot in slots)
        {
            var directory = slot.Parent.Length == 0 ? Path.Combine(game.FullName, slot.Folder) : Path.Combine(game.FullName, slot.Parent, slot.Folder);
            if (!AreaCommon.IsPlainDirectory(directory) || (slot.Parent.Length > 0 && !AreaCommon.IsPlainDirectory(Path.Combine(game.FullName, slot.Parent))))
            {
                continue;
            }

            var inside = new DirectoryInfo(directory).EnumerateFiles("*", Options).ToList();
            var images = inside.Where(f => SupportedImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.Length > 0)
                .OrderBy(f => Array.IndexOf(SupportedImageExtensions, f.Extension)).ToLookup(f => f.DirectoryName!, StringComparer.OrdinalIgnoreCase);
            foreach (var file in inside.Where(f => VideoExtensions.Contains(f.Extension) && f.Length > 0).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                found.Add(Build(game, shortId, file, images, ExtraKind.Special, VideoRows.SlotType(file.Name, Naming), slot.Index));
            }
        }

        return found;
    }

    /// <summary>
    /// The tracks of the folder "soundtrack" of a game folder: its audio files directly in it (no sub folders), one extra each, named like the file; the folder's
    /// cover (<see cref="Soundtrack.CoverNames"/> in this order, the image extensions in Jellyfin's order) is every track's ImagePath. Not classified by
    /// Jellyfin's extra rules: any playable audio file counts.
    /// </summary>
    private static IEnumerable<GameExtra> SoundtrackTracks(DirectoryInfo game, string shortId, DirectoryInfo soundtrack)
    {
        var inside = soundtrack.EnumerateFiles("*", Options).ToList();
        var cover = Soundtrack.CoverNames.Select(name => inside
                .Where(f => f.Length > 0 && Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase) && SupportedImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderBy(f => Array.IndexOf(SupportedImageExtensions, f.Extension)).FirstOrDefault()?.FullName)
            .FirstOrDefault(path => path is not null);
        return inside.Where(f => AudioExtensions.Contains(f.Extension) && f.Length > 0).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f =>
        {
            var relative = Path.GetRelativePath(game.FullName, f.FullName).Replace('\\', '/');
            return new GameExtra(VirtualIds.Extra(game.Name.ToLowerInvariant() + "/" + relative.ToLowerInvariant()), shortId, relative, f.FullName, ExtraKind.Soundtrack,
                ExtraType.Unknown, Path.GetFileNameWithoutExtension(f.Name), null, f.Extension.TrimStart('.').ToLowerInvariant(), f.Length, cover);
        });
    }

    /// <summary>The kind of a Jellyfin extra type; null for one that is none of the four.</summary>
    public static ExtraKind? Kind(ExtraType? type) => type switch
    {
        ExtraType.ThemeSong => ExtraKind.ThemeSong,
        ExtraType.ThemeVideo => ExtraKind.ThemeVideo,
        ExtraType.Trailer => ExtraKind.Trailer,
        ExtraType t when DisplayExtraTypes.Contains(t) => ExtraKind.Special,
        _ => null
    };

    /// <summary>
    /// The sort name Jellyfin gives an item named <paramref name="name"/>: copy of BaseItem.CreateSortName and ModifySortChunks
    /// (Jellyfin 10.10.7; EnableAlphaNumericSorting is true for videos and audio) with the
    /// server's SortRemoveWords, SortRemoveCharacters and SortReplaceCharacters from <paramref name="config"/>.
    /// </summary>
    public static string SortName(string name, ServerConfiguration config)
    {
        var sortable = name.Trim().ToLowerInvariant();

        foreach (var search in config.SortRemoveWords)
        {
            // Remove from beginning if a space follows
            if (sortable.StartsWith(search + " ", StringComparison.Ordinal))
            {
                sortable = sortable.Remove(0, search.Length + 1);
            }

            // Remove from middle if surrounded by spaces
            sortable = sortable.Replace(" " + search + " ", " ", StringComparison.Ordinal);

            // Remove from end if followed by a space
            if (sortable.EndsWith(" " + search, StringComparison.Ordinal))
            {
                sortable = sortable.Remove(sortable.Length - (search.Length + 1));
            }
        }

        foreach (var removeChar in config.SortRemoveCharacters)
        {
            sortable = sortable.Replace(removeChar, string.Empty, StringComparison.Ordinal);
        }

        foreach (var replaceChar in config.SortReplaceCharacters)
        {
            sortable = sortable.Replace(replaceChar, " ", StringComparison.Ordinal);
        }

        return ModifySortChunks(sortable);
    }

    // Copy of BaseItem.ModifySortChunks (Jellyfin 10.10.7): digit chunks padded to 10 digits, diacritics removed, the rest
    // transliterated to ASCII (Jellyfin.Extensions).
    private static string ModifySortChunks(ReadOnlySpan<char> name)
    {
        static void AppendChunk(StringBuilder builder, bool isDigitChunk, ReadOnlySpan<char> chunk)
        {
            if (isDigitChunk && chunk.Length < 10)
            {
                builder.Append('0', 10 - chunk.Length);
            }

            builder.Append(chunk);
        }

        if (name.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);

        int chunkStart = 0;
        bool isDigitChunk = char.IsDigit(name[0]);
        for (int i = 0; i < name.Length; i++)
        {
            var isDigit = char.IsDigit(name[i]);
            if (isDigit != isDigitChunk)
            {
                AppendChunk(builder, isDigitChunk, name.Slice(chunkStart, i - chunkStart));
                chunkStart = i;
                isDigitChunk = isDigit;
            }
        }

        AppendChunk(builder, isDigitChunk, name.Slice(chunkStart));

        var result = builder.ToString().RemoveDiacritics();
        if (!result.All(char.IsAscii))
        {
            result = result.Transliterated();
        }

        return result;
    }

    /// <summary>
    /// The first &lt;title&gt; and &lt;plot&gt; of an .nfo under any root element, trimmed, empty as null; (null, null) when the file is missing,
    /// a reparse point or hidden (the skip rule of the scan), unreadable or not well-formed. No DTD is processed and nothing outside the file is resolved.
    /// </summary>
    public static (string? Title, string? Plot) ReadNfo(string path)
    {
        try
        {
            string? title = null;
            string? plot = null;
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & Options.AttributesToSkip) != 0)
            {
                return (null, null);
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName is "title" or "plot")
                {
                    var name = reader.LocalName;
                    var text = reader.ReadElementContentAsString().Trim();
                    if (name == "title")
                    {
                        title ??= text;
                    }
                    else
                    {
                        plot ??= text;
                    }
                }
                else
                {
                    reader.Read();
                }
            }

            return (string.IsNullOrEmpty(title) ? null : title, string.IsNullOrEmpty(plot) ? null : plot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// One file as an extra by Jellyfin's ExtraRuleResolver; null when no rule makes it one of the four kinds or its format is not one a browser
    /// plays (theme songs audio, the others video). <paramref name="images"/> = the image files per folder, for the images next to a video extra.
    /// </summary>
    private static GameExtra? Classify(DirectoryInfo folder, string shortId, FileInfo file, ILookup<string, FileInfo> images)
    {
        if (!AudioExtensions.Contains(file.Extension) && !VideoExtensions.Contains(file.Extension))
        {
            return null;
        }

        var info = ExtraRuleResolver.GetExtraInfo(file.FullName, Naming);
        if (Kind(info.ExtraType) is not ExtraKind kind
            || !(kind == ExtraKind.ThemeSong ? AudioExtensions : VideoExtensions).Contains(file.Extension))
        {
            return null;
        }

        return Build(folder, shortId, file, images, kind, info.ExtraType!.Value, 0);
    }

    // One file as a GameExtra of the game folder; slot > 0 = a file of that own video slot.
    private static GameExtra Build(DirectoryInfo folder, string shortId, FileInfo file, ILookup<string, FileInfo> images, ExtraKind kind, ExtraType extraType, int slot)
    {
        var relative = Path.GetRelativePath(folder.FullName, file.FullName).Replace('\\', '/');
        var baseName = Path.GetFileNameWithoutExtension(file.Name);
        var withNfo = kind is ExtraKind.Trailer or ExtraKind.Special;
        var (title, plot) = withNfo
            ? ReadNfo(Path.Combine(file.DirectoryName!, baseName + ".nfo"))
            : (null, null);
        // Theme songs get no images: LocalImageProvider does not support audio.
        var (primary, single, backdrops) = kind == ExtraKind.ThemeSong
            ? (null, null, null)
            : LocalImages(images[file.DirectoryName!].ToList(), baseName);
        // Keyed by the folder name: two folders of the same short id never share an id.
        return new GameExtra(
            VirtualIds.Extra(folder.Name.ToLowerInvariant() + "/" + relative.ToLowerInvariant()),
            shortId,
            relative,
            file.FullName,
            kind,
            extraType,
            title ?? baseName,
            plot,
            file.Extension.TrimStart('.').ToLowerInvariant(),
            file.Length,
            primary,
            kind == ExtraKind.ThemeSong ? null : ReadTrickplay(file),
            single,
            backdrops,
            slot,
            file.LastWriteTimeUtc);
    }

    /// <summary>
    /// The images of a video named <paramref name="stem"/> (without extension) as Jellyfin's LocalImageProvider (10.10.7) finds them for a video
    /// in a mixed folder (imagePrefix = "&lt;stem&gt;-", isInMixedFolder = true): <paramref name="files"/> = the image files of its folder in Jellyfin's
    /// order, each name without extension compared whole, case ignored, the first found wins. Primary: "&lt;stem&gt;",
    /// then "&lt;stem&gt;-" poster, folder, cover, default, movie (no name without the prefix); Logo, Art, Disc, Banner, Thumb: the
    /// names of <see cref="ImageCache.SingleNames"/> with the prefix; backdrops: "&lt;stem&gt;-&lt;stem&gt;-fanart", then fanart,
    /// background and art each alone and numbered "-1" to "-20", then backdrop alone and numbered "1" to "20", each numbered row ending once three
    /// numbers in total were missing. The folder extrafanart\ is not read.
    /// </summary>
    private static (string? Primary, IReadOnlyDictionary<ImageType, string> Single, IReadOnlyList<string> Backdrops) LocalImages(
        IReadOnlyList<FileInfo> files, string stem)
    {
        var prefix = stem + "-";
        string? Named(string name) => files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))?.FullName;

        var single = new Dictionary<ImageType, string>();
        foreach (var (type, names) in ImageCache.SingleNames)
        {
            var candidates = names.Select(n => prefix + n);
            if ((type == ImageType.Primary ? candidates.Prepend(stem) : candidates).Select(Named).FirstOrDefault(p => p is not null) is string path)
            {
                single[type] = path;
            }
        }

        single.Remove(ImageType.Primary, out var primary);
        var backdrops = new List<string>();
        if (Named(prefix + stem + "-fanart") is string own)
        {
            backdrops.Add(own);
        }

        foreach (var (first, numbered) in ImageCache.BackdropNames.Append(("backdrop", "backdrop")))
        {
            if (Named(prefix + first) is string path)
            {
                backdrops.Add(path);
            }

            var unfound = 0;
            for (var i = 1; i <= 20; i++)
            {
                if (Named(prefix + numbered + i.ToString(CultureInfo.InvariantCulture)) is string numberedPath)
                {
                    backdrops.Add(numberedPath);
                }
                else if (++unfound >= 3)
                {
                    break;
                }
            }
        }

        return (primary, single, backdrops);
    }

    /// <summary>
    /// The image of <paramref name="extra"/> for an image type and index, as the image endpoint answers it: File = its image file (Backdrop: the
    /// <paramref name="index"/>th of its list; every other type only index 0); StillFrame = true only for Primary without a file of a trailer or
    /// special feature, whose still frame is shown instead. Neither = no image (404).
    /// </summary>
    public static (string? File, bool StillFrame) Image(GameExtra extra, ImageType type, int index) => type switch
    {
        ImageType.Backdrop => (extra.Backdrops?.ElementAtOrDefault(index), false),
        _ when index != 0 => (null, false),
        ImageType.Primary => (extra.ImagePath, extra.ImagePath is null && extra.Kind is ExtraKind.Trailer or ExtraKind.Special),
        _ => (extra.Images?.GetValueOrDefault(type), false)
    };

    /// <summary>
    /// Width, cols and rows of a trickplay layout folder name "&lt;width&gt; - &lt;cols&gt;x&lt;rows&gt;"; null for any other name (no leading zero,
    /// width 1-99999, cols and rows 1-999).
    /// </summary>
    public static (int Width, int Cols, int Rows)? TrickplayLayout(string folderName)
    {
        var match = TrickplayLayoutPattern.Match(folderName);
        return match.Success
            ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// The TrickplayInfo values of a layout, as Jellyfin's import of existing tiles: Interval = the server's, at least
    /// 1000; Height = the height of 0.jpg (<paramref name="sheetHeight"/>) / rows, rounded up; ThumbnailCount = the
    /// runtime / Interval rounded up, at most sheets * cols * rows (that capacity without a known runtime; Jellyfin counts the sheet files instead);
    /// Bandwidth = the largest length * 8 / cols / rows / (Interval / 1000) rounded up. Null without a height above 0.
    /// </summary>
    public static TrickplayValues? TrickplayValuesFor(TrickplaySet set, int? sheetHeight, int serverInterval, long? runtimeMs)
    {
        if (sheetHeight is not > 0 || set.Sheets.Count == 0)
        {
            return null;
        }

        var interval = Math.Max(1000, serverInterval);
        var capacity = Math.Min((long)set.Sheets.Count * set.Cols * set.Rows, int.MaxValue);
        var count = runtimeMs > 0 ? Math.Min((runtimeMs.Value + interval - 1) / interval, capacity) : capacity;
        var bandwidth = set.Lengths.Max(l => Math.Min(Math.Ceiling(l * 8m / set.Cols / set.Rows / (interval / 1000m)), int.MaxValue));
        return new TrickplayValues(set.Width, (sheetHeight.Value + set.Rows - 1) / set.Rows, set.Cols, set.Rows, (int)count, interval, (int)bandwidth);
    }

    /// <summary>The sheet <paramref name="index"/> of <paramref name="extra"/>'s layout of <paramref name="width"/>, a path from the scan; null for any other width or index.</summary>
    public static string? TrickplaySheet(GameExtra extra, int width, int index) =>
        extra.Trickplay?.FirstOrDefault(s => s.Width == width) is TrickplaySet set && index >= 0 && index < set.Sheets.Count ? set.Sheets[index] : null;

    /// <summary>
    /// The layouts of &lt;base name&gt;.trickplay next to a video, read with the scan's skip rule (only that folder and its layout folders, nothing
    /// deeper): of two layouts of one width the first by folder name (ordinal, case ignored); sheets 0.jpg, 1.jpg ... up to the first gap; a layout
    /// without 0.jpg is left out.
    /// </summary>
    private static IReadOnlyList<TrickplaySet> ReadTrickplay(FileInfo video)
    {
        var folder = new DirectoryInfo(Path.Combine(video.DirectoryName!, Path.GetFileNameWithoutExtension(video.Name) + ".trickplay"));
        if (!folder.Exists || (folder.Attributes & Options.AttributesToSkip) != 0)
        {
            return Array.Empty<TrickplaySet>();
        }

        var sets = new List<TrickplaySet>();
        foreach (var layout in folder.EnumerateDirectories("*", Options).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (TrickplayLayout(layout.Name) is not (int width, int cols, int rows) || sets.Any(s => s.Width == width))
            {
                continue;
            }

            var sheets = new Dictionary<int, FileInfo>();
            foreach (var file in layout.EnumerateFiles("*", Options))
            {
                var match = SheetPattern.Match(file.Name);
                if (match.Success)
                {
                    sheets[int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)] = file;
                }
            }

            var ordered = new List<FileInfo>();
            while (sheets.TryGetValue(ordered.Count, out var sheet))
            {
                ordered.Add(sheet);
            }

            if (ordered.Count > 0)
            {
                sets.Add(new TrickplaySet(width, cols, rows, ordered.Select(f => f.FullName).ToList(), ordered.Select(f => f.Length).ToList()));
            }
        }

        return sets;
    }

    private sealed record CachedIndex(string Root, DateTime Created, ExtrasIndex Index, string? Skip, string Sound, string? Skip2 = null, string SlotKey = "");
}

/// <summary>
/// The aspect ratio (width / height) of image files by Jellyfin's image processor, kept in memory only, per file and state (path + ImageCache.Tag):
/// each file is measured once per state. An unreadable file or one without a size counts as 16:9 (one warning per file and state).
/// </summary>
public sealed class ImageRatioCache
{
    /// <summary>The ratio of a still frame and of an unreadable image: 16:9.</summary>
    public const double Default = 16.0 / 9.0;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, ImageDimensions> _measure;
    private readonly Action<string, string> _warn;

    /// <param name="measure">The size of a file (IImageProcessor.GetImageDimensions).</param>
    /// <param name="warn">Called once per file and state that gives no size, with the path and the reason.</param>
    public ImageRatioCache(Func<string, ImageDimensions> measure, Action<string, string> warn)
    {
        _measure = measure;
        _warn = warn;
    }

    /// <summary>The ratio of <paramref name="path"/> in the state <paramref name="tag"/>.</summary>
    public double Get(string path, string tag) =>
        _entries.AddOrUpdate(path, p => New(p, tag), (p, old) => old.Tag == tag ? old : New(p, tag)).Ratio.Value;

    private Entry New(string path, string tag) => new(tag, new Lazy<double>(() => Measure(path)));

    private double Measure(string path)
    {
        try
        {
            var size = _measure(path);
            return size.Width > 0 && size.Height > 0 ? (double)size.Width / size.Height : throw new InvalidDataException("the image has no size");
        }
        catch (Exception ex)
        {
            _warn(path, ex.Message);
            return Default;
        }
    }

    private sealed record Entry(string Tag, Lazy<double> Ratio);
}
