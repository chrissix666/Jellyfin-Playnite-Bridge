using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Tasks;

/// <summary>
/// Creates the game folder of every visible game group (hidden games only with ShowHidden); with Artwork Copies "All" copies every image it
/// lacks and replaces or deletes the plugin's copies that no longer fit (a user's file is never touched), with "Nothing" deletes the plugin's
/// copies; with it the BlurHash of every image file the group's items show (blurhashes.json, BlurHashStore) and removes the stored media info of
/// extra videos no longer found (mediainfo.json, MediaInfoStore); then deletes the plugin's copies and
/// the empty folders of games no longer in Playnite. With GenerateTrickplay it then creates a trickplay folder next to every local extra video without one, with Jellyfin's trickplay
/// settings, media encoder and image encoder, one video after another, never in Jellyfin's database. No default trigger.
/// </summary>
public abstract class BuildGameImagesTaskBase
{
    private readonly IServerApplicationPaths _paths;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IImageEncoder _imageEncoder;
    private readonly IImageProcessor _imageProcessor;
    private readonly EncodingHelper _encodingHelper;
    private readonly IServerConfigurationManager _config;
    private readonly ILogger _logger;

    protected BuildGameImagesTaskBase(
        IServerApplicationPaths paths,
        ILibraryManager libraryManager,
        ILocalizationManager localization,
        IMediaEncoder mediaEncoder,
        IImageEncoder imageEncoder,
        IImageProcessor imageProcessor,
        EncodingHelper encodingHelper,
        IServerConfigurationManager config,
        ILogger logger)
    {
        _paths = paths;
        _libraryManager = libraryManager;
        _mediaEncoder = mediaEncoder;
        _imageEncoder = imageEncoder;
        _imageProcessor = imageProcessor;
        _encodingHelper = encodingHelper;
        _config = config;
        _logger = logger;
    }

    /// <summary>What a run does: the images and the BlurHashes (Sync Artwork), or the extras' thumbnails and trickplay (Sync Extras).</summary>
    protected enum Part
    {
        Artwork,
        Extras
    }

    protected string CategoryName => "Playnite Bridge";

    protected Task Execute(Part part, IProgress<double> progress, CancellationToken cancellationToken) =>
        Task.Run(() => Run(part, progress, cancellationToken), cancellationToken);

    /// <summary>
    /// The existing image files the items of a group show, each once, from the same lists the items are built from: per version (the group's
    /// visible games, and each twin left out of it, whose own item shows its own files; <paramref name="visible"/> = the visible games by id)
    /// VirtualItemsFilter.GameImages (Primary, the backdrops, Logo, Banner, Thumb, Art, Disc); per video extra of the group
    /// VirtualItemsFilter.ExtraImages (Primary or the still frame when it exists, made by Run before; Logo, Art, Disc, Banner, Thumb, the backdrops).
    /// </summary>
    public static IReadOnlyList<string> ShownImages(ImageCache cache, GameGroup group, IReadOnlyDictionary<string, GameEntry> visible, IReadOnlyList<GameExtra> extras,
        string cachePath) =>
        group.Versions.Concat((group.Twins?.Keys ?? Enumerable.Empty<string>()).Select(id => visible[id]))
            .SelectMany(v => VirtualItemsFilter.GameImages(cache, group, v))
            .Concat(extras.SelectMany(e => VirtualItemsFilter.ExtraImages(e, cachePath)))
            .Select(i => i.Path)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>True when no sync wrote the copy between reading the games (<paramref name="atStart"/>) and the clean-up (<paramref name="now"/>).</summary>
    public static bool CleanUpAllowed(int atStart, int now) => atStart == now;

    private async Task Run(Part part, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        var playnite = PlayniteReader.ResolveFolder(config.PlayniteFolder);
        if (playnite is null)
        {
            // A refused Playnite folder counts as no Playnite: nothing built or cleaned up, nothing there opened.
            _logger.LogWarning("PlayniteBridge: Playnite folder \"{Folder}\" refused, nothing built or cleaned up: {Reason}", config.PlayniteFolder, PlayniteReader.FolderRefused);
            progress.Report(100);
            return;
        }

        var steam = ImageCache.SteamFolder();
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var cache = new ImageCache(config, playnite, ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath), steam, jellyfin, _logger);
        if (cache.PathError is not null)
        {
            throw new InvalidOperationException($"Game folder \"{cache.CacheFolder}\" refused: {cache.PathError}");
        }

