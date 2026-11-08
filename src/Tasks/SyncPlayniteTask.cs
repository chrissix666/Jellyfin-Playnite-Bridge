using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Tasks;

/// <summary>
/// Copies Playnite's database files into the plugin's own copy (PlayniteSnapshot) while Playnite is closed. No default trigger;
/// Playnite running ends normally with a note, a real error fails the task. Not in the activity log. A completed copy is followed by the game folders
/// of every visible game group and the clean-up of the plugin's copies and empty folders of games no longer in Playnite (the images are Sync Artwork's).
/// </summary>
public sealed class SyncPlayniteTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly IServerApplicationPaths _paths;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SyncPlayniteTask> _logger;

    public SyncPlayniteTask(ILocalizationManager localization, IServerApplicationPaths paths, ILibraryManager libraryManager, ILogger<SyncPlayniteTask> logger)
    {
        _paths = paths;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Sync Metadata";

    public string Key => "PlayniteBridgeSyncPlaynite";

    public string Description => "Copies Playnite's database while Playnite is closed, makes the game folders and cleans up.";

    public string Category => "Playnite Bridge";

    public bool IsHidden => false;

    public bool IsEnabled => true;

    public bool IsLogged => false;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        PlayniteSnapshot.Note = null;
        var config = Plugin.Instance!.Configuration;
        var playnite = PlayniteReader.ResolveFolder(config.PlayniteFolder)
            ?? throw new InvalidOperationException(PlayniteReader.FolderRefused);
        var result = await PlayniteSnapshot.Sync(playnite, cancellationToken, progress).ConfigureAwait(false);
        switch (result.Outcome)
        {
            case SyncOutcome.PlayniteRunning:
                PlayniteSnapshot.Note = $"The last sync ({DateTime.Now:yyyy-MM-dd HH:mm}) found Playnite running and copied nothing.";
                _logger.LogWarning("PlayniteBridge: Playnite is running or changed its files, nothing was copied from \"{Folder}\"", playnite);
                break;
            default:
                _logger.LogInformation("PlayniteBridge: copied Playnite's data from \"{Folder}\": {Games} games", playnite, result.Games);
                break;
        }

        if (result.Outcome == SyncOutcome.Accepted)
        {
            Prepare(config, playnite, cancellationToken);
            if (config.EnableVideos && config.EnableSteamSpecialFeatures)
            {
                await SteamVideos.Prefetch(PlayniteReader.ReadVisibleGames(config.ShowHidden).Select(SteamVideos.AppIdOf)
                    .Where(id => !string.IsNullOrEmpty(id)).Select(id => id!), _logger, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// After a completed copy: the game folder of every visible game group, then the clean-up (the plugin's copies and the empty folders of games no longer
    /// in Playnite). A sync that wrote the copy again meanwhile skips the clean-up (the games read here are old).
    /// </summary>
    private void Prepare(PluginConfiguration config, string playnite, CancellationToken cancellationToken)
    {
        var generation = PlayniteSnapshot.Generation;
        try
        {
            var steam = ImageCache.SteamFolder();
            var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
            var cache = new ImageCache(config, playnite, ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath), steam, jellyfin, _logger);
            if (cache.PathError is not null)
            {
                _logger.LogWarning("PlayniteBridge: game folder \"{Folder}\" refused, no folders made or cleaned up: {Reason}", cache.CacheFolder, cache.PathError);
                return;
            }

            var games = PlayniteReader.ReadGames();
            var shown = PlayniteReader.ReadVisibleGames(config.ShowHidden);
            var groups = GameGroups.Group(shown, config);
            var visible = shown.ToDictionary(g => g.Id);
            var made = cache.EnsureFolders(groups, visible);
            // the Curator's files of a game that is gone go before the clean-up, so its folder is empty for it
            if (BuildGameImagesTaskBase.CleanUpAllowed(generation, PlayniteSnapshot.Generation))
            {
                Curator.AfterSync(config, cache, playnite, steam, jellyfin, groups, visible, _logger, cancellationToken);
            }

            var removed = BuildGameImagesTaskBase.CleanUpAllowed(generation, PlayniteSnapshot.Generation) ? cache.CleanUp(games) : 0;
            cache.Flush();
            _logger.LogInformation("PlayniteBridge: {Folders} game folders checked, {Removed} empty folders of games no longer in Playnite removed", made, removed);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogWarning("PlayniteBridge: could not make the game folders after the sync: {Message}", ex.Message);
        }
    }
}
