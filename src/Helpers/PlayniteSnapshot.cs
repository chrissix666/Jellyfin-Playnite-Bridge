using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>How a sync ended without an error.</summary>
public enum SyncOutcome
{
    Accepted,
    PlayniteRunning
}

/// <summary>A sync's end: the games of the copy.</summary>
public sealed record SyncResult(SyncOutcome Outcome, int Games);

/// <summary>
/// The plugin's own copy of Playnite's database files: exactly one copy in Root\database\, overwritten by every sync. Playnite's folder is
/// only read.
/// </summary>
public static class PlayniteSnapshot
{
    /// <summary>The files Playnite's GameDatabase.LoadCollections opens, and database.json; games.db and database.json are required.</summary>
    public static readonly string[] Files =
    {
        "platforms.db", "emulators.db", "genres.db", "companies.db", "tags.db", "categories.db", "ageratings.db", "series.db", "regions.db",
        "sources.db", "features.db", "games.db", "tools.db", "scanners.db", "filterpresets.db", "importexclusions.db", "completionstatuses.db",
        "database.json"
    };

    /// <summary>Held while a sync writes the copy and while PlayniteReader reads it (lock order SetLock, then PlayniteReader's cache lock).</summary>
    internal static readonly object SetLock = new();

    private static int _generation;

    /// <summary>The copy's parent folder, set at start (Plugin: &lt;DataPath&gt;\playnite-bridge).</summary>
    public static string Root { get; set; } = string.Empty;

    /// <summary>The note of the last sync that ended without an error (Playnite running); kept in memory only.</summary>
    public static string? Note { get; set; }

    private static string DatabaseFolder => Path.Combine(Root, "database");

    /// <summary>Whether Playnite (desktop or fullscreen mode) runs, by process name only.</summary>
    public static bool IsPlayniteRunning()
    {
        foreach (var name in new[] { "Playnite.DesktopApp", "Playnite.FullscreenApp" })
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (processes.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Counts in memory every sync of this process that wrote into the copy, so the game data (VirtualItemsFilter.Find) and PlayniteReader's
    /// cache notice a new copy although its folder stays the same.
    /// </summary>
    public static int Generation => Volatile.Read(ref _generation);

    /// <summary>The copy's folder, null when it holds no games.db (no sync yet).</summary>
    public static string? CurrentSet() => File.Exists(Path.Combine(DatabaseFolder, "games.db")) ? DatabaseFolder : null;

    /// <summary>When the copy was last synced (UTC): the write time of its games.db.</summary>
    public static DateTime SetTime(string set) => File.GetLastWriteTimeUtc(Path.Combine(set, "games.db"));

    /// <summary>
    /// Copies Playnite's files from &lt;playniteFolder&gt;\library over the copy while Playnite is not running; reads wait meanwhile (SetLock).
    /// Playnite running before the copy = PlayniteRunning, nothing written; Playnite starting or a source changing during the copy =
    /// PlayniteRunning, the files copied so far stay (the next sync overwrites them); a real error throws. 0 games is a normal sync.
    /// <paramref name="isRunning"/> replaces the process check, and <paramref name="afterCopy"/> is called with the file name after a file is copied, before it is checked
    /// again.
    /// </summary>
    public static Task<SyncResult> Sync(string playniteFolder, CancellationToken token, IProgress<double>? progress = null,
        Func<bool>? isRunning = null, Action<string>? afterCopy = null)
    {
        isRunning ??= IsPlayniteRunning;
        if (isRunning())
        {
            return Task.FromResult(new SyncResult(SyncOutcome.PlayniteRunning, 0));
        }

        progress?.Report(5);
        var library = Path.Combine(playniteFolder, "library");
        if (!File.Exists(Path.Combine(library, "games.db")))
        {
            throw new InvalidOperationException("games.db was not found in Playnite's library folder.");
        }

        if (!File.Exists(Path.Combine(library, "database.json")))
        {
            throw new InvalidOperationException("Playnite's database has an unknown format: database.json was not found.");
        }

        // Checked in Playnite's folder (read only), so an unknown format overwrites nothing.
        if (FormatVersion(Path.Combine(library, "database.json")) != 4)
        {
            throw new InvalidOperationException("Playnite's database has an unknown format: database.json does not hold \"Version\": 4.");
        }

        var files = Files.Where(f => File.Exists(Path.Combine(library, f))).ToList();
        // Synchronous within the lock (a lock cannot hold an await); the files are a few MB.
        lock (SetLock)
        {
            var folder = DatabaseFolder;
            var written = false;
            try
            {
                Directory.CreateDirectory(folder);
                for (var i = 0; i < files.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var source = Path.Combine(library, files[i]);
                    var before = Stamp(source);
                    if (isRunning())
                    {
                        return Task.FromResult(new SyncResult(SyncOutcome.PlayniteRunning, 0));
                    }

                    written = true;
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var output = new FileStream(Path.Combine(folder, files[i]), FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        input.CopyTo(output);
                    }

                    afterCopy?.Invoke(files[i]);
                    if (isRunning() || Stamp(source) != before)
                    {
                        return Task.FromResult(new SyncResult(SyncOutcome.PlayniteRunning, 0));
                    }

                    progress?.Report(5 + (75.0 * (i + 1) / files.Count));
                }

                // An optional file Playnite no longer has leaves the copy too: the copy is Playnite's current state.
                foreach (var file in Files.Except(files))
                {
                    File.Delete(Path.Combine(folder, file));
                }
            }
            finally
            {
                if (written)
                {
                    Interlocked.Increment(ref _generation);
                }
            }

            int games;
            try
            {
                games = PlayniteReader.ReadSetGames(folder).Count;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Playnite's data cannot be read: {ex.Message}", ex);
            }

            progress?.Report(100);
            return Task.FromResult(new SyncResult(SyncOutcome.Accepted, games));
        }
    }

    private static (long Length, DateTime Written) Stamp(string path)
    {
        var info = new FileInfo(path);
        return (info.Length, info.LastWriteTimeUtc);
    }

    // The "Version" of database.json, opened read only and shared; null when it is missing or no number.
    private static int? FormatVersion(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var json = JsonDocument.Parse(stream);
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("Version", out var version)
                && version.TryGetInt32(out var number) ? number : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }
}
