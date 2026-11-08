using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One game for the Curator: its short id, its existing folder in each root (null = none there) and the links to write.</summary>
public sealed record CuratorGame(string ShortId, string? MetadataFolder, string? ExtrasFolder, IReadOnlyList<ExternalUrl> Links);

/// <summary>What a Curator run did, in files.</summary>
public sealed class CuratorCounts
{
    public int Written { get; set; }

    public int Unchanged { get; set; }

    public int Removed { get; set; }

    public int NoFolder { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    /// <summary>Set when the run did nothing because the list of files made here cannot be read.</summary>
    public string? Error { get; set; }
}

/// <summary>A file or folder the Curator made: Kind "link", "shortcut" or "folder"; length and write time of a file (it is ours only while both fit), a shortcut's target.</summary>
public sealed class CuratorEntry
{
    public string Kind { get; set; } = string.Empty;

    public string Game { get; set; } = string.Empty;

    public long Length { get; set; }

    public long Ticks { get; set; }

    public string? Target { get; set; }
}

/// <summary>
/// The list of what the Curator made (curator.json: full path -> entry). A file counts as ours only while it is listed and its length and write time are
/// the ones recorded (the rule of the artwork copies); a folder only when the Curator created it. Open returns null when the file cannot be read: then
/// nothing is ours and nothing is written or deleted.
/// </summary>
public sealed class CuratorManifest
{
    private readonly string _file;

    private CuratorManifest(string file, Dictionary<string, CuratorEntry> entries)
    {
        _file = file;
        Entries = entries;
    }

    public Dictionary<string, CuratorEntry> Entries { get; }

    /// <summary>The list in <paramref name="file"/> (a missing file is an empty list); null with one warning when it cannot be read.</summary>
    public static CuratorManifest? Open(string file, ILogger? logger)
    {
        if (file.Length == 0)
        {
            return null;
        }

        try
        {
            var read = File.Exists(file)
                ? JsonSerializer.Deserialize<Dictionary<string, CuratorEntry?>>(File.ReadAllText(file)) ?? throw new JsonException("empty list")
                : new Dictionary<string, CuratorEntry?>();
            // a null entry or a second spelling of one path (case) is dropped, never thrown on
            var entries = new Dictionary<string, CuratorEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, entry) in read)
            {
                if (entry is not null && !string.IsNullOrEmpty(path))
                {
                    entries.TryAdd(path, entry);
                }
            }

            return new CuratorManifest(file, entries);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            logger?.LogWarning("PlayniteBridge: cannot read the list of files made by the Curator \"{File}\"; nothing is written or deleted: {Message}", file, ex.Message);
            return null;
        }
    }

    /// <summary>How often a run writes the list while it goes, and only when it changed (a kill loses at most this much work).</summary>
    public static TimeSpan SaveInterval { get; set; } = TimeSpan.FromSeconds(5);

    private long _lastSave = Environment.TickCount64;
    private bool _dirty;

    /// <summary>Number of file checks (<see cref="Owns"/>) made so far. The cheap pass makes none.</summary>
    public static long FileChecks => Interlocked.Read(ref _fileChecks);

    private static long _fileChecks;

    /// <summary>Removes an entry; true when there was one.</summary>
    public bool Remove(string path)
    {
        var removed = Entries.Remove(path);
        _dirty |= removed;
        return removed;
    }

    /// <summary>Sets an entry.</summary>
    public void Set(string path, CuratorEntry entry)
    {
        Entries[path] = entry;
        _dirty = true;
    }

    /// <summary>Writes the list when <see cref="SaveInterval"/> has passed since the last write (called after every game).</summary>
    public void SaveIfDue(ILogger? logger)
    {
        if (_dirty && Environment.TickCount64 - _lastSave >= (long)SaveInterval.TotalMilliseconds)
        {
            Save(logger);
        }
    }

    /// <summary>Writes the list (temp file, then move); false with one warning on failure.</summary>
    public bool Save(ILogger? logger)
    {
        if (!_dirty && File.Exists(_file))
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Entries));
            File.Move(tmp, _file, true);
            _lastSave = Environment.TickCount64;
            _dirty = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.LogWarning("PlayniteBridge: cannot write the list of files made by the Curator \"{File}\": {Message}", _file, ex.Message);
            return false;
        }
    }

    public int Count(string kind) => Entries.Values.Count(e => e.Kind == kind);

    /// <summary>True when <paramref name="path"/> is listed as <paramref name="kind"/> and the file still has the recorded length and write time.</summary>
    public bool Owns(string path, string kind)
    {
        if (!Entries.TryGetValue(path, out var entry) || entry.Kind != kind)
        {
            return false;
        }

        Interlocked.Increment(ref _fileChecks);
        var info = new FileInfo(path);
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 && info.Length == entry.Length && info.LastWriteTimeUtc.Ticks == entry.Ticks;
    }

    /// <summary>Records the file as written now.</summary>
    public void Record(string path, string kind, string game, string? target = null)
    {
        var info = new FileInfo(path);
        Set(path, new CuratorEntry { Kind = kind, Game = game, Length = info.Length, Ticks = info.LastWriteTimeUtc.Ticks, Target = target });
    }
}