        progress.Report(0);
        List<GameEntry> games;
        List<GameGroup> groups;
        Dictionary<string, GameEntry> visible;
        // A sync while the folders are built makes the games read here old: the clean-up waits for the next run then.
        var generation = PlayniteSnapshot.Generation;
        try
        {
            // Folders for the groups the library shows, the clean-up against every game.
            games = PlayniteReader.ReadGames();
            var shown = PlayniteReader.ReadVisibleGames(config.ShowHidden);
            groups = GameGroups.Group(shown, config);
            visible = shown.ToDictionary(g => g.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("PlayniteBridge: cannot read the copy of Playnite's data, nothing built or cleaned up: {Message}", ex.Message);
            progress.Report(100);
            return;
        }

        // Only video extras the scan finds inside the checked root (GameExtras.Root: the game folder checked above, or the extras folder with
        // SeparateExtrasFolder), never below a junction: their images for the BlurHashes, their videos for the trickplay.
        var (root, refused) = GameExtras.Root(config, cache.CacheFolder, cache.PathError, f => ImageCache.CheckPaths(f, playnite, steam, jellyfin));
        var extras = root is null ? ExtrasIndex.Empty : GameExtras.Scan(root, Screenshots.AreaName(config), Soundtrack.FolderOf(config), GameExtras.OtherAreas(config), VideoRows.Slots(config));

        // With trickplay the game folders take the first half of the progress, the trickplay videos the second.
        // Sync Extras: the thumbnails (of the trailers with Enable Trailers, of the special features with Enable Extras) and then the trickplay,
        // all only with Enable Videos; Sync Artwork: the images.
        var generate = part == Part.Extras && config.EnableVideos && config.ShowTrickplay;
        var stills = part == Part.Extras && config.EnableVideos;
        var share = generate ? 50.0 : 100.0;
        var hashes = BlurHashStore.Shared(_logger);
        var blurHash = BlurHashCache.ByContent(_imageProcessor.GetImageBlurHash, Path.Combine(_paths.DataPath, "playnite-bridge"));
        var mediaInfo = MediaInfoStore.Shared(_logger);
        // The probes for the still frames, as the filter's (stored answers first, at most two ffprobe processes at once).
        var probes = new MediaInfoCache(
            path => _mediaEncoder.GetMediaInfo(
                new MediaInfoRequest { MediaSource = new MediaSourceInfo { Path = path, Protocol = MediaProtocol.File, VideoType = VideoType.VideoFile }, MediaType = DlnaProfileType.Video },
                CancellationToken.None),
            (path, reason) => _logger.LogWarning("PlayniteBridge: cannot probe the local extra \"{File}\": {Message}", Path.GetFileName(path), reason),
            () => MediaInfoStore.Shared(_logger));
        var hashed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int folders, computed = 0, kept = 0, removed = 0;
        try
        {
            for (var i = 0; i < groups.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (part == Part.Artwork)
                {
                    cache.BuildGroup(groups[i], _imageProcessor, visible);
                }

                // A trailer or special feature without its own image shows a still frame: made here when missing, so its BlurHash is stored
                // before the first request, as Jellyfin's scan extracts the frame (VideoImageProvider).
                foreach (var extra in stills ? extras.ForGroup(groups[i]).Where(e => e.ImagePath is null && (e.Kind == ExtraKind.Trailer && config.EnableTrailers || e.Kind == ExtraKind.Special && config.EnableExtras && VideoRows.Shown(config, e))) : Enumerable.Empty<GameExtra>())
                {
                    await VirtualItemsFilter.StillFrame(extra, _paths.CachePath, probes, _mediaEncoder, _logger).ConfigureAwait(false);
                }

                foreach (var file in ShownImages(cache, groups[i], visible, extras.ForGroup(groups[i]), _paths.CachePath))
                {
                    if (!hashed.Add(file))
                    {
                        continue;
                    }

                    if (!config.EnableBlurHash)
                    {
                        continue;
                    }

                    if (hashes.Refresh(file, blurHash))
                    {
                        computed++;
                    }
                    else
                    {
                        kept++;
                    }
                }

                progress.Report(share * (i + 1) / groups.Count);
            }

            // Only after a complete run: the hashes of files no group shows any more go, and the media info of videos the scan no longer
            // finds. Not while the extras root is refused or missing: the extras were not seen in this run, so their stored entries stay.
            if (root is not null)
            {
                removed = hashes.RemoveExcept(hashed);
                mediaInfo.RemoveExcept(extras.ById.Values.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase));
            }

            folders = 0;
        }
        finally
        {
            // Also after a cancellation: every copy made is listed, every hash computed is kept.
            cache.Flush();
            hashes.Save();
            mediaInfo.Save();
        }

