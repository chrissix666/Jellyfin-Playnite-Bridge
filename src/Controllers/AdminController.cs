using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using Jellyfin.Plugin.PlayniteBridge.Tasks;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Controllers;

/// <summary>Admin page endpoints beyond Jellyfin's plugin configuration API. Administrators only.</summary>
[ApiController]
[Route("PlayniteBridge")]
[Authorize(Policy = Policies.RequiresElevation)]
public class AdminController : ControllerBase
{
    private readonly IServerApplicationPaths _paths;
    private readonly ITaskManager _taskManager;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<AdminController> _logger;
    private readonly VirtualItemsFilter? _filter;

    public AdminController(IServerApplicationPaths paths, ITaskManager taskManager, ILibraryManager libraryManager, ILogger<AdminController> logger,
        VirtualItemsFilter? filter = null)
    {
        _paths = paths;
        _taskManager = taskManager;
        _libraryManager = libraryManager;
        _logger = logger;
        _filter = filter;
    }

    // Trickplay folders (<video name>.trickplay) next to the games' extra videos. None without a usable extras folder.
    private List<string> TrickplayFolders()
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is not string playnite)
        {
            return new List<string>();
        }

        var steam = ImageCache.SteamFolder();
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var gameFolder = ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath);
        var (root, _) = GameExtras.Root(config, gameFolder, ImageCache.CheckPaths(gameFolder, playnite, steam, jellyfin), f => ImageCache.CheckPaths(f, playnite, steam, jellyfin));
        return root is null
            ? new List<string>()
            : GameExtras.Scan(root, Screenshots.AreaName(config), Soundtrack.FolderOf(config), GameExtras.OtherAreas(config), VideoRows.Slots(config)).ById.Values.Select(e => TrickplayGenerator.FolderOf(e.Path))
                .Where(Directory.Exists).Where(d => (System.IO.File.GetAttributes(d) & System.IO.FileAttributes.ReparsePoint) == 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Sub folders of the video slots' parent folders (for example videos\Gameplay) that no slot reads, with the number of games that have them.
    /// Uses the cached extras index. Empty without a usable extras folder, with videos or special features off, or without a slot.
    /// </summary>
    [HttpGet("VideoFolders")]
    public ActionResult GetVideoFolders()
    {
        var config = Plugin.Instance!.Configuration;
        var slots = VideoRows.Slots(config);
        if (!config.EnableVideos || !config.EnableExtras || slots.Count == 0 || PlayniteReader.ResolveFolder(config.PlayniteFolder) is not string playnite)
        {
            return Ok(new { Unused = Array.Empty<object>() });
        }

        var steam = ImageCache.SteamFolder();
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var gameFolder = ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath);
        var (root, _) = GameExtras.Root(config, gameFolder, ImageCache.CheckPaths(gameFolder, playnite, steam, jellyfin), f => ImageCache.CheckPaths(f, playnite, steam, jellyfin));
        var unused = root is null ? null : GameExtras.Index(root, true, Screenshots.AreaName(config), Soundtrack.FolderOf(config), GameExtras.OtherAreas(config), slots).UnusedSlotFolders;
        return Ok(new { Unused = (unused ?? new Dictionary<string, int>()).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Take(50).Select(k => new { Path = k.Key, Games = k.Value }).ToArray() });
    }

    /// <summary>Number of trickplay folders next to the games' extra videos, i.e. what Clear Trickplay would delete.</summary>
    [HttpGet("TrickplayFolders")]
    public ActionResult GetTrickplayFolders() => Ok(new { Count = TrickplayFolders().Count });

    /// <summary>
    /// Deletes every trickplay folder next to the games' extra videos, permanently (no recycle bin, like Jellyfin). Needs confirm=true;
    /// thumbnails and other files stay. A folder that cannot be deleted is skipped and counted.
    /// </summary>
    [HttpPost("ClearTrickplay")]
    public ActionResult ClearTrickplay([FromQuery] bool confirm)
    {
        var folders = TrickplayFolders();
        if (!confirm)
        {
            return Ok(new { Deleted = 0, Failed = 0, Count = folders.Count });
        }

        int deleted = 0, failed = 0;
        foreach (var folder in folders)
        {
            try
            {
                System.IO.Directory.Delete(folder, true);
                deleted++;
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                failed++;
                _logger.LogWarning("PlayniteBridge: could not delete the trickplay folder \"{Folder}\": {Message}", folder, ex.Message);
            }
        }

        _logger.LogInformation("PlayniteBridge: Clear Trickplay deleted {Deleted} trickplay folders, {Failed} could not be deleted", deleted, failed);
        return Ok(new { Deleted = deleted, Failed = failed, Count = folders.Count });
    }

    // Roots Cleanup searches (game folder, and the extras folder while it is separate and accepted), the games from the copy of Playnite's data,
    // and the reason the list and the action are refused right now (null = allowed). Reads only the copy, never Playnite's games.db.
    private (List<OrphanRoot> Roots, List<GameEntry> Games, string? Refusal, DateTime? DataTime) CleanupState()
    {
        var config = Plugin.Instance!.Configuration;
        var roots = new List<OrphanRoot>();
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is string playnite)
        {
            var steam = ImageCache.SteamFolder();
            var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
            Func<string, string?> check = f => ImageCache.CheckPaths(f, playnite, steam, jellyfin);
            var gameFolder = ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath);
            if (check(gameFolder) is null)
            {
                roots.Add(new OrphanRoot("game", gameFolder));
            }

            if (config.SeparateExtrasFolder && GameExtras.ExtrasFolderError(config.ExtrasFolder, gameFolder, check) is null)
            {
                roots.Add(new OrphanRoot("extras", config.ExtrasFolder));
            }
        }

        var games = new List<GameEntry>();
        var current = PlayniteSnapshot.CurrentSet();
        if (current is not null)
        {
            try
            {
                games = PlayniteReader.ReadGames();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PlayniteBridge: could not read the copy \"{Set}\" for Cleanup", current);
            }
        }

        var syncRunning = _taskManager.ScheduledTasks.Any(t => t.State == TaskState.Running
            && (t.ScheduledTask is SyncAllTask or SyncPlayniteTask or BuildGameImagesTask or SyncExtrasTask));
        return (roots, games, OrphanFolders.Refusal(PlayniteSnapshot.IsPlayniteRunning(), games.Count, syncRunning),
            current is null ? null : PlayniteSnapshot.SetTime(current));
    }

    private static string RootLabel(string key) => key == "extras" ? "Extras Folder" : "Game Folder";

    /// <summary>
    /// Cleanup list: orphaned game folders (<see cref="OrphanFolders"/>), one entry per game with the roots that hold a folder, plus the re-import note
    /// and the date of the Playnite data copy. 409 while Playnite runs, delivers no games or a sync runs; 503 for an unreadable root.
    /// </summary>
    [HttpGet("OrphanFolders")]
    public ActionResult GetOrphanFolders()
    {
        if (!OrphanFolders.TryEnter())
        {
            return StatusCode(409, new { Error = "A clean-up is already running." });
        }

        try
        {
            var (roots, games, refusal, dataTime) = CleanupState();
            if (refusal is not null)
            {
                return StatusCode(409, new { Error = refusal });
            }

            var rows = OrphanFolders.Rows(OrphanFolders.Find(roots, games), games);
            return Ok(new
            {
                DataTime = dataTime,
                RecycleBin = OrphanFolders.RecycleBinAvailable,
                Games = rows.Select(r => new
                {
                    r.Id,
                    r.Name,
                    Roots = r.Folders.Select(f => RootLabel(f.Root)).ToList(),
                    Folders = r.Folders.Select(f => new { f.Root, f.Name }).ToList(),
                    Note = r.TitleUnderAnotherId ? "A game with this title exists in Playnite under another ID." : null
                }).ToList()
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "PlayniteBridge: Cleanup could not read a folder");
            return StatusCode(503, new { Error = "A folder could not be read: " + ex.Message });
        }
        finally
        {
            OrphanFolders.Exit();
        }
    }

    /// <summary>
    /// Cleanup action: RecycleBin, MoveAside or DeletePermanently (anything else, or RecycleBin without a recycle bin: 400, nothing done).
    /// Items carry the root ("game" or "extras") and a folder name, never a path. Orphans are derived again and each folder is rechecked
    /// right before it is handled. Same refusals as the list (409). Returns Ok/Error per folder.
    /// </summary>
    [HttpPost("CleanupFolders")]
    public ActionResult CleanupFolders([FromBody] CleanupRequest request)
    {
        if (!Enum.TryParse<CleanupAction>(request.Action, false, out var action) || !Enum.IsDefined(action)
            || (action == CleanupAction.RecycleBin && !OrphanFolders.RecycleBinAvailable) || request.Items is null)
        {
            return BadRequest(new { Error = "Unknown or unavailable action." });
        }

        if (!OrphanFolders.TryEnter())
        {
            return StatusCode(409, new { Error = "A clean-up is already running." });
        }

        try
        {
            var (roots, games, refusal, _) = CleanupState();
            if (refusal is not null)
            {
                return StatusCode(409, new { Error = refusal });
            }

            var results = OrphanFolders.Act(roots, games, action, request.Items.Select(i => (i.Root ?? string.Empty, i.Name ?? string.Empty)));
            foreach (var r in results)
            {
                if (r.Ok)
                {
                    _logger.LogInformation("PlayniteBridge: Cleanup {Action}: {Root} \"{Name}\"", action, RootLabel(r.Root), r.Name);
                }
                else
                {
                    _logger.LogWarning("PlayniteBridge: Cleanup {Action} failed: {Root} \"{Name}\": {Error}", action, RootLabel(r.Root), r.Name, r.Error);
                }
            }

            return Ok(new { Results = results.Select(r => new { r.Root, r.Name, r.Ok, r.Error }).ToList() });
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "PlayniteBridge: Cleanup could not read a folder");
            return StatusCode(503, new { Error = "A folder could not be read: " + ex.Message });
        }
        finally
        {
            OrphanFolders.Exit();
        }
    }

    /// <summary>
    /// Default cache folder, whether games.db exists in the saved Playnite folder (the file is not opened), why that folder is refused if it is,
    /// one row per store of the start table in the saved platform order (counted from the Playnite data copy), and the sync state.
    /// </summary>
    [HttpGet("AdminInfo")]
    public ActionResult GetAdminInfo()
    {
        var folder = PlayniteReader.ResolveFolder(Plugin.Instance!.Configuration.PlayniteFolder);
        var games = new List<GameEntry>();
        var current = PlayniteSnapshot.CurrentSet();
        if (current is not null)
        {
            try
            {
                games = PlayniteReader.ReadGames();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PlayniteBridge: could not count the games of the copy \"{Set}\"", current);
            }
        }

        var last = SyncTask()?.LastExecutionResult;
        return Ok(new
        {
            DefaultCacheFolder = ImageCache.ResolveCacheFolder(string.Empty, _paths.InternalMetadataPath),
            DefaultCollectionPicturesFolder = CollectionPictures.Resolve(string.Empty, _paths.InternalMetadataPath),
            CollectionPicturesError = folder is null ? null : CollectionPicturesRefusal(Plugin.Instance!.Configuration.CollectionPicturesFolder, Plugin.Instance.Configuration.CacheFolder, folder),
            PlayniteReadable = folder is not null && System.IO.File.Exists(System.IO.Path.Combine(folder, "library", "games.db")),
            PlayniteError = folder is null ? PlayniteReader.FolderRefused : null,
            Platform = Curator.PlatformName(),
            // A refused folder is never opened: an empty folder is not an allowed path, so Riot's config.json is not looked for.
            Stores = GameStart.StoreRows(
                games,
                key => LauncherPaths.Find(key, folder ?? string.Empty) is var l && (l.Gog ?? l.Rockstar ?? l.Riot) is not null,
                Plugin.Instance!.Configuration.PlatformOrder),
            Sync = new
            {
                SnapshotTime = current is null ? (DateTime?)null : PlayniteSnapshot.SetTime(current),
                SnapshotGames = games.Count,
                PlayniteRunning = PlayniteSnapshot.IsPlayniteRunning(),
                PlayniteSnapshot.Note,
                LastError = last?.Status == TaskCompletionStatus.Failed ? last.ErrorMessage : null
            }
        });
    }

    /// <summary>
    /// Curator state: the server's platform (the page greys out what does not work there), how many .url files and shortcuts it made, whether a run is in progress
    /// and the last result. Served from memory, no disk scan.
    /// </summary>
    /// </summary>
    [HttpGet("Curator/State")]
    public ActionResult GetCuratorState()
    {
        var (links, shortcuts) = Curator.Totals(_logger, Curator.ConfiguredRoots(Plugin.Instance!.Configuration, _paths.InternalMetadataPath));
        var progress = Curator.Progress;
        var last = Curator.Last;
        return Ok(new
        {
            Platform = Curator.PlatformName(),
            Links = links,
            Shortcuts = shortcuts,
            Busy = Curator.Busy,
            Kind = progress.Kind,
            Done = progress.Done,
            Total = progress.Total,
            Last = last is null ? null : new { last.Value.Kind, last.Value.Counts, last.Value.Counts.Error, last.Value.Finished }
        });
    }

    /// <summary>Writes and refreshes the .url files in the background (progress in Curator/State). Uses the saved settings; nothing comes from the request.</summary>
    [HttpPost("Curator/Links")]
    public ActionResult CuratorLinks()
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.CuratorLinksEnabled)
        {
            return StatusCode(409, new { Error = "Generate Links is off." });
        }

        return Curator.LinksFolderError(config) is string error
            ? StatusCode(409, new { Error = "Links Folder: " + error })
            : StartCurator("links");
    }

    /// <summary>Creates the shortcuts between each game's two folders in the background (Windows, Separate Extras Folder).</summary>
    [HttpPost("Curator/Shortcuts")]
    public ActionResult CuratorShortcuts() =>
        Curator.ShortcutsRefusal(Plugin.Instance!.Configuration, null) is string why ? StatusCode(409, new { Error = why }) : StartCurator("shortcuts");

    private ActionResult StartCurator(string kind)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is not string playnite)
        {
            return StatusCode(409, new { Error = PlayniteReader.FolderRefused });
        }

        if (PlayniteSnapshot.CurrentSet() is null)
        {
            return StatusCode(409, new { Error = "Playnite delivers no games: run Sync Metadata first." });
        }

        if (_taskManager.ScheduledTasks.Any(t => t.State == TaskState.Running && (t.ScheduledTask is SyncAllTask or SyncPlayniteTask or BuildGameImagesTask or SyncExtrasTask)))
        {
            return StatusCode(409, new { Error = "A sync is running: wait until it is done." });
        }

        // Everything that can throw is read before the gate is taken, so a failure never leaves it busy.
        var steam = ImageCache.SteamFolder();
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var gameFolder = ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath);
        var logger = _logger;
        if (!Curator.TryBegin(kind))
        {
            return StatusCode(409, new { Error = "A Curator run is already going." });
        }

        _ = System.Threading.Tasks.Task.Run(() =>
        {
            CuratorCounts? result = null;
            try
            {
                var cache = new ImageCache(config, playnite, gameFolder, steam, jellyfin, logger);
                var shown = PlayniteReader.ReadVisibleGames(config.ShowHidden);
                var setup = Curator.Setup(config, cache, playnite, steam, jellyfin, shown);
                result = kind == "links" ? Curator.UpdateLinks(config, setup, logger, Curator.Shutdown) : Curator.GenerateShortcuts(config, setup, logger, Curator.Shutdown);
                Curator.Report(logger, kind, result);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PlayniteBridge: the Curator run failed");
                result = new CuratorCounts { Error = "The run failed: " + ex.Message };
            }
            finally
            {
                Curator.End(result);
            }
        });
        return Accepted(new { Started = true });
    }

    /// <summary>Deletes the .url files. Without confirm it only counts; with confirm=true it removes the files this plugin made and that are unchanged, plus its empty links folders.</summary>
    [HttpPost("Curator/DeleteLinks")]
    public ActionResult CuratorDeleteLinks([FromQuery] bool confirm) => DeleteCurator("link", confirm);

    /// <summary>Deletes the shortcuts, like <see cref="CuratorDeleteLinks"/>.</summary>
    [HttpPost("Curator/DeleteShortcuts")]
    public ActionResult CuratorDeleteShortcuts([FromQuery] bool confirm) => DeleteCurator("shortcut", confirm);

    private ActionResult DeleteCurator(string kind, bool confirm)
    {
        var (links, shortcuts) = Curator.Totals(_logger, Curator.ConfiguredRoots(Plugin.Instance!.Configuration, _paths.InternalMetadataPath));
        if (!confirm)
        {
            return Ok(new { Count = kind == "link" ? links : shortcuts });
        }

        if (!Curator.TryBegin("delete"))
        {
            return StatusCode(409, new { Error = "A Curator run is already going." });
        }

        try
        {
            Curator.ClearLast();
            var counts = Curator.DeleteAll(CuratorManifest.Open(Curator.ManifestFile, _logger), kind, Curator.ConfiguredRoots(Plugin.Instance!.Configuration, _paths.InternalMetadataPath), _logger);
            _logger.LogInformation("PlayniteBridge: Curator Delete All ({Kind}): {Removed} removed, {Skipped} left alone, {Failed} failed", kind, counts.Removed, counts.Skipped, counts.Failed);
            return counts.Error is null ? Ok(counts) : StatusCode(409, new { counts.Error });
        }
        finally
        {
            Curator.End(null);
        }
    }

    /// <summary>Why the own collection pictures folder (empty = default) is refused (<see cref="CollectionPictures.FolderError"/>), or null. Writes nothing.</summary>
    private string? CollectionPicturesRefusal(string? pictures, string? cacheSetting, string playniteFolder)
    {
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var steam = ImageCache.SteamFolder();
        var game = ImageCache.ResolveCacheFolder(cacheSetting ?? string.Empty, _paths.InternalMetadataPath);
        return CollectionPictures.FolderError(CollectionPictures.Resolve(pictures, _paths.InternalMetadataPath), game, f => ImageCache.CheckPaths(f, playniteFolder, steam, jellyfin));
    }

    private IScheduledTaskWorker? SyncTask() => _taskManager.ScheduledTasks.FirstOrDefault(t => t.ScheduledTask is SyncPlayniteTask);

    /// <summary>
    /// Checks the folders of the form before they are saved. PlayniteError: why the Playnite folder is refused (nothing else is checked then).
    /// Error: why the cache folder is refused (Jellyfin's People, Studio or library folders, the Playnite folders, ImageCache.ValidateCacheFolder).
    /// ExtrasError: why the extras folder is refused while SeparateExtrasFolder is on, checked after the cache folder's path checks and before its write probe.
    /// Null means fine. Nothing is written.
    /// </summary>
    [HttpPost("CacheFolderCheck")]
    public ActionResult CheckCacheFolder([FromBody] CacheFolderRequest request)
    {
        var cache = ImageCache.ResolveCacheFolder(request.CacheFolder ?? string.Empty, _paths.InternalMetadataPath);
        var playnite = PlayniteReader.ResolveFolder(request.PlayniteFolder ?? string.Empty);
        if (playnite is null)
        {
            return Ok(new { PlayniteError = PlayniteReader.FolderRefused });
        }

        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        if (ImageCache.JellyfinError(cache, jellyfin) is string jellyfinError)
        {
            return Ok(new { Error = jellyfinError });
        }

        // The form value may differ from the real Playnite folder, so the saved and the default one are refused too, before anything is written.
        var saved = PlayniteReader.ResolveFolder(Plugin.Instance!.Configuration.PlayniteFolder);
        var standard = PlayniteReader.ResolveFolder(string.Empty);
        if ((saved is not null && IsSameOrInside(cache, saved)) || (standard is not null && IsSameOrInside(cache, standard)))
        {
            return Ok(new { Error = "The game folder must not be inside the Playnite folder." });
        }

        var steam = ImageCache.SteamFolder();
        // The own collection pictures folder (read only) is refused before anything is written, like the extras folder.
        var picturesFolder = CollectionPictures.Resolve(request.CollectionPicturesFolder, _paths.InternalMetadataPath);
        var picturesError = CollectionPicturesRefusal(request.CollectionPicturesFolder, request.CacheFolder, playnite)
            ?? ((saved is not null && IsSameOrInside(picturesFolder, saved)) || (standard is not null && IsSameOrInside(picturesFolder, standard))
                ? "The collection pictures folder must not be inside the Playnite folder." : null);
        if (picturesError is not null)
        {
            return Ok(new { CollectionPicturesError = picturesError });
        }

        if (request.SeparateExtrasFolder)
        {
            // Checked before the cache folder's write probe: a refused extras folder stops the save with nothing written.
            var extras = request.ExtrasFolder ?? string.Empty;
            var extrasError = GameExtras.ExtrasFolderError(extras, cache, f => ImageCache.CheckPaths(f, playnite, steam, jellyfin));
            if (extrasError is null && ((saved is not null && IsSameOrInside(extras, saved)) || (standard is not null && IsSameOrInside(extras, standard))))
            {
                extrasError = "The extras folder must not be inside the Playnite folder.";
            }

            if (extrasError is not null)
            {
                return Ok(new { ExtrasError = extrasError });
            }
        }

        return Ok(new { Error = ImageCache.ValidateCacheFolder(cache, playnite, steam, jellyfin) });
    }

    /// <summary>
    /// Scenario Test of the admin page: counts the titles a word list hits with the form's current values (laid over a copy of the saved settings,
    /// <see cref="ScenarioTests.Overlay"/>) on the Playnite data copy. Nothing is saved. 400 for an unknown scenario or Settings that is not an object,
    /// 503 without a readable copy; a body over 256 KB is refused by the server (413).
    /// </summary>
    [HttpPost("ScenarioTest")]
    [RequestSizeLimit(262144)]
    [TooLargeIs413]
    public ActionResult ScenarioTest([FromBody] ScenarioTestRequest request) =>
        ScenarioAnswer(request, Plugin.Instance!.Configuration, PlayniteReader.ReadShownGames, () => _filter?.ScenarioUsers() ?? Array.Empty<ScenarioTests.ScenarioUser>());

    /// <summary>
    /// The scenario answer for the given saved settings and reader: Count, Titles, and parallel to Titles States and BlockedFor
    /// (<see cref="ScenarioTests.Evaluate"/>), Unmatched, Truncated. The reader is asked for all games; <paramref name="users"/> only while ApplyParentalControls is on in the form.
    /// </summary>
    [NonAction]
    public ActionResult ScenarioAnswer(ScenarioTestRequest request, PluginConfiguration saved, Func<bool, List<GameEntry>> read,
        Func<IReadOnlyList<ScenarioTests.ScenarioUser>>? users = null)
    {
        var config = ScenarioTests.Overlay(saved, request.Settings);
        if (config is null || request.Scenario is null || !ScenarioTests.Names.Contains(request.Scenario, StringComparer.Ordinal))
        {
            return BadRequest();
        }

        List<GameEntry> games;
        try
        {
            games = read(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PlayniteBridge: the scenario test could not read the copy of Playnite's data: {Message}", ex.Message);
            return StatusCode(503);
        }

        var result = ScenarioTests.Evaluate(request.Scenario, games, config, config.ApplyParentalControls ? users?.Invoke() : null)!;
        return Ok(new
        {
            Count = result.Lines.Length,
            Titles = result.Lines.Select(l => l.Text).ToArray(),
            States = result.Lines.Select(l => l.State).ToArray(),
            BlockedFor = result.Lines.Select(l => l.BlockedFor).ToArray(),
            result.Unmatched,
            Truncated = false
        });
    }

    private static bool IsSameOrInside(string path, string root)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
            var parent = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
            return full.Equals(parent, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // Not a valid path: ValidateCacheFolder refuses it without writing.
            return false;
        }
    }

    /// <summary>The form values the cache and extras folder checks need.</summary>
    public sealed class CacheFolderRequest
    {
        public string? CacheFolder { get; set; }

        public string? PlayniteFolder { get; set; }

        public bool SeparateExtrasFolder { get; set; }

        public string? ExtrasFolder { get; set; }

        public string? CollectionPicturesFolder { get; set; }
    }

    /// <summary>A Cleanup action: its name and the folders it applies to (root key and folder name only).</summary>
    public sealed class CleanupRequest
    {
        public string? Action { get; set; }

        public List<CleanupItem>? Items { get; set; }
    }

    /// <summary>One folder of a Cleanup action.</summary>
    public sealed class CleanupItem
    {
        public string? Root { get; set; }

        public string? Name { get; set; }
    }

    /// <summary>A Scenario Test: the scenario's name and the admin form's values by setting name.</summary>
    public sealed class ScenarioTestRequest
    {
        public string? Scenario { get; set; }

        public System.Text.Json.JsonElement Settings { get; set; }
    }
}

/// <summary>A request body over the action's [RequestSizeLimit] is answered with 413 (the server throws while the body is read, which Jellyfin would turn into 500).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TooLargeIs413Attribute : Microsoft.AspNetCore.Mvc.Filters.ExceptionFilterAttribute
{
    public override void OnException(Microsoft.AspNetCore.Mvc.Filters.ExceptionContext context)
    {
        for (var ex = context.Exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is Microsoft.AspNetCore.Http.BadHttpRequestException { StatusCode: 413 })
            {
                context.Result = new StatusCodeResult(413);
                context.ExceptionHandled = true;
                return;
            }
        }
    }
}