/// <summary>
/// The Curator: bulk work for explorer-side curating. Writes one .url file per link of each game into a links folder inside the game folder and/or the
/// extras folder, and (Windows, Separate Extras Folder) .lnk shortcuts between a game's two folders. The one place the plugin writes into the extras
/// folder, and there only the links folder and the shortcuts, only in folders that already exist, only what it listed in
/// curator.json; a path never comes from a request. Pure file work: the callers hand it the games and the folders.
/// </summary>
public static class Curator
{
    private const string TempSuffix = ".pbtmp";

    // A path longer than this is shortened (Windows stops at 260).
    private const int MaxPathChars = 240;

    private static readonly UTF8Encoding Utf8 = new(false);

    private static readonly CancellationTokenSource Stop = CreateStop();

    private static CancellationTokenSource CreateStop()
    {
        var source = new CancellationTokenSource();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => source.Cancel();
        return source;
    }

    /// <summary>Cancelled when the process shuts down: a run stops after the game it is on and writes the list.</summary>
    public static CancellationToken Shutdown => Stop.Token;

    /// <summary>How long the shortcut thread may take before the run is stopped and reported as failed.</summary>
    public static TimeSpan ShortcutTimeout { get; set; } = TimeSpan.FromMinutes(10);

    private static bool _syncAllActive;
    private static bool _syncAllRan;

    /// <summary>Sync All starts (true) or ends (false): the Curator runs once in it, not in each of Sync Metadata and Sync Extras.</summary>
    public static void SetSyncAll(bool active)
    {
        lock (Gate)
        {
            _syncAllActive = active;
            _syncAllRan = false;
        }
    }

    /// <summary>True when the sync part asking may run the Curator: always outside Sync All, once inside it.</summary>
    public static bool ClaimSyncRun()
    {
        lock (Gate)
        {
            if (!_syncAllActive)
            {
                return true;
            }

            var first = !_syncAllRan;
            _syncAllRan = true;
            return first;
        }
    }

    /// <summary>Where the list of made files lives (set by the plugin).</summary>
    public static string ManifestFile { get; set; } = string.Empty;

    /// <summary>The platform test; replaceable so the non-Windows path can be exercised.</summary>
    public static Func<bool> IsWindows { get; set; } = OperatingSystem.IsWindows;

    /// <summary>The name shown for the server's platform ("Windows", "Linux", "macOS").</summary>
    public static string PlatformName() => IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsMacOS() ? "macOS" : "this platform";

    // One run at a time (manual runs and the sync hooks).
    private static readonly object Gate = new();
    private static bool _busy;

    /// <summary>The kind ("links" or "shortcuts"), games done and games in total of the run going now; empty kind = none.</summary>
    public static (string Kind, int Done, int Total) Progress { get; private set; } = (string.Empty, 0, 0);

    /// <summary>The result of the last finished run (kind, counts, finish time), or null.</summary>
    public static (string Kind, CuratorCounts Counts, DateTime Finished)? Last { get; private set; }

    private static List<(string Path, string Kind)>? _listed;

    /// <summary>How many .url files and shortcuts the list holds (read once, then kept up to date by every run); (0, 0) when the list cannot be read.</summary>
    public static (int Links, int Shortcuts) Totals(ILogger? logger) => Totals(logger, null);

    /// <summary>
    /// The same, counting only entries below <paramref name="roots"/> (null = all): what Delete All can reach. An entry outside every configured root is left as it is by a run and is not counted
    /// (a folder setting changed after a run), so the page's count and button agree with what Delete All does.
    /// </summary>
    public static (int Links, int Shortcuts) Totals(ILogger? logger, IReadOnlyCollection<string>? roots)
    {
        lock (Gate)
        {
            if (_listed is null)
            {
                var manifest = CuratorManifest.Open(ManifestFile, logger);
                _listed = manifest is null ? new List<(string, string)>() : Listed(manifest);
            }

            var scope = roots is null ? _listed : _listed.Where(e => UnderAny(e.Path, roots)).ToList();
            return (scope.Count(e => e.Kind == "link"), scope.Count(e => e.Kind == "shortcut"));
        }
    }

    private static List<(string Path, string Kind)> Listed(CuratorManifest manifest) =>
        manifest.Entries.Where(e => e.Value.Kind is "link" or "shortcut").Select(e => (e.Key, e.Value.Kind)).ToList();

    private static void SetTotals(CuratorManifest manifest)
    {
        lock (Gate)
        {
            _listed = Listed(manifest);
        }
    }

    /// <summary>True while a run goes.</summary>
    public static bool Busy
    {
        get
        {
            lock (Gate)
            {
                return _busy;
            }
        }
    }

    /// <summary>Takes the one-at-a-time gate; false when a run is going.</summary>
    public static bool TryBegin(string kind)
    {
        lock (Gate)
        {
            if (_busy)
            {
                return false;
            }

            _busy = true;
            Progress = (kind, 0, 0);
            return true;
        }
    }

