using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>What the Cleanup action does with a folder.</summary>
public enum CleanupAction
{
    RecycleBin,
    MoveAside,
    DeletePermanently
}

/// <summary>One root searched for orphaned game folders: Key "game" (Game Folder) or "extras" (Extras Folder).</summary>
public sealed record OrphanRoot(string Key, string Path);

/// <summary>A folder directly inside a root whose name ends in "[8 hex]" and whose id belongs to no game of Playnite.</summary>
public sealed record OrphanFolder(string Root, string Name, string Id);

/// <summary>One list row: a game by its short id; the roots that hold a folder of it and the re-import hint.</summary>
public sealed record OrphanGame(string Id, string Name, IReadOnlyList<OrphanFolder> Folders, bool TitleUnderAnotherId);

/// <summary>The outcome of the action for one folder.</summary>
public sealed record CleanupOutcome(string Root, string Name, bool Ok, string? Error);

/// <summary>
/// Manual clean-up of orphaned game folders (admin page, General, Cleanup). Nothing here runs by itself. An orphan is a direct sub-folder of a root
/// named "... [8 hex]" whose short id belongs to no game of the full list of Playnite (<see cref="PlayniteReader.ReadGames"/>, the list Sync Metadata
/// cleans against; a game that is only hidden is not removed). Matching is by id, never by title. Links and hidden folders are never touched.
/// </summary>
public static class OrphanFolders
{
    /// <summary>The folder inside a root that "Move Aside" fills; never listed, never touched by a scan.</summary>
    public const string RemovedFolderName = "_removed by Playnite Bridge";

    // Same skips as the scans of the game folders: junctions, symbolic links and hidden entries; an unreadable root is an error, not an empty list.
    private static readonly EnumerationOptions Options = new() { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden, IgnoreInaccessible = false };

    // One list or action at a time.
    private static readonly object Gate = new();

    /// <summary>Takes the one-at-a-time gate; false when a list or an action is already running.</summary>
    public static bool TryEnter() => System.Threading.Monitor.TryEnter(Gate);

    /// <summary>Releases the gate taken by <see cref="TryEnter"/>.</summary>
    public static void Exit() => System.Threading.Monitor.Exit(Gate);

    /// <summary>Whether the platform has a recycle bin the plugin can use.</summary>
    public static bool RecycleBinAvailable => OperatingSystem.IsWindows();

    /// <summary>Why the list and the action are refused now; null = allowed.</summary>
    public static string? Refusal(bool playniteRunning, int playniteGames, bool syncRunning) =>
        playniteRunning ? "Playnite is running: close it and run Sync Metadata first."
        : playniteGames == 0 ? "Playnite delivers no games: run Sync Metadata first."
        : syncRunning ? "A sync is running: wait until it is done."
        : null;