        // Sync Extras marks the documents that still need a page-1 thumbnail (an administrator's browser draws them, Generate Thumbnails); independent of Enable Videos, so before the early return below.
        // Not while the extras root is refused or missing: the folders were not seen then, so the list stays as it was.
        if (part == Part.Extras && root is not null)
        {
            var wanted = Documents.Enabled(config) && config.DocumentsGenerateThumbnails;
            var pending = new List<PendingThumb>();
            var comics = new List<DocumentFile>();
            if (wanted)
            {
                var shortIds = visible.Keys.Select(GameExtras.ShortIdOf).ToHashSet(StringComparer.Ordinal);
                foreach (var folder in extras.Folders.Where(f => shortIds.Contains(f.ShortId)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var documents = Documents.Read(Path.Combine(root, folder.Name), config).Files.Values.SelectMany(f => f).ToList();
                    pending.AddRange(DocumentThumbs.Missing(folder.ShortId, documents));
                    comics.AddRange(documents.Where(Documents.ServerKind));
                }
            }

            DocumentThumbs.WritePending(pending);
            if (wanted)
            {
                _logger.LogInformation("PlayniteBridge: documents without a thumbnail: {Count}", pending.Count);
#if PB_SERVER_CBR
                // CBR comics (Jellyfin 12.x): the server reads page 1 itself, no browser involved.
                var comicsMade = await ComicPages.MakeMissingAsync(comics, _imageProcessor, _logger, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("PlayniteBridge: comic thumbnails made: {Count}", comicsMade);
#endif
            }
        }

        // Sync Extras refreshes the Curator's files (Links / Shortcuts on Every Sync); not when a sync wrote the copy meanwhile (the games read here are old).
        if (part == Part.Extras && CleanUpAllowed(generation, PlayniteSnapshot.Generation))
        {
            Curator.AfterSync(config, cache, playnite, steam, jellyfin, groups, visible, _logger, cancellationToken);
        }

        if (cache.IconsConverted + cache.IcoNotRecognised > 0)
        {
            _logger.LogInformation("PlayniteBridge: icons converted to icon.png: {Converted}; icon.ico not recognised as a copy of the plugin and left alone: {NotRecognised}", cache.IconsConverted, cache.IcoNotRecognised);
        }

        if (!generate)
        {
            _logger.LogInformation("PlayniteBridge: built the game folders in \"{Folder}\": {Groups} game folders, artwork copies {Mode}: {Written} written, {Replaced} replaced, {Deleted} deleted; {Folders} empty folders of games no longer in Playnite removed; blurhashes: {Computed} computed, {Kept} kept, {Removed} removed", cache.CacheFolder, groups.Count, config.ArtworkCopy, cache.Written, cache.Replaced, cache.Deleted, folders, computed, kept, removed);
            progress.Report(100);
            return;
        }

        if (root is null)
        {
            _logger.LogWarning("PlayniteBridge: built the game folders in \"{Folder}\": {Groups} game folders, artwork copies {Mode}: {Written} written, {Replaced} replaced, {Deleted} deleted; {Folders} empty folders of games no longer in Playnite removed; blurhashes: {Computed} computed, {Kept} kept, {Removed} removed; no trickplay of extra videos, extras folder \"{Extras}\": {Reason}", cache.CacheFolder, groups.Count, config.ArtworkCopy, cache.Written, cache.Replaced, cache.Deleted, folders, computed, kept, removed, config.ExtrasFolder, refused);
            progress.Report(100);
            return;
        }

        var plan = TrickplayGenerator.Plan(extras, true);
        int created = 0, skipped = 0, failed = 0;
        for (var i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (await GenerateTrickplay(plan[i], cancellationToken).ConfigureAwait(false))
            {
                case true:
                    created++;
                    TrickplayGenerator.ClearFailed(plan[i].Path);
                    break;
                case false:
                    skipped++;
                    break;
                default:
                    // Not planned again until the video changes (time or length), so its warning comes once per file and state.
                    failed++;
                    TrickplayGenerator.MarkFailed(plan[i].Path);
                    break;
            }

            progress.Report(50 + (50.0 * (i + 1) / plan.Count));
        }

        _logger.LogInformation("PlayniteBridge: built the game folders in \"{Folder}\": {Groups} game folders, artwork copies {Mode}: {Written} written, {Replaced} replaced, {Deleted} deleted; {Folders} empty folders of games no longer in Playnite removed; blurhashes: {Computed} computed, {Kept} kept, {Removed} removed; trickplay of extra videos: {Created} created, {Skipped} skipped, {Failed} failed", cache.CacheFolder, groups.Count, config.ArtworkCopy, cache.Written, cache.Replaced, cache.Deleted, folders, computed, kept, removed, created, skipped, failed);
        progress.Report(100);
    }

    /// <summary>
    /// The trickplay folder of one extra video with the server's trickplay settings, read anew (TrickplayManager.RefreshTrickplayDataInternal without
    /// the manager and its database): true = created; false = skipped (missing file, no video stream, runtime unknown or shorter than the interval,
    /// no width, or the folder appeared meanwhile); null = failed (one warning). A cancellation is passed on.
    /// </summary>
    private async Task<bool?> GenerateTrickplay(GameExtra extra, CancellationToken cancellationToken)
    {
        var options = _config.Configuration.TrickplayOptions;
        var interval = TrickplayGenerator.Interval(options.Interval);
        try
        {
            if (!File.Exists(extra.Path))
            {
                _logger.LogDebug("PlayniteBridge: no trickplay for the local extra \"{File}\": the file is missing", extra.RelativePath);
                return false;
            }

            var source = new MediaSourceInfo { Path = extra.Path, Protocol = MediaProtocol.File, VideoType = VideoType.VideoFile };
            var info = await _mediaEncoder.GetMediaInfo(new MediaInfoRequest { MediaSource = source, MediaType = DlnaProfileType.Video }, cancellationToken).ConfigureAwait(false);
            var stream = info.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video);
            var widths = TrickplayGenerator.Widths(options.WidthResolutions, stream?.Width);
            if (stream is null || info.RunTimeTicks is not long ticks || ticks < TimeSpan.FromMilliseconds(interval).Ticks || widths.Count == 0)
            {
                _logger.LogInformation("PlayniteBridge: no trickplay for the local extra \"{File}\": no video stream, a runtime unknown or shorter than the interval, or no width", extra.RelativePath);
                return false;
            }

            source.Container = info.Container;
            source.RunTimeTicks = info.RunTimeTicks;
            source.MediaStreams = info.MediaStreams;
            var made = await TrickplayGenerator.Generate(
                extra.Path,
                widths,
                options.TileWidth,
                options.TileHeight,
                _paths.TempDirectory,
                (width, token) => _mediaEncoder.ExtractVideoImagesOnIntervalAccelerated(
                    extra.Path,
                    info.Container,
                    source,
                    stream,
                    width,
                    TimeSpan.FromMilliseconds(interval),
                    options.EnableHwAcceleration,
                    options.EnableHwEncoding,
                    options.ProcessThreads,
                    options.Qscale,
                    options.ProcessPriority,
                    options.EnableKeyFrameOnlyExtraction,
                    _encodingHelper,
                    token),
                (inputs, output, width, height) => _imageEncoder.CreateTrickplayTile(
                    new ImageCollageOptions { Width = options.TileWidth, Height = options.TileHeight, InputPaths = inputs, OutputPath = output },
                    options.JpegQuality,
                    width,
                    height),
                cancellationToken).ConfigureAwait(false);
            if (!made)
            {
                _logger.LogInformation("PlayniteBridge: no trickplay for the local extra \"{File}\": its trickplay folder appeared meanwhile and stays as it is", extra.RelativePath);
                return false;
            }

            _logger.LogInformation("PlayniteBridge: created the trickplay of the local extra \"{File}\"", extra.RelativePath);
            return true;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("PlayniteBridge: cannot create the trickplay of the local extra \"{File}\": {Message}", extra.RelativePath, ex.Message);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Jellyfin's media encoder ends a cancelled ffmpeg with an exception of its own; the task ends as cancelled.
            throw new OperationCanceledException(cancellationToken);
        }
    }
}