    /// <summary>Ends the run taken with <see cref="TryBegin"/> and keeps its result.</summary>
    public static void End(CuratorCounts? result)
    {
        lock (Gate)
        {
            if (result is not null)
            {
                Last = (Progress.Kind, result, DateTime.Now);
            }

            Progress = (string.Empty, 0, 0);
            _busy = false;
        }
    }

    /// <summary>Forgets the result of the last run (after Delete All it no longer describes the folders).</summary>
    public static void ClearLast()
    {
        lock (Gate)
        {
            Last = null;
        }
    }

    private static void Step(string kind, int done, int total)
    {
        lock (Gate)
        {
            Progress = (kind, done, total);
        }
    }

    // ---------- names

    /// <summary>
    /// The file name stem of a link (without ".url"): the link's name with every character a file name must not have turned into a space, spaces
    /// collapsed, ends and trailing dots trimmed, hidden formatting characters dropped, at most 80 characters; empty = the host of the address, else "link";
    /// a Windows device name (also as "NAME.url") gets a trailing underscore.
    /// </summary>
    public static string Stem(string? name, string url)
    {
        var stem = Clean(name);
        if (stem.Length == 0)
        {
            stem = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? Clean(uri.Host) : string.Empty;
        }

        if (stem.Length == 0)
        {
            stem = "link";
        }

        return AreaCommon.IsDeviceName(stem + ".url") ? stem + "_" : stem;
    }

    private static string Clean(string? text)
    {
        var builder = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(char.IsControl(c) || Array.IndexOf(GameExtras.InvalidPathCharacters, c) >= 0 || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? ' ' : c);
        }

        var clean = System.Text.RegularExpressions.Regex.Replace(builder.ToString(), " {2,}", " ").Trim(' ');
        if (clean.Length > 80)
        {
            // not in the middle of a surrogate pair
            clean = clean[..(char.IsHighSurrogate(clean[79]) ? 79 : 80)];
        }