    /// <summary>The orphaned folders of the roots, by root and name; a root that is missing or is a link is left out, an unreadable one throws.</summary>
    public static List<OrphanFolder> Find(IEnumerable<OrphanRoot> roots, IReadOnlyCollection<GameEntry> games)
    {
        var known = games.Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet();
        var found = new List<OrphanFolder>();
        foreach (var root in roots)
        {
            if (!Directory.Exists(root.Path) || (File.GetAttributes(root.Path) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            foreach (var dir in new DirectoryInfo(root.Path).EnumerateDirectories("*", Options).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!dir.Name.Equals(RemovedFolderName, StringComparison.OrdinalIgnoreCase) && GameExtras.ShortId(dir.Name) is string id && !known.Contains(id))
                {
                    found.Add(new OrphanFolder(root.Key, dir.Name, id));
                }
            }
        }

        return found;
    }

    /// <summary>The list rows: one per short id, by name; the note is true when a game of Playnite has the same title under another id.</summary>
    public static List<OrphanGame> Rows(IReadOnlyList<OrphanFolder> folders, IReadOnlyCollection<GameEntry> games)
    {
        var titles = games.Select(g => TitleOf(GameExtras.FolderName(g.Name, g.Id))).Where(t => t.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return folders.GroupBy(f => f.Id).Select(g =>
        {
            var title = TitleOf(g.First().Name);
            return new OrphanGame(g.Key, title.Length > 0 ? title : "[" + g.Key + "]", g.ToList(), title.Length > 0 && titles.Contains(title));
        }).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    // The folder name without its "[8 hex]" suffix, trimmed.
    private static string TitleOf(string folderName)
    {
        var at = folderName.LastIndexOf('[');
        return (at >= 0 ? folderName[..at] : folderName).Trim();
    }

    /// <summary>
    /// Does <paramref name="action"/> to each requested (root key, folder name). The orphan set is derived again here; a request names no path. A folder
    /// that is gone already counts as done; one that exists but is no orphan (or is a link) is refused. Never throws for a folder: the first error of
    /// each is returned.
    /// </summary>
    public static List<CleanupOutcome> Act(IEnumerable<OrphanRoot> roots, IReadOnlyCollection<GameEntry> games, CleanupAction action,
        IEnumerable<(string Root, string Name)> requests)
    {
        var rootList = roots.ToList();
        // (root, lower-case name) -> the folder's real spelling from the directory listing; actions use that spelling, never the request's.
        var found = Find(rootList, games);
        var orphans = found.GroupBy(o => (o.Root, o.Name.ToLowerInvariant())).ToDictionary(g => g.Key, g => g.First().Name);
        var exact = found.Select(o => (o.Root, o.Name)).ToHashSet();
        var results = new List<CleanupOutcome>();
        foreach (var (rootKey, name) in requests)
        {
            var root = rootList.FirstOrDefault(r => r.Key == rootKey);
            if (root is null || name.Length == 0 || name != Path.GetFileName(name) || name is "." or "..")
            {
                results.Add(new CleanupOutcome(rootKey, name, false, "Not a folder of this root."));
                continue;
            }

            var path = Path.Combine(root.Path, name);
            if (!Directory.Exists(path))
            {
                results.Add(new CleanupOutcome(rootKey, name, true, null));
                continue;
            }

            // The folder that is acted on is the orphan the directory listing names: the request's own spelling when that is an orphan, else the one
            // orphan of the same lower-cased name; never a different folder that only the request's spelling names (case-sensitive file systems).
            var realName = name;
            if (!exact.Contains((rootKey, name))
                && (!orphans.TryGetValue((rootKey, name.ToLowerInvariant()), out realName!)
                    || Directory.EnumerateDirectories(root.Path).Any(d => string.Equals(Path.GetFileName(d), name, StringComparison.Ordinal))))
            {
                results.Add(new CleanupOutcome(rootKey, name, false, "Not an orphaned folder."));
                continue;
            }

            if ((File.GetAttributes(Path.Combine(root.Path, realName)) & FileAttributes.ReparsePoint) != 0)
            {
                results.Add(new CleanupOutcome(rootKey, name, false, "Not an orphaned folder."));
                continue;
            }

            path = Path.Combine(root.Path, realName);

            try
            {
                switch (action)
                {
                    case CleanupAction.RecycleBin:
                        Recycle(path);
                        break;
                    case CleanupAction.MoveAside:
                        MoveAside(root.Path, path, realName);
                        break;
                    default:
                        DeleteTree(path);
                        break;
                }

                results.Add(new CleanupOutcome(rootKey, name, true, null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
            {
                results.Add(new CleanupOutcome(rootKey, name, false, ex.Message));
            }
        }

        return results;
    }

    // Delete Permanently: Windows refuses to delete a read-only file or folder (the folder-icon tool leaves +r on the game folder and +r+h+s on desktop.ini).
    // So the ReadOnly bit is cleared first, on the folder and everything below it (links are never entered or changed). If the delete still fails, the
    // bits are put back on what is left, so a failure leaves the folder as it was, and the error names the path.
    private static void DeleteTree(string path)
    {
        var cleared = new List<(string Path, FileAttributes Attributes)>();
        try
        {
            ClearReadOnly(path, cleared);
            Directory.Delete(path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (p, a) in cleared)
            {
                try
                {
                    if (File.Exists(p) || Directory.Exists(p))
                    {
                        File.SetAttributes(p, a);
                    }
                }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                    // Best effort: the delete error below is the one that counts.
                }
            }

            throw new IOException("Could not delete \"" + path + "\": " + ex.Message, ex);
        }
    }

    private static void ClearReadOnly(string path, List<(string Path, FileAttributes Attributes)> cleared)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            cleared.Add((path, attributes));
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        if ((attributes & FileAttributes.Directory) == 0)
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false }))
        {
            ClearReadOnly(entry, cleared);
        }
    }

    // The windows recycle bin: dialogs for errors only, so a folder over the bin's size limit is deleted without a question.
    private static void Recycle(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new InvalidOperationException("This system has no recycle bin.");
        }

        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    // Into <root>\_removed by Playnite Bridge\<name>, " (2)", " (3)" ... when that name is taken; the same drive, so it is a rename.
    private static void MoveAside(string root, string path, string name)
    {
        var aside = Path.Combine(root, RemovedFolderName);
        if (Directory.Exists(aside) && (File.GetAttributes(aside) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("\"" + RemovedFolderName + "\" is a link.");
        }

        Directory.CreateDirectory(aside);
        var target = Path.Combine(aside, name);
        for (var n = 2; Directory.Exists(target) || File.Exists(target); n++)
        {
            target = Path.Combine(aside, name + " (" + n + ")");
        }

        Directory.Move(path, target);
    }
}