/// <summary>
/// Sync Artwork: the images of the games' sources into their game folders and the BlurHash of every image the items show; nothing else (the game
/// folders and the clean-up are Sync Metadata's, trickplay and thumbnails of extras are Sync Extras'). No default trigger.
/// </summary>
public sealed class BuildGameImagesTask : BuildGameImagesTaskBase, IScheduledTask
{
    public BuildGameImagesTask(IServerApplicationPaths paths, ILibraryManager libraryManager, ILocalizationManager localization, IMediaEncoder mediaEncoder,
        IImageEncoder imageEncoder, IImageProcessor imageProcessor, EncodingHelper encodingHelper, IServerConfigurationManager config, ILogger<BuildGameImagesTask> logger)
        : base(paths, libraryManager, localization, mediaEncoder, imageEncoder, imageProcessor, encodingHelper, config, logger)
    {
    }

    public string Name => "Sync Artwork";

    public string Key => "PlayniteBridgeBuildGameImages";

    public string Description => "Copies the games' artwork from its sources into the game folders and computes the BlurHash of the images.";

    public string Category => CategoryName;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => Execute(Part.Artwork, progress, cancellationToken);
}

/// <summary>
/// Sync Extras: the thumbnail of every trailer (with Enable Trailers) and special feature (with Enable Extras) without a picture, a still frame made as
/// Jellyfin's scan makes it, and the trickplay of every local extra video that has none (Show Trickplay), all only with Enable Videos. No default trigger.
/// </summary>
public sealed class SyncExtrasTask : BuildGameImagesTaskBase, IScheduledTask
{
    public SyncExtrasTask(IServerApplicationPaths paths, ILibraryManager libraryManager, ILocalizationManager localization, IMediaEncoder mediaEncoder,
        IImageEncoder imageEncoder, IImageProcessor imageProcessor, EncodingHelper encodingHelper, IServerConfigurationManager config, ILogger<SyncExtrasTask> logger)
        : base(paths, libraryManager, localization, mediaEncoder, imageEncoder, imageProcessor, encodingHelper, config, logger)
    {
    }