        return clean.TrimEnd(' ', '.').TrimStart(' ');
    }

    /// <summary>
    /// The files of a games's links, in the order of <paramref name="links"/>: "Stem.url", the second of a name "Stem (2).url", the third "(3)" ... (case ignored, so
    /// a run without change gives the same names), never a name in <paramref name="taken"/> (what is in the folder and not ours); a name is shortened so the whole path
    /// stays within 240 characters, a link that cannot fit is left out (<c>Skipped</c>).
    /// </summary>
    public static List<(string File, string Url)> PlanNames(IEnumerable<ExternalUrl> links, ISet<string> taken, string folder, out int skipped)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var plan = new List<(string, string)>();
        skipped = 0;
        foreach (var link in links)
        {
            var url = link.Url ?? string.Empty;
            var stem = Stem(link.Name, url);
            var room = MaxPathChars - folder.Length - 1 - ".url".Length;
            string? chosen = null;
            for (var n = 1; chosen is null; n++)
            {
                var suffix = n == 1 ? string.Empty : " (" + n.ToString(CultureInfo.InvariantCulture) + ")";
                var fit = stem.Length + suffix.Length <= room ? stem : stem[..Math.Max(0, room - suffix.Length)].TrimEnd(' ', '.');
                if (fit.Length == 0)
                {
                    break;
                }

                var candidate = fit + suffix + ".url";
                if (AreaCommon.IsDeviceName(candidate))
                {
                    candidate = fit + "_" + suffix + ".url";
                }

                if (used.Add(candidate))
                {
                    chosen = candidate;
                }
            }

            if (chosen is null)
            {
                skipped++;
            }
            else
            {
                plan.Add((chosen, url));
            }
        }

        return plan;
    }

    /// <summary>The content of a .url file: an Internet Shortcut with the address, UTF-8 without a byte order mark.</summary>
    public static byte[] Content(string url) => Utf8.GetBytes("[InternetShortcut]\r\nURL=" + url + "\r\n");

    /// <summary>Why the Links Folder name is not used (the rules of an area folder, and not the folder of another area that is on), or null.</summary>
    public static string? LinksFolderError(PluginConfiguration config)
    {
        var name = config.CuratorLinksFolder ?? string.Empty;
        return AreaCommon.FolderNameError(name, true)
            ?? (AreaCommon.Clashes(config, "links", name.Trim()) ? "It is also the folder of another area." : null)
            ?? (VideoRows.Slots(config).Any(v => string.Equals(v.Parent.Length == 0 ? v.Folder : v.Parent, name, StringComparison.OrdinalIgnoreCase))
                ? "It is also the folder of a video slot." : null);
    }

    /// <summary>Why a shortcut name is not used (a plain file name), or null.</summary>
    public static string? ShortcutNameError(string? name) => AreaCommon.FolderNameError(name, false);

    // ---------- files

    private static bool Plain(string folder) => AreaCommon.IsPlainDirectory(folder);

    private static bool IsReparse(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool Under(string path, string root) =>
        path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool UnderAny(string path, IReadOnlyCollection<string> roots) => roots.Any(r => Under(path, Path.GetFullPath(r)));

    /// <summary>
    /// Whether a listed path has the shape of something the Curator makes: a .url file in a links folder of a game folder (root\game [id]\folder\file.url), a .lnk file in a game folder
    /// (root\game [id]\name.lnk), or a links folder (root\game [id]\folder); a kind it does not know never counts. A list that was edited or damaged cannot make it delete anything else.
    /// </summary>
    private static bool Shaped(string path, CuratorEntry entry)
    {
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        var (extension, depth) = entry.Kind switch { "link" => (".url", 3), "shortcut" => (".lnk", 2), "folder" => (string.Empty, 2), _ => ("?", 0) };
        if (depth == 0 || (extension.Length > 0 && !string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var parts = path.Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);
        // the number of parts below the root is not known here, so the game folder part is found from the end
        return parts.Length >= depth + 1 && GameExtras.ShortId(parts[^depth]) is not null;
    }

    // Writes through a temp file next to the target; the temp file is removed when anything fails.
    private static void WriteFile(string path, Action<string> write)
    {
        var tmp = path + TempSuffix;
        try
        {
            write(tmp);
            File.Move(tmp, path, true);
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the original error is the one that counts
            }

            throw;
        }
    }

    /// <summary>
    /// Removes the files of <paramref name="kind"/> the list holds that are not in <paramref name="keep"/> (and, with <paramref name="roots"/>, lie under one of them): a file
    /// that is still ours is deleted (<c>Removed</c>), one that changed is left alone and dropped from the list (<c>Skipped</c>), one that is gone is dropped; never through a
    /// folder that is a junction or symbolic link. Then every folder of the list that is gone or empty is removed (not recursive).
    /// </summary>
    private static void Sweep(CuratorManifest manifest, string kind, ISet<string>? keep, IReadOnlyCollection<string> roots, CuratorCounts counts, ILogger? logger)
    {
        foreach (var (path, entry) in manifest.Entries.Where(e => e.Value.Kind == kind).ToList())
        {
            if (keep is not null && keep.Contains(path))
            {
                continue;
            }

            if (!UnderAny(path, roots))
            {
                // Delete All: an entry outside every configured root whose file is gone has nothing left to remove (and nothing to wait for): it goes from the list; one whose file exists is left alone
                if (keep is null && !File.Exists(path))
                {
                    manifest.Remove(path);
                }

                continue;
            }

            if (!Shaped(path, entry))
            {
                // below a root but not a place or name the Curator makes: not ours, let go of the entry and leave the file
                manifest.Remove(path);
                counts.Skipped++;
                continue;
            }

            try
            {
                var folder = Path.GetDirectoryName(path);
                if (!File.Exists(path))
                {
                    manifest.Remove(path);
                }
                else if (folder is null || !Plain(folder))
                {
                    counts.Skipped++;
                }
                else if (manifest.Owns(path, kind))
                {
                    File.Delete(path);
                    manifest.Remove(path);
                    counts.Removed++;
                }
                else
                {
                    manifest.Remove(path);
                    counts.Skipped++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                counts.Failed++;
                logger?.LogWarning("PlayniteBridge: Curator could not remove \"{File}\" of game {Id}: {Message}", Path.GetFileName(path), entry.Game, ex.Message);
            }
        }

        foreach (var (path, _) in manifest.Entries.Where(e => e.Value.Kind == "folder").ToList())
        {
            try
            {
                if (!UnderAny(path, roots))
                {
                    continue;
                }

                if (!Shaped(path, manifest.Entries[path]))
                {
                    manifest.Remove(path);
                }
                else if (!Directory.Exists(path))
                {
                    manifest.Remove(path);
                }
                else if (Plain(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path, false);
                    manifest.Remove(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning("PlayniteBridge: Curator could not remove the folder \"{Folder}\": {Message}", Path.GetFileName(path), ex.Message);
            }
        }
    }

    // ---------- links

    /// <summary>
    /// "Update": for every game, in each selected root with a folder of the game, writes its links as .url files into the links folder and removes what the
    /// list holds and no longer fits (a link gone from Playnite, a game gone or hidden, another folder name or place). Unchanged files are not written again. A game without a
    /// folder in a root is skipped (<c>NoFolder</c>); nothing is created but the links folder. Files and folders that are not in the list are never touched.
    /// <paramref name="roots"/> are the roots this run can see; only entries below them are removed for not being planned.
    /// </summary>
    public static CuratorCounts RunLinks(CuratorManifest? manifest, IReadOnlyList<CuratorGame> games, string folderName, bool inMetadata, bool inExtras, IReadOnlyCollection<string> roots,
        ILogger? logger, CancellationToken cancellationToken = default, bool quick = false)
    {
        var counts = new CuratorCounts();
        if (manifest is null)
        {
            counts.Error = "The list of files made here cannot be read.";
            return counts;
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // the cheap pass: the files of a folder the list knows, by folder
        var byDir = quick
            ? manifest.Entries.Where(e => e.Value.Kind == "link").GroupBy(e => Path.GetDirectoryName(e.Key) ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(e => e.Key).ToList(), StringComparer.OrdinalIgnoreCase)
            : null;
        try
        {
            for (var i = 0; i < games.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Step("links", i, games.Count);
                var game = games[i];
                foreach (var (selected, folder) in new[] { (inMetadata, game.MetadataFolder), (inExtras, game.ExtrasFolder) })
                {
                    if (!selected)
                    {
                        continue;
                    }

                    if (folder is null)
                    {
                        counts.NoFolder++;
                    }
                    else
                    {
                        LinksIn(manifest, game, folder, folderName, keep, sets, byDir, counts, logger);
                    }
                }

                manifest.SaveIfDue(logger);
            }

            Sweep(manifest, "link", keep, roots, counts, logger);
            // the remembered link sets of folders this run did not look at (a game gone, another Where or folder name)
            foreach (var key in manifest.Entries.Where(e => e.Value.Kind == "set" && !sets.Contains(e.Key) && UnderAny(e.Key[SetPrefix.Length..], roots)).Select(e => e.Key).ToList())
            {
                manifest.Remove(key);
            }
        }
        finally
        {
            manifest.Save(logger);
            SetTotals(manifest);
        }

        return counts;
    }

    private const string SetPrefix = "set|";

    // What a game's links folder should hold, as one hash: the folder name and every link's name and address in order.
    private static string SetHash(string folderName, IReadOnlyList<ExternalUrl> links)
    {
        var text = new StringBuilder(folderName);
        foreach (var link in links)
        {
            text.Append('\n').Append(link.Name).Append('\n').Append(link.Url);
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Utf8.GetBytes(text.ToString())), 0, 8);
    }

    private static void RememberSet(CuratorManifest manifest, HashSet<string> sets, string dir, string game, string hash)
    {
        var key = SetPrefix + dir;
        var exists = Directory.Exists(dir) ? 1 : 0;
        sets.Add(key);
        if (!manifest.Entries.TryGetValue(key, out var entry) || entry.Target != hash || entry.Length != exists)
        {
            manifest.Set(key, new CuratorEntry { Kind = "set", Game = game, Length = exists, Target = hash });
        }
    }

    /// <summary>Number of folder listings Update made so far. The cheap pass lists none.</summary>
    public static long FolderListings => Interlocked.Read(ref _listings);

    private static long _listings;

    private static void LinksIn(CuratorManifest manifest, CuratorGame game, string gameFolder, string folderName, HashSet<string> keep, HashSet<string> sets,
        Dictionary<string, List<string>>? byDir, CuratorCounts counts, ILogger? logger)
    {
        var dir = Path.Combine(gameFolder, folderName);
        var hash = SetHash(folderName, game.Links);
        // The cheap pass (the sync hooks): the same links as when the folder was last looked at and the folder is still there (or still not there): one directory check, no file is looked at.
        // A file deleted by hand is repaired by the manual Update, which looks at every file.
        if (byDir is not null && manifest.Entries.TryGetValue(SetPrefix + dir, out var known) && known.Target == hash && known.Length == (Directory.Exists(dir) ? 1 : 0))
        {
            sets.Add(SetPrefix + dir);
            if (byDir.TryGetValue(dir, out var files))
            {
                foreach (var file in files)
                {
                    keep.Add(file);
                }

                counts.Unchanged += files.Count;
            }

            return;
        }

        var troubleBefore = counts.Failed + counts.Skipped;
        try
        {
            if (!Plain(gameFolder) || File.Exists(dir) || (Directory.Exists(dir) && !Plain(dir)))
            {
                counts.Skipped++;
                logger?.LogWarning("PlayniteBridge: Curator left game {Id} alone: its folder or links folder is a link or a file", game.ShortId);
                return;
            }

            var existed = Directory.Exists(dir);
            if (game.Links.Count == 0 && !existed)
            {
                RememberSet(manifest, sets, dir, game.ShortId, hash);
                return;
            }

            // a folder of that name the Curator did not make and that holds anything but .url files is a real folder (extras, a video slot): the scan skips the name, so nothing is written into it
            Interlocked.Increment(ref _listings);
            if (existed && !manifest.Entries.ContainsKey(dir) && Directory.EnumerateFileSystemEntries(dir).Any(e => !File.Exists(e) || !(e.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || e.EndsWith(".url" + TempSuffix, StringComparison.OrdinalIgnoreCase))))
            {
                counts.Skipped++;
                logger?.LogWarning("PlayniteBridge: Curator left game {Id} alone: a folder \"{Folder}\" that holds other files than .url files is already there", game.ShortId, folderName);
                return;
            }

            // what is in the folder: ours (listed, unchanged) can be refreshed, anything else keeps its name; a changed own file is let go; a temp file of a killed run is removed
            var foreign = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (existed)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    if (entry.EndsWith(".url" + TempSuffix, StringComparison.OrdinalIgnoreCase) && File.Exists(entry))
                    {
                        File.Delete(entry);
                        continue;
                    }

                    if (manifest.Owns(entry, "link"))
                    {
                        continue;
                    }

                    if (manifest.Remove(entry))
                    {
                        counts.Skipped++;
                    }

                    foreign.Add(Path.GetFileName(entry));
                }
            }

            var plan = PlanNames(game.Links, foreign, dir, out var tooLong);
            counts.Skipped += tooLong;
            if (plan.Count > 0 && !existed)
            {
                Directory.CreateDirectory(dir);
                manifest.Set(dir, new CuratorEntry { Kind = "folder", Game = game.ShortId });
            }

            foreach (var (file, url) in plan)
            {
                var path = Path.Combine(dir, file);
                var bytes = Content(url);
                try
                {
                    if (manifest.Owns(path, "link") && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                    {
                        counts.Unchanged++;
                    }
                    else
                    {
                        WriteFile(path, tmp => File.WriteAllBytes(tmp, bytes));
                        manifest.Record(path, "link", game.ShortId);
                        counts.Written++;
                    }

                    keep.Add(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    counts.Failed++;
                    logger?.LogWarning("PlayniteBridge: Curator could not write \"{File}\" of game {Id}: {Message}", file, game.ShortId, ex.Message);
                }
            }

            // only a folder that came out right is remembered (nothing failed, nothing skipped), so the cheap pass never hides a problem
            if (counts.Failed + counts.Skipped == troubleBefore)
            {
                RememberSet(manifest, sets, dir, game.ShortId, hash);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            counts.Failed++;
            logger?.LogWarning("PlayniteBridge: Curator could not read the folder of game {Id}: {Message}", game.ShortId, ex.Message);
        }
    }

    /// <summary>"Delete All" of the links: removes every .url file the list holds below <paramref name="roots"/> that is still unchanged, and the links folders the Curator made once they are empty.</summary>
    public static CuratorCounts DeleteAll(CuratorManifest? manifest, string kind, IReadOnlyCollection<string> roots, ILogger? logger)
    {
        var counts = new CuratorCounts();
        if (manifest is null)
        {
            counts.Error = "The list of files made here cannot be read.";
            return counts;
        }

        try
        {
            Sweep(manifest, kind, null, roots, counts, logger);
            if (kind == "link")
            {
                foreach (var key in manifest.Entries.Where(e => e.Value.Kind == "set" && UnderAny(e.Key[SetPrefix.Length..], roots)).Select(e => e.Key).ToList())
                {
                    manifest.Remove(key);
                }
            }
        }
        finally
        {
            manifest.Save(logger);
            SetTotals(manifest);
        }

        return counts;
    }

    // ---------- shortcuts

    /// <summary>
    /// "Generate": for every game whose two folders both exist, <c>&lt;metadata folder&gt;\&lt;toExtrasName&gt;.lnk</c> to its extras folder and
    /// <c>&lt;extras folder&gt;\&lt;toMetadataName&gt;.lnk</c> to its game folder. A shortcut is refreshed when its target changed; a file of that name that is not ours is left alone
    /// (<c>Skipped</c>); shortcuts of an old name are removed. Windows only (the caller checks <see cref="IsWindows"/>); runs on a thread of its own with an STA apartment.
    /// <paramref name="create"/> makes the shortcut (ShellLink.Create).
    /// </summary>
    public static CuratorCounts RunShortcuts(CuratorManifest? manifest, IReadOnlyList<CuratorGame> games, string toExtrasName, string toMetadataName, IReadOnlyCollection<string> roots,
        Action<string, string> create, ILogger? logger, CancellationToken cancellationToken = default)
    {
        var counts = new CuratorCounts();
        if (manifest is null)
        {
            counts.Error = "The list of files made here cannot be read.";
            return counts;
        }

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Exception? failure = null;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = limit.Token;
        var thread = new Thread(() =>
        {
            try
            {
                for (var i = 0; i < games.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    Step("shortcuts", i, games.Count);
                    var game = games[i];
                    if (game.MetadataFolder is null || game.ExtrasFolder is null)
                    {
                        counts.NoFolder++;
                        continue;
                    }

                    Shortcut(manifest, game, game.MetadataFolder, toExtrasName, game.ExtrasFolder, keep, counts, create, logger);
                    Shortcut(manifest, game, game.ExtrasFolder, toMetadataName, game.MetadataFolder, keep, counts, create, logger);
                    manifest.SaveIfDue(logger);
                }

                Sweep(manifest, "shortcut", keep, roots, counts, logger);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.IsBackground = true;
        if (OperatingSystem.IsWindows())
        {
            thread.SetApartmentState(ApartmentState.STA);
        }

        thread.Start();
        var timedOut = !thread.Join(ShortcutTimeout);
        if (timedOut)
        {
            limit.Cancel();
            thread.Join(TimeSpan.FromSeconds(30));
        }

        manifest.Save(logger);
        SetTotals(manifest);
        if (timedOut)
        {
            throw new TimeoutException("The shortcut run did not finish in time and was stopped.");
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return counts;
    }

    private static void Shortcut(CuratorManifest manifest, CuratorGame game, string folder, string name, string target, HashSet<string> keep, CuratorCounts counts,
        Action<string, string> create, ILogger? logger)
    {
        var path = Path.Combine(folder, name + ".lnk");
        try
        {
            if (File.Exists(path + TempSuffix))
            {
                File.Delete(path + TempSuffix);
            }

            if (!Plain(folder) || !Plain(target) || target.Length > 259 || path.Length > MaxPathChars + 20)
            {
                counts.Skipped++;
                return;
            }

            if (manifest.Owns(path, "shortcut") && string.Equals(manifest.Entries[path].Target, target, StringComparison.OrdinalIgnoreCase))
            {
                counts.Unchanged++;
                keep.Add(path);
                return;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                if (!manifest.Owns(path, "shortcut"))
                {
                    // not ours, or changed since we made it: let go of it, never replace it
                    manifest.Remove(path);
                    counts.Skipped++;
                    return;
                }
            }

            WriteFile(path, tmp => create(tmp, target));
            manifest.Record(path, "shortcut", game.ShortId, target);
            keep.Add(path);
            counts.Written++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            counts.Failed++;
            logger?.LogWarning("PlayniteBridge: Curator could not make the shortcut \"{File}\" of game {Id}: {Message}", name + ".lnk", game.ShortId, ex.Message);
        }
    }

    // ---------- from the settings

    /// <summary>The games and folders of one run: what <see cref="Setup"/> found, and why part of it is missing (null = nothing).</summary>
    public sealed record CuratorSetup(IReadOnlyList<CuratorGame> Games, bool InMetadata, bool InExtras, IReadOnlyList<string> Roots, string? Problem, bool ExtrasUsable);

    /// <summary>
    /// The games of the shown groups (every version and twin: each has its own folder) with the existing folder of each in the game folder and, with Separate Extras
    /// Folder, in the extras folder (found by the short id in the name, as the extras scan does; no folder is made), and their links in the order of the game page but
    /// all of them (not only the hosts the page shows). The extras root is checked as every other extras read is (<see cref="GameExtras.Root"/>).
    /// </summary>
    public static CuratorSetup Setup(PluginConfiguration config, ImageCache cache, string playnite, string? steam, JellyfinFolders jellyfin, IEnumerable<GameGroup> groups,
        IReadOnlyDictionary<string, GameEntry> visible)
    {
        if (cache.PathError is not null)
        {
            return new CuratorSetup(Array.Empty<CuratorGame>(), false, false, Array.Empty<string>(), "The game folder is refused: " + cache.PathError, false);
        }

        string? extrasRoot = null, problem = null;
        if (config.SeparateExtrasFolder)
        {
            var (root, error) = GameExtras.Root(config, cache.CacheFolder, cache.PathError, f => ImageCache.CheckPaths(f, playnite, steam, jellyfin));
            extrasRoot = root;
            problem = error;
        }

        var extrasFolders = extrasRoot is null
            ? new Dictionary<string, string>()
            : GameExtras.FolderIndex(new DirectoryInfo(extrasRoot).EnumerateDirectories("*", AreaCommon.ScanOptions).Select(d => d.Name));
        var links = PlayniteReader.ReadAllLinks();
        var games = new List<CuratorGame>();
        foreach (var game in groups.SelectMany(g => ImageCache.GamesOf(g, visible)))
        {
            var shortId = GameExtras.ShortIdOf(game.Id);
            links.TryGetValue(game.Id, out var own);
            games.Add(new CuratorGame(shortId, cache.GameFolder(game),
                extrasRoot is not null && extrasFolders.TryGetValue(shortId, out var name) ? Path.Combine(extrasRoot, name) : null,
                GameLinks.Arrange(own ?? Array.Empty<ExternalUrl>(), false, string.Empty, config.LinkHostOrder)));
        }

        var place = config.CuratorLinksWhere;
        var roots = new List<string> { cache.CacheFolder };
        if (extrasRoot is not null)
        {
            roots.Add(extrasRoot);
        }

        // with Separate Extras Folder off the choice does not apply: the files go in the game folder only
        return new CuratorSetup(games, place != CuratorPlace.Extras || !config.SeparateExtrasFolder, config.SeparateExtrasFolder && place != CuratorPlace.Metadata && extrasRoot is not null, roots, problem, extrasRoot is not null);
    }

    /// <summary>The setup for the games Jellyfin shows (<paramref name="shown"/>: the visible games), grouped as the library groups them.</summary>
    public static CuratorSetup Setup(PluginConfiguration config, ImageCache cache, string playnite, string? steam, JellyfinFolders jellyfin, IReadOnlyList<GameEntry> shown) =>
        Setup(config, cache, playnite, steam, jellyfin, GameGroups.Group(shown, config), shown.ToDictionary(g => g.Id));

    /// <summary>The roots Delete All trusts: the game folder and, when set, the extras folder, as the settings say now (a list entry elsewhere is not ours).</summary>
    public static IReadOnlyList<string> ConfiguredRoots(PluginConfiguration config, string internalMetadataPath)
    {
        var roots = new List<string> { ImageCache.ResolveCacheFolder(config.CacheFolder, internalMetadataPath) };
        if (!string.IsNullOrWhiteSpace(config.ExtrasFolder))
        {
            roots.Add(config.ExtrasFolder);
        }

        return roots;
    }

    /// <summary>Update (Generate Links): the settings decide the folder name and the places; a refused name writes nothing. <paramref name="quick"/> (the sync hooks) skips a game whose links and folder are as last time without looking at its files.</summary>
    public static CuratorCounts UpdateLinks(PluginConfiguration config, CuratorSetup setup, ILogger? logger, CancellationToken cancellationToken = default, bool quick = false)
    {
        if (LinksFolderError(config) is string error)
        {
            return new CuratorCounts { Error = "Links Folder: " + error + " No links are written." };
        }

        return RunLinks(CuratorManifest.Open(ManifestFile, logger), setup.Games, config.CuratorLinksFolder, setup.InMetadata, setup.InExtras, setup.Roots, logger, cancellationToken, quick);
    }

    /// <summary>Why the shortcuts cannot be made now, or null: Windows only, Separate Extras Folder, a usable extras folder, usable names.</summary>
    public static string? ShortcutsRefusal(PluginConfiguration config, CuratorSetup? setup)
    {
        if (!IsWindows())
        {
            return "Windows only";
        }

        if (!config.SeparateExtrasFolder)
        {
            return "Needs Separate Extras Folder";
        }

        if (setup is not null && !setup.ExtrasUsable)
        {
            return "The extras folder cannot be used: " + (setup.Problem ?? "it does not exist.");
        }

        return ShortcutNameError(config.CuratorToExtrasName) is string a ? "Shortcut to Extras: " + a
            : ShortcutNameError(config.CuratorToMetadataName) is string b ? "Shortcut to Game Folder: " + b : null;
    }

    /// <summary>Generate (shortcuts): refused with the reason in <see cref="CuratorCounts.Error"/>.</summary>
    public static CuratorCounts GenerateShortcuts(PluginConfiguration config, CuratorSetup setup, ILogger? logger, CancellationToken cancellationToken = default)
    {
        if (ShortcutsRefusal(config, setup) is string refusal)
        {
            return new CuratorCounts { Error = refusal };
        }

        return RunShortcuts(CuratorManifest.Open(ManifestFile, logger), setup.Games, config.CuratorToExtrasName, config.CuratorToMetadataName, setup.Roots, MakeShortcut, logger, cancellationToken);
    }

    private static void MakeShortcut(string lnkPath, string target)
    {
        if (!IsWindows() || !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Shortcuts are Windows only.");
        }

        ShellLink.Create(lnkPath, target, "Playnite Bridge");
    }

    /// <summary>
    /// What Sync Metadata and Sync Extras do for the Curator: Update with Generate Links and Links on Every Sync, Generate with Shortcuts on Every Sync and Separate Extras
    /// Folder on Windows. One line in the log each. A run that goes already (a manual one) makes the sync skip its part.
    /// </summary>
    public static void AfterSync(PluginConfiguration config, ImageCache cache, string playnite, string? steam, JellyfinFolders jellyfin, IEnumerable<GameGroup> groups,
        IReadOnlyDictionary<string, GameEntry> visible, ILogger logger, CancellationToken cancellationToken = default)
    {
        var links = config.CuratorLinksEnabled && config.CuratorLinksOnSync;
        var shortcuts = config.CuratorShortcutsOnSync && ShortcutsRefusal(config, null) is null;
        if (!links && !shortcuts)
        {
            return;
        }

        if (!ClaimSyncRun())
        {
            return;
        }

        if (!TryBegin(links ? "links" : "shortcuts"))
        {
            logger.LogInformation("PlayniteBridge: Curator skipped in the sync, a Curator run is going");
            return;
        }

        CuratorCounts? last = null;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Shutdown);
        cancellationToken = stop.Token;
        try
        {
            var setup = Setup(config, cache, playnite, steam, jellyfin, groups, visible);
            if (links)
            {
                last = UpdateLinks(config, setup, logger, cancellationToken, quick: true);
                Report(logger, "links", last);
            }

            if (shortcuts)
            {
                Step("shortcuts", 0, 0);
                last = GenerateShortcuts(config, setup, logger, cancellationToken);
                Report(logger, "shortcuts", last);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("PlayniteBridge: the Curator could not run in the sync: {Message}", ex.Message);
        }
        finally
        {
            End(last);
        }
    }

    /// <summary>One information line (a warning with an error) with the counts of a run.</summary>
    public static void Report(ILogger logger, string kind, CuratorCounts c)
    {
        if (c.Error is not null)
        {
            logger.LogWarning("PlayniteBridge: Curator {Kind} not run: {Error}", kind, c.Error);
        }
        else
        {
            logger.LogInformation("PlayniteBridge: Curator {Kind}: {Written} written, {Unchanged} unchanged, {Removed} removed, {NoFolder} without a folder, {Skipped} skipped, {Failed} failed", kind, c.Written, c.Unchanged, c.Removed, c.NoFolder, c.Skipped, c.Failed);
        }
    }
}