    public string Name => "Sync Extras";

    public string Key => "PlayniteBridgeSyncExtras";

    public string Description => "Creates the thumbnails and the trickplay of the games' local extra videos.";

    public string Category => CategoryName;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken) => Execute(Part.Extras, progress, cancellationToken);
}

/// <summary>
/// Sync All: Sync Metadata, Sync Artwork and Sync Extras one after the other, in this process (the other tasks are not started through the task
/// manager). Each part guards itself against settings that switch it off. No default trigger.
/// </summary>
public sealed class SyncAllTask : BuildGameImagesTaskBase, IScheduledTask
{
    private readonly SyncPlayniteTask _metadata;

    public SyncAllTask(IServerApplicationPaths paths, ILibraryManager libraryManager, ILocalizationManager localization, IMediaEncoder mediaEncoder,
        IImageEncoder imageEncoder, IImageProcessor imageProcessor, EncodingHelper encodingHelper, IServerConfigurationManager config, ILogger<SyncAllTask> logger,
        ILogger<SyncPlayniteTask> metadataLogger)
        : base(paths, libraryManager, localization, mediaEncoder, imageEncoder, imageProcessor, encodingHelper, config, logger)
    {
        _metadata = new SyncPlayniteTask(localization, paths, libraryManager, metadataLogger);
    }

    public string Name => "Sync All";

    public string Key => "PlayniteBridgeSyncAll";

    public string Description => "Runs Sync Metadata, Sync Artwork and Sync Extras one after the other.";

    public string Category => CategoryName;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // the Curator runs once in Sync All (in whichever of Metadata and Extras comes first with something to do), not in each
        Curator.SetSyncAll(true);
        try
        {
            await _metadata.ExecuteAsync(new Progress<double>(p => progress.Report(p / 3)), cancellationToken).ConfigureAwait(false);
            await Execute(Part.Artwork, new Progress<double>(p => progress.Report(100d / 3 + p / 3)), cancellationToken).ConfigureAwait(false);
            await Execute(Part.Extras, new Progress<double>(p => progress.Report(200d / 3 + p / 3)), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Curator.SetSyncAll(false);
        }
    }
}
