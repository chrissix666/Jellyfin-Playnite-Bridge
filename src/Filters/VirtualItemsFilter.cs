using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using Jellyfin.Plugin.PlayniteBridge.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.PlayniteBridge.Filters;

/// <summary>
/// Answers Jellyfin's item endpoints for the games library with games built from Playnite, without any entry in Jellyfin's database: lists, filter lists,
/// single items, images, similar games, a game's local extras (theme media, trailers, special features, with their images, playback info, direct streams and
/// trickplay sheets), the Steam Special Features, the Suggestions rows, and genres and Playnite series as virtual collection folders. A refresh of the games
/// library (POST /Items/{id}/Refresh) starts the sync task instead; a rename of the library in the dashboard is taken into the LibraryName setting.
/// </summary>
public sealed class VirtualItemsFilter : IAsyncActionFilter
{
    // Still frames of trailers and special features whose extraction failed, with the video's tag; not retried until the video changes.
    private static readonly ConcurrentDictionary<Guid, string> FailedFrames = new();

    // One extraction at a time per still frame.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FrameLocks = new(StringComparer.OrdinalIgnoreCase);

    // The games library found by name, kept while Jellyfin's item of that id still carries the name and no other type. A missing or unsupported
    // library is remembered for NoLibrarySeconds.
    private static volatile CachedLibrary? _library;

    // Library id the single error line for a library that holds real items was logged for.
    private static ILogger? _libraryLog;
    private static Guid _refusedLogged;

    /// <summary>How long "no games library for this name" is kept before Jellyfin's library folders are read again.</summary>
    public const int NoLibrarySeconds = 10;

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly IServerApplicationHost _host;
    private readonly IServerApplicationPaths _paths;
    private readonly IImageProcessor _imageProcessor;
    private readonly IMediaEncoder _mediaEncoder;
    private readonly IServerConfigurationManager _config;
    private readonly ILocalizationManager _localization;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<VirtualItemsFilter> _logger;
    private readonly GameDataCache _data = new();
    private readonly ParentalDataCache _parental = new();
    private readonly BlurHashCache _blurHashes;
    private readonly MediaInfoCache _mediaInfo;
    // Tags of the soundtrack files (ffprobe as audio), in memory per file and state (path + tag).
    private readonly MediaInfoCache _trackInfo;
    // One extraction of an embedded picture at a time per target; tracks whose extraction failed are remembered with the file's tag until it changes.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ArtLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, string> FailedArt = new();
    // Height of each trickplay sheet 0.jpg per file and state (ImageCache.Tag), in memory only; null = unreadable (one warning).
    private readonly ConcurrentDictionary<string, (string Tag, int? Height)> _sheetHeights = new(StringComparer.OrdinalIgnoreCase);
    // Aspect ratio of each extra's Primary file per file and state, in memory only; unreadable = 16:9 (one warning).
    private readonly ImageRatioCache _ratios;
    private string? _lastReadFailure;
    private string? _lastExtrasFailure;
    private string? _lastAnswerFailure;
    private CachedImages? _images;
    private CollectionFolderState? _collectionFolder;

    public VirtualItemsFilter(
        ILibraryManager libraryManager,
        IUserManager userManager,
        IServerApplicationHost host,
        IServerApplicationPaths paths,
        IImageProcessor imageProcessor,
        IMediaEncoder mediaEncoder,
        IServerConfigurationManager config,
        ILocalizationManager localization,
        ITaskManager taskManager,
        ILogger<VirtualItemsFilter> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _host = host;
        _paths = paths;
        _imageProcessor = imageProcessor;
        _mediaEncoder = mediaEncoder;
        _config = config;
        _localization = localization;
        _taskManager = taskManager;
        _logger = logger;
        _blurHashes = new BlurHashCache(
            BlurHashCache.ByContent(imageProcessor.GetImageBlurHash, Path.Combine(paths.DataPath, "playnite-bridge")),
            (path, reason) => logger.LogWarning("PlayniteBridge: no BlurHash of \"{File}\": {Message}", Path.GetFileName(path), reason),
            () => BlurHashStore.Shared(logger));
        _ratios = new ImageRatioCache(
            imageProcessor.GetImageDimensions,
            (path, reason) => logger.LogWarning("PlayniteBridge: cannot read the size of the local extra image \"{File}\": {Message}", Path.GetFileName(path), reason));
        _mediaInfo = new MediaInfoCache(
            path => mediaEncoder.GetMediaInfo(
                new MediaInfoRequest { MediaSource = ExtraSource(path), MediaType = DlnaProfileType.Video },
                CancellationToken.None),
            (path, reason) => logger.LogWarning("PlayniteBridge: cannot probe the local extra \"{File}\": {Message}", Path.GetFileName(path), reason),
            () => MediaInfoStore.Shared(logger));
        _trackInfo = new MediaInfoCache(
            path => mediaEncoder.GetMediaInfo(
                new MediaInfoRequest { MediaSource = ExtraSource(path), MediaType = DlnaProfileType.Audio },
                CancellationToken.None),
            (path, reason) => logger.LogWarning("PlayniteBridge: cannot read the soundtrack file \"{File}\": {Message}", Path.GetFileName(path), reason));
        // The singleton is made by Jellyfin at the first MVC request; from then on ArtworkPlusLink answers through it.
        ArtworkPlusLink.Register(this);
        GlobeLink.Register(this);
        // The gate of "this id is mine" asks Jellyfin whether an id is a real item (OwnIds.Refused); a real item that later gets one of our ids is a conflict at once.
        OwnIds.Log = logger;
        OwnIds.KnownToJellyfin = id => libraryManager.GetItemById(id) is not null;
        // A game found to be a real item leaves the stored game data too, so the lists, tiles and lookups agree (the next build leaves it out).
        OwnIds.ConflictNoted = id => _data.InvalidateGame(id);
        _libraryLog = logger;
        libraryManager.ItemAdded += (_, e) =>
        {
            try
            {
                OwnIds.NoteAdded(e.Item.Id, _data.Current);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "PlayniteBridge: ItemAdded check failed");
            }
        };
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var controller = context.RouteData.Values["controller"] as string;
        var action = context.RouteData.Values["action"] as string;

        // The names below are matched only for Jellyfin's own controllers (OwnIds.IsJellyfinController), never for another plugin's controllers of the same name.
        if (!IsJellyfinAction(context.ActionDescriptor))
        {
            if (controller is "Items" or "UserLibrary" or "Library" or "Image")
            {
                OwnIds.NoteUnrecognised(controller);
            }

            await next().ConfigureAwait(false);
            return;
        }

        // Before the folder check, so a rename is followed even with a refused Playnite folder. Jellyfin's action runs once, as always.
        if (controller == "LibraryStructure" && action == "RenameVirtualFolder")
        {
            var name = Arg<string>(context, "name");
            var newName = Arg<string>(context, "newName");
            var executed = await next().ConfigureAwait(false);
            var configuration = Plugin.Instance!.Configuration;
            var renamed = RenamedLibraryName(controller, action, name, newName, configuration.LibraryName,
                executed.Exception is null && executed.Result is NoContentResult);
            if (renamed is not null)
            {
                SaveLibraryName(configuration, renamed, Plugin.Instance!.SaveConfiguration, _logger);
            }

            return;
        }

        // A refused Playnite folder counts as no Playnite: Jellyfin answers everything itself and nothing there is opened.
        if (PlayniteReader.ResolveFolder(Plugin.Instance!.Configuration.PlayniteFolder) is null)
        {
            WarnOnce(Plugin.Instance!.Configuration.PlayniteFolder, PlayniteReader.FolderRefused);
            await next().ConfigureAwait(false);
            return;
        }

        try
        {
            var result = controller switch
            {
                "Items" when action is "GetItems" or "GetItemsByUserIdLegacy" => ListResult(context),
                "Items" when action is "GetResumeItems" or "GetResumeItemsLegacy" => ResumeResult(context),
                "Filter" when action is "GetQueryFiltersLegacy" => FiltersLegacyResult(context),
                "Filter" when action is "GetQueryFilters" => FiltersResult(context),
                "UserLibrary" when action is "GetItem" or "GetItemLegacy" => await ItemResult(context).ConfigureAwait(false),
                "Image" when action is "GetItemImage" or "GetItemImageByIndex" => await ImageResult(context).ConfigureAwait(false),
                "Library" when action is "GetSimilarItems" => SimilarResult(context),
                "Library" when action is "GetAncestors" => AncestorsResult(context),
#if NET10_0_OR_GREATER
                "Library" when action is "GetItemCollections" => ItemCollectionsResult(context),
#endif
                "UserLibrary" when action is "GetLatestMedia" or "GetLatestMediaLegacy" => LatestResult(context),
                "Movies" when action is "GetMovieRecommendations" => RecommendationsResult(context),
                "Genres" when action is "GetGenres" => GenresResult(context),
                "Library" when action is "GetThemeMedia" => await ExtrasForGame(context, async extras => new AllThemeMediaResult
                {
                    ThemeVideosResult = await ThemeResult(context, extras, ExtraKind.ThemeVideo).ConfigureAwait(false),
                    ThemeSongsResult = await ThemeResult(context, extras, ExtraKind.ThemeSong).ConfigureAwait(false),
                    SoundtrackSongsResult = new ThemeMediaResult { OwnerId = Arg<Guid>(context, "itemId") }
                }).ConfigureAwait(false) ?? EmptyThemeMedia(context, all: true),
                "Library" when action is "GetThemeSongs" => await ExtrasForGame(context, async extras => await ThemeResult(context, extras, ExtraKind.ThemeSong).ConfigureAwait(false)).ConfigureAwait(false) ?? EmptyThemeMedia(context, all: false),
                "Library" when action is "GetThemeVideos" => await ExtrasForGame(context, async extras => await ThemeResult(context, extras, ExtraKind.ThemeVideo).ConfigureAwait(false)).ConfigureAwait(false) ?? EmptyThemeMedia(context, all: false),
                "UserLibrary" when action is "GetLocalTrailers" or "GetLocalTrailersLegacy" => await ExtrasForGame(context, async extras => await ExtraDtos(extras, ExtraKind.Trailer, CanDownload(context)).ConfigureAwait(false)).ConfigureAwait(false),
                "UserLibrary" when action is "GetSpecialFeatures" or "GetSpecialFeaturesLegacy" => await SpecialFeaturesResult(context).ConfigureAwait(false),
                "UserLibrary" when action is "GetIntros" or "GetIntrosLegacy" => FindExtra(context) is null && FindSteam(context) is null
                    ? EmptyForGame(context, () => new QueryResult<BaseItemDto>())
                    : new OkObjectResult(new QueryResult<BaseItemDto>()),
                "MediaInfo" when action is "GetPlaybackInfo" or "GetPostedPlaybackInfo" => PlaybackInfoResult(context),
                "UniversalAudio" when action is "GetUniversalAudioStream" => StreamResult(context),
                "Audio" when action is "GetAudioStream" or "GetAudioStreamByContainer" => StreamResult(context),
                "Videos" when action is "GetVideoStream" or "GetVideoStreamByContainer" => StreamResult(context),
                "Library" when action is "GetDownload" => FindExtra(context) is GameExtra download ? ExtraDownload(download, CanDownload(context)) : DocumentDownload(context) ?? ScreenshotDownload(context) ?? WallpaperDownload(context),
                "Trickplay" when action is "GetTrickplayTileImage" && Plugin.Instance!.Configuration.ShowTrickplay => TrickplayTileResult(context),
                "ItemRefresh" when IsGamesLibraryRefresh(controller, action, Arg<Guid>(context, "itemId"), GamesLibrary(_libraryManager)) => SyncResult(),
                _ => null
            };
            if (result is not null)
            {
                context.Result = result;
                return;
            }
        }
        catch (Exception ex)
        {
            // Once per changed failure, not once per request.
            var state = controller + "." + action + "|" + ex.Message;
            if (Interlocked.Exchange(ref _lastAnswerFailure, state) != state)
            {
                _logger.LogError(ex, "PlayniteBridge: virtual answer for {Controller}.{Action} failed", controller, action);
            }
        }

        await next().ConfigureAwait(false);
    }

    /// <summary>True when <paramref name="descriptor"/> is an action of a Jellyfin API controller (<see cref="OwnIds.IsJellyfinController"/>).</summary>
    public static bool IsJellyfinAction(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor? descriptor) =>
        descriptor is ControllerActionDescriptor controller && OwnIds.IsJellyfinController(controller.ControllerTypeInfo);

    /// <summary>
    /// True for Jellyfin's refresh of the games library <paramref name="library"/> (ItemRefreshController.RefreshItem, POST /Items/{itemId}/Refresh; administrators only),
    /// which starts the sync instead of Jellyfin's scan of the empty folder.
    /// </summary>
    public static bool IsGamesLibraryRefresh(string? controller, string? action, Guid itemId, Guid? library) =>
        controller == "ItemRefresh" && action == "RefreshItem" && itemId != Guid.Empty && library == itemId;

    /// <summary>
    /// The new name of the games library after a rename in the dashboard (LibraryStructureController.RenameVirtualFolder, POST /Library/VirtualFolders/Name; 204 on success), or null.
    /// The setting names the library by name, not by id, so a rename would part the plugin from its library. Only after a successful rename
    /// (<paramref name="succeeded"/>) and only of the library the setting names (case-insensitive). The new name is kept as Jellyfin stores it (the folder name).
    /// </summary>
    public static string? RenamedLibraryName(string? controller, string? action, string? name, string? newName, string? libraryName, bool succeeded) =>
        controller == "LibraryStructure" && action == "RenameVirtualFolder" && succeeded
            && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(newName) && !string.IsNullOrWhiteSpace(libraryName)
            && string.Equals(name, libraryName, StringComparison.OrdinalIgnoreCase)
            ? newName
            : null;

    /// <summary>
    /// Takes the new name of the renamed games library into LibraryName and saves the configuration with <paramref name="save"/>. A failed save is one warning;
    /// the name in memory stays the new one, so the library is found until the next start.
    /// </summary>
    public static void SaveLibraryName(Configuration.PluginConfiguration configuration, string renamed, Action save, ILogger logger)
    {
        var old = configuration.LibraryName;
        configuration.LibraryName = renamed;
        try
        {
            save();
            logger.LogInformation("PlayniteBridge: games library renamed from \"{Old}\" to \"{New}\", setting LibraryName updated", old, renamed);
        }
        catch (Exception ex)
        {
            logger.LogWarning("PlayniteBridge: games library renamed from \"{Old}\" to \"{New}\", setting LibraryName not saved: {Message}", old, renamed, ex.Message);
        }
    }

    /// <summary>True when the sync task in <paramref name="state"/> is started; a running or cancelling one is neither started again nor queued.</summary>
    public static bool StartsSync(TaskState state) => state == TaskState.Idle;

    // The games library's refresh starts the sync task like "Sync Now" on the admin page (ScheduledTasksController.StartTask) and answers with RefreshItem's 204;
    // Jellyfin's QueueRefresh is not called.
    private NoContentResult SyncResult()
    {
        var worker = _taskManager.ScheduledTasks.First(t => t.ScheduledTask is SyncPlayniteTask);
        if (StartsSync(worker.State))
        {
            _taskManager.Execute(worker, new TaskOptions());
            _logger.LogInformation("PlayniteBridge: refresh of the games library intercepted, sync started");
        }
        else
        {
            _logger.LogInformation("PlayniteBridge: refresh of the games library intercepted, sync already running");
        }

        return new NoContentResult();
    }

    private IActionResult? ListResult(ActionExecutingContext context)
    {
        // The reader asks for the document it opens by id (Items?ids=): the documents' items, nothing else.
        if (Arg<Guid[]>(context, "ids") is { Length: > 0 } askedIds && askedIds.All(i => Documents.Decode(i) is not null))
        {
            var found = askedIds.Select(i => OwnIds.Document(i, shortId => ShortIdVisible(context, shortId), DocumentsOf)).ToList();
            if (found.All(f => f is not null))
            {
                var items = found.Select(f => DocumentDto(f!.Value.File, f.Value.Key, _host.SystemId, CanDownload(context))).ToList();
                return new OkObjectResult(new QueryResult<BaseItemDto>(0, items.Count, items));
            }
        }

        // The sibling list of a screenshot row (the query of Jellyfin's photo viewer, ParentId = the row id).
        if (Arg<Guid?>(context, "parentId") is Guid rowId && OwnIds.ScreenshotRow(rowId, shortId => ShortIdVisible(context, shortId)) is { } rowKey
            && ScreenshotSiblings(context, rowKey) is { } siblings)
        {
            return siblings;
        }

        // The same for the pictures of a wallpapers row.
        if (Arg<Guid?>(context, "parentId") is Guid wallpaperRowId && OwnIds.WallpaperRow(wallpaperRowId, shortId => ShortIdVisible(context, shortId)) is { } wallpaperRowKey
            && WallpaperSiblings(context, wallpaperRowKey) is { } wallpaperSiblings)
        {
            return wallpaperSiblings;
        }

        // A search never lists games (no games in search results); it keeps Jellyfin's own answer.
        if (!string.IsNullOrEmpty(Arg<string>(context, "searchTerm")))
        {
            return null;
        }

        var types = Arg<BaseItemKind[]>(context, "includeItemTypes") ?? Array.Empty<BaseItemKind>();
        if (types.Length == 1 && types[0] == BaseItemKind.BoxSet && GamesLibraryFor(context) is Guid collectionsLibrary)
        {
            return CollectionsResult(context, collectionsLibrary);
        }

        // Trailers and other types keep Jellyfin's own (empty) answer.
        if (!AsksMovies(types))
        {
            return null;
        }

        // Groups over all visible games; a collection folder keeps the groups with a version of its series.
        if (ParentFor(context) is not (Guid libraryId, var collection, var data))
        {
            return null;
        }

        // The extras index only for a hasTrailer/hasSpecialFeature/hasThemeSong/hasThemeVideo filter (GameQuery.WithExtras).
        var (page, total) = GameQuery.Run(data.Groups, GameQuery.WithExtras(new GameQueryRequest
        {
            VersionIds = collection?.Games.Select(g => g.Id).ToHashSet(),
            SortBy = Arg<ItemSortBy[]>(context, "sortBy") ?? Array.Empty<ItemSortBy>(),
            SortOrder = Arg<SortOrder[]>(context, "sortOrder") ?? Array.Empty<SortOrder>(),
            StartIndex = Arg<int?>(context, "startIndex"),
            Limit = Arg<int?>(context, "limit"),
            NameStartsWith = Arg<string>(context, "nameStartsWith"),
            NameLessThan = Arg<string>(context, "nameLessThan"),
            Filters = Arg<ItemFilter[]>(context, "filters") ?? Array.Empty<ItemFilter>(),
            IsFavorite = Arg<bool?>(context, "isFavorite"),
            IsPlayed = Arg<bool?>(context, "isPlayed"),
            PlayedMark = Plugin.Instance!.Configuration.PlayedMark,
            Genres = Arg<string[]>(context, "genres") ?? Array.Empty<string>(),
            GenreIds = Arg<Guid[]>(context, "genreIds") ?? Array.Empty<Guid>(),
            Years = Arg<int[]>(context, "years") ?? Array.Empty<int>(),
            OfficialRatings = Arg<string[]>(context, "officialRatings") ?? Array.Empty<string>(),
            Tags = Arg<string[]>(context, "tags") ?? Array.Empty<string>(),
            PersonIds = Arg<Guid[]>(context, "personIds") ?? Array.Empty<Guid>(),
            StudioIds = Arg<Guid[]>(context, "studioIds") ?? Array.Empty<Guid>(),
            HasTrailer = Arg<bool?>(context, "hasTrailer"),
            HasSubtitles = Arg<bool?>(context, "hasSubtitles"),
            HasThemeSong = Arg<bool?>(context, "hasThemeSong"),
            HasThemeVideo = Arg<bool?>(context, "hasThemeVideo"),
            HasSpecialFeature = Arg<bool?>(context, "hasSpecialFeature"),
            VideoTypes = Arg<VideoType[]>(context, "videoTypes") ?? Array.Empty<VideoType>(),
            Is3D = Arg<bool?>(context, "is3D"),
            IsHd = Arg<bool?>(context, "isHd"),
            Is4K = Arg<bool?>(context, "is4K")
        }, () => Extras(true)));

        var images = Images();
        return new OkObjectResult(new QueryResult<BaseItemDto>(
            Arg<int?>(context, "startIndex"),
            total,
            page.Select(g => ListDto(g, libraryId, images)).ToList()));
    }

    /// <summary>
    /// The type rule for every answer that hands out game data (Items, Resume, Genres, Items/Filters, Items/Filters2): games are movies to Jellyfin, so only a request
    /// whose includeItemTypes is empty or names Movie gets games. Any other type keeps Jellyfin's own answer, so a TV or music page never gets game data.
    /// </summary>
    public static bool AsksMovies(IReadOnlyCollection<BaseItemKind>? types) => types is null || types.Count == 0 || types.Contains(BaseItemKind.Movie);

    /// <summary>
    /// The Suggestions tab's "Recently played" row of the React library page (GET UserItems/Resume with parentId), answered like the legacy page's Items query
    /// with Filters=IsResumable sorted by DatePlayed. Only with parentId = the games library, so the home page's row (no parentId) never gets games.
    /// </summary>
    private IActionResult? ResumeResult(ActionExecutingContext context)
    {
        if (!AsksMovies(Arg<BaseItemKind[]>(context, "includeItemTypes")) || GamesLibraryFor(context) is not Guid libraryId || ParentFor(context) is not (_, null, var data))
        {
            return null;
        }

        var (page, total) = GameQuery.Run(data.Groups, new GameQueryRequest
        {
            SortBy = new[] { ItemSortBy.DatePlayed },
            SortOrder = new[] { SortOrder.Descending },
            StartIndex = Arg<int?>(context, "startIndex"),
            Limit = Arg<int?>(context, "limit"),
            Filters = new[] { ItemFilter.IsResumable },
            PlayedMark = Plugin.Instance!.Configuration.PlayedMark
        });

        var images = Images();
        return new OkObjectResult(new QueryResult<BaseItemDto>(
            Arg<int?>(context, "startIndex"),
            total,
            page.Select(g => ListDto(g, libraryId, images)).ToList()));
    }

    /// <summary>The Collections tab: Playnite series as virtual folders (a click opens Jellyfin's list of the folder's games).</summary>
    private OkObjectResult CollectionsResult(ActionExecutingContext context, Guid libraryId)
    {
        var data = Data(context);
        var collections = GameSuggestions.Sort(
            data.Collections,
            Arg<ItemSortBy[]>(context, "sortBy") ?? Array.Empty<ItemSortBy>(),
            Arg<SortOrder[]>(context, "sortOrder") ?? Array.Empty<SortOrder>());
        IEnumerable<GameCollection> page = collections.Skip(Arg<int?>(context, "startIndex") ?? 0);
        if (Arg<int?>(context, "limit") is int limit)
        {
            page = page.Take(limit);
        }

        var images = Images();
        return new OkObjectResult(new QueryResult<BaseItemDto>(
            Arg<int?>(context, "startIndex"),
            collections.Count,
            page.Select(c => CollectionDto(c, data, libraryId, images)).ToList()));
    }

    /// <summary>
    /// The Suggestions tab's "Latest" row, only when the web script marks the request (pbLatest=1, see <see cref="AsksLatest"/>), so no client without it gets games there.
    /// </summary>
    private IActionResult? LatestResult(ActionExecutingContext context)
    {
        if (!AsksLatest(context.HttpContext.Request.Query["pbLatest"].FirstOrDefault()))
        {
            return null;
        }

        var types = Arg<BaseItemKind[]>(context, "includeItemTypes") ?? Array.Empty<BaseItemKind>();
        if (!types.Contains(BaseItemKind.Movie) || GamesLibraryFor(context) is not Guid libraryId)
        {
            return null;
        }

        var images = Images();
        // One tile per group: the latest of the main versions.
        var data = Data(context);
        return new OkObjectResult(GameSuggestions.Latest(data.Groups.Select(g => g.Main), Arg<int?>(context, "limit") ?? 20, Arg<bool?>(context, "isPlayed"),
                Plugin.Instance!.Configuration.PlayedMark)
            .Select(g => ListDto(data.GroupOf(g), libraryId, images)).ToList());
    }

    /// <summary>The Latest row goes only to clients that run the web script, which marks the Suggestions tab's Latest request with pbLatest=1.</summary>
    public static bool AsksLatest(string? marker) => marker == "1";

    /// <summary>
    /// The Suggestions tab's recommendation rows. Jellyfin Web sends no parentId here, so the web script adds pbTopParentId (the library of the open tab);
    /// either one must be the games library.
    /// </summary>
    private IActionResult? RecommendationsResult(ActionExecutingContext context)
    {
        var parentId = Arg<Guid?>(context, "parentId")
            ?? (Guid.TryParse(context.HttpContext.Request.Query["pbTopParentId"].ToString(), out var marker) ? (Guid?)marker : null);
        var libraryId = !OwnIds.IsGamesLibrary(parentId, GamesLibrary(_libraryManager)) ? null : GamesLibrary(context);
        if (libraryId is null || libraryId != parentId)
        {
            return null;
        }

        // Over the main versions: one tile per group, and no row shows a version of its own baseline's group.
        var images = Images();
        var data = Data(context);
        return new OkObjectResult(GameSuggestions.Recommendations(data.Groups.Select(g => g.Main).ToList(), Arg<int?>(context, "categoryLimit") ?? 5, Arg<int?>(context, "itemLimit") ?? 8)
            .Select(r => new RecommendationDto
            {
                RecommendationType = r.Type,
                BaselineItemName = r.BaselineItemName,
                CategoryId = Guid.Parse(r.BaselineId),
                Items = r.Items.Select(g => ListDto(data.GroupOf(g), libraryId.Value, images)).ToList()
            })
            .ToList());
    }

    /// <summary>The Genres tab: the genres of the visible games, sorted by name.</summary>
    private IActionResult? GenresResult(ActionExecutingContext context)
    {
        if (!AsksMovies(Arg<BaseItemKind[]>(context, "includeItemTypes")) || GamesLibraryFor(context) is null)
        {
            return null;
        }

        var genres = Genres(Data(context).Games);
        if ((Arg<SortOrder[]>(context, "sortOrder") ?? Array.Empty<SortOrder>()).FirstOrDefault() == SortOrder.Descending)
        {
            genres.Reverse();
        }

        IEnumerable<NameGuidPair> page = genres.Skip(Arg<int?>(context, "startIndex") ?? 0);
        if (Arg<int?>(context, "limit") is int limit)
        {
            page = page.Take(limit);
        }

        return new OkObjectResult(new QueryResult<BaseItemDto>(Arg<int?>(context, "startIndex"), genres.Count, page.Select(GenreDto).ToList()));
    }

    /// <summary>"More like this" on a game's detail page: similar games (main versions, the page's group left out), played ones included, at most 12.</summary>
    private IActionResult? SimilarResult(ActionExecutingContext context)
    {
        var group = Find(context, Arg<Guid>(context, "itemId"), out var data) is { Kind: GameHitKind.Game } hit ? hit.Group : null;
        var libraryId = group is null ? null : GamesLibrary(context);
        if (group is null || libraryId is null)
        {
            return null;
        }

        var images = Images();
        var similar = GameSuggestions.Similar(data.Groups.Where(g => g != group).Select(g => g.Main), group.Main, Math.Min(Arg<int?>(context, "limit") ?? 12, 12));
        return new OkObjectResult(new QueryResult<BaseItemDto>(similar.Select(g => ListDto(data.GroupOf(g), libraryId.Value, images)).ToList()));
    }

    /// <summary>
    /// GET Items/{itemId}/Ancestors (LibraryController.GetAncestors) for a game or a series folder: the games library, then the user root folder,
    /// as Jellyfin lists a movie's ancestors. The 12.1 toolbar asks it on every page to mark the open library. Null for any other id.
    /// </summary>
    private IActionResult? AncestorsResult(ActionExecutingContext context)
    {
        if (Find(context, Arg<Guid>(context, "itemId"), out _) is not { Kind: GameHitKind.Game or GameHitKind.Collection }
            || GamesLibrary(context) is not Guid libraryId)
        {
            return null;
        }

        return new OkObjectResult(new List<BaseItemDto>
        {
            new()
            {
                Id = libraryId,
                Name = _libraryManager.GetItemById(libraryId)?.Name,
                ServerId = _host.SystemId,
                Type = BaseItemKind.CollectionFolder,
                IsFolder = true
            },
            new()
            {
                Id = _libraryManager.GetUserRootFolder().Id,
                Name = _libraryManager.GetUserRootFolder().Name,
                ServerId = _host.SystemId,
                Type = BaseItemKind.UserRootFolder,
                IsFolder = true
            }
        });
    }

    /// <summary>The empty answer of Items/{itemId}/Collections (the page of a game without a series): total 0, no items.</summary>
    public static QueryResult<BaseItemDto> EmptyItemCollections(int? startIndex) => new(startIndex, 0, new List<BaseItemDto>());

#if NET10_0_OR_GREATER
    /// <summary>
    /// Jellyfin 12.x GET Items/{itemId}/Collections (LibraryController.GetItemCollections) for a game id (any version or twin) the request's user may see: the Playnite
    /// series of that version that are collections, as the Collections tab's folders (<see cref="CollectionDto"/>), sorted and paged by GameSuggestions.ItemCollections,
    /// in Jellyfin's QueryResult; a game without one gets an empty page. Null (Jellyfin answers) for any other id except a virtual collection folder (empty page),
    /// without a user (also an API key), when the user may not see the games library and for a game blocked by parental controls.
    /// </summary>
    private IActionResult? ItemCollectionsResult(ActionExecutingContext context)
    {
        var found = Find(context, Arg<Guid>(context, "itemId"), out var data);
        // A virtual collection folder is in no collection: answer with the empty page, not Jellyfin's 404 (which the detail page logs as a failed request).
        if (found is { Kind: GameHitKind.Collection })
        {
            return new OkObjectResult(EmptyItemCollections(Arg<int?>(context, "startIndex")));
        }

        if (found is not { Kind: GameHitKind.Game } hit
            || VisibleGamesAccess(_libraryManager, _userManager, context.HttpContext.User, Arg<Guid?>(context, "userId")) is not (Guid libraryId, User _))
        {
            return null;
        }

        var (page, total) = GameSuggestions.ItemCollections(hit.Game!, data.Collections, Arg<int?>(context, "startIndex"), Arg<int?>(context, "limit"));
        var images = Images();
        return new OkObjectResult(new QueryResult<BaseItemDto>(
            Arg<int?>(context, "startIndex"),
            total,
            page.Select(c => CollectionDto(c, data, libraryId, images)).ToList()));
    }
#endif

    private IActionResult? FiltersLegacyResult(ActionExecutingContext context)
    {
        if (!AsksMovies(Arg<BaseItemKind[]>(context, "includeItemTypes")) || ParentFor(context) is not (_, var collection, var data))
        {
            return null;
        }

        var games = collection?.Games ?? data.Games;
        var entries = GameQuery.ShowsFilterEntries(FilterMarker(context));
        return new OkObjectResult(FiltersLegacy(games, entries, entries ? ExtraKindsOf(games, data) : new HashSet<ExtraKind>()));
    }

    /// <summary>The kinds of local extras of the groups of <paramref name="games"/> in the extras index (read again at most every 10 seconds).</summary>
    private IReadOnlySet<ExtraKind> ExtraKindsOf(IReadOnlyList<GameEntry> games, GameData data) =>
        GameQuery.ExtraKindsOf(Extras(true), games.Select(data.GroupOf).Distinct());

    /// <summary>
    /// The answer of Items/Filters for <paramref name="games"/>: the filter entries (installed, source, features, and one per kind of local extra in
    /// <paramref name="extraKinds"/>) ride along as tags, only when the script asks (<paramref name="entries"/>, pbFilters=1); it moves them into their own groups
    /// or shows Jellyfin's own checkbox of that kind. Every name goes through GameQuery.FilterSafe.
    /// </summary>
    public static QueryFiltersLegacy FiltersLegacy(IReadOnlyList<GameEntry> games, bool entries, IReadOnlySet<ExtraKind> extraKinds)
    {
        var tags = Distinct(games.SelectMany(g => g.Tags));
        return new QueryFiltersLegacy
        {
            Genres = Distinct(games.SelectMany(g => g.Genres)),
            Tags = entries ? tags.Concat(Distinct(GameQuery.PseudoTags(games).Concat(extraKinds.Select(GameQuery.ExtraFilter)))).ToArray() : tags,
            OfficialRatings = Distinct(games.Select(GameQuery.OfficialRating).OfType<string>()),
            Years = games.Select(g => g.Year).OfType<int>().Distinct().OrderByDescending(y => y).ToArray()
        };
    }

    private IActionResult? FiltersResult(ActionExecutingContext context)
    {
        if (!AsksMovies(Arg<BaseItemKind[]>(context, "includeItemTypes")) || ParentFor(context) is not (_, var collection, var data))
        {
            return null;
        }

        var games = collection?.Games ?? data.Games;
        var entries = GameQuery.ShowsFilterEntries(FilterMarker(context));
        return new OkObjectResult(Filters(games, entries, entries ? ExtraKindsOf(games, data) : new HashSet<ExtraKind>()));
    }

    /// <summary>
    /// The answer of Items/Filters2 for <paramref name="games"/>: list.html's filter menu shows only genres, so the filter entries (and one per kind of local extra
    /// in <paramref name="extraKinds"/>) ride along as genres with derived ids, only when the script asks (<paramref name="entries"/>, pbFilters=1).
    /// Names go through GameQuery.FilterSafe, ids are those of the raw names.
    /// </summary>
    public static QueryFilters Filters(IReadOnlyList<GameEntry> games, bool entries, IReadOnlySet<ExtraKind> extraKinds)
    {
        var genres = Distinct(Genres(games));
        var extras = extraKinds.Select(GameQuery.ExtraFilter).Select(v => new NameGuidPair { Id = VirtualIds.Filter(v), Name = v });
        return new QueryFilters
        {
            Genres = entries ? genres.Concat(Distinct(GameQuery.PseudoGenres(games).Concat(extras))).ToArray() : genres,
            Tags = Distinct(games.SelectMany(g => g.Tags))
        };
    }

    private async Task<IActionResult?> ItemResult(ActionExecutingContext context)
    {
        var id = Arg<Guid>(context, "itemId");
        var hit = Find(context, id, out var data);
        if (hit is null)
        {
            return FindExtra(context) is GameExtra extra
                ? extra.Kind == ExtraKind.Soundtrack
                    ? await SoundtrackDto(extra).ConfigureAwait(false) is { } track ? new OkObjectResult(track) : new NotFoundResult()
                    : new OkObjectResult(await ExtraDto(extra, CanDownload(context)).ConfigureAwait(false))
                : FindSteam(context) is SteamVideoItem steam ? new OkObjectResult(await SteamDtoWithFormat(steam, context.HttpContext.RequestAborted).ConfigureAwait(false))
                : FindDocument(context) is (DocumentFile document, DocumentKey documentKey) ? new OkObjectResult(DocumentDto(document, documentKey, _host.SystemId, CanDownload(context)))
                : FindScreenshot(context) is (ScreenshotFile file, ScreenshotKey shot) ? new OkObjectResult(ScreenshotDto(file, shot, _host.SystemId, CanDownload(context), Plugin.Instance!.Configuration, ScreenshotsOf(shot.ShortId) ?? ScreenshotArea.Empty))
                : FindWallpaper(context) is (WallpaperFile picture, WallpaperKey pictureKey) ? new OkObjectResult(WallpaperDto(picture, pictureKey, _host.SystemId, CanDownload(context), Plugin.Instance!.Configuration, WallpapersOf(pictureKey.ShortId) ?? WallpaperArea.Empty)) : null;
        }

        var libraryId = GamesLibrary(context);
        if (libraryId is null)
        {
            return null;
        }

        // A collection folder, a genre, a developer, a publisher or a feature/status/source filter entry of the games (list.html asks for the item it lists).
        if (hit.Kind != GameHitKind.Game)
        {
            return new OkObjectResult(hit.Kind switch
            {
                GameHitKind.Collection => CollectionDto(hit.Collection!, data, libraryId.Value, Images()),
                GameHitKind.Person => NamedDto(new NameGuidPair { Id = id, Name = hit.Name }, BaseItemKind.Person),
                GameHitKind.Studio => NamedDto(new NameGuidPair { Id = id, Name = hit.Name }, BaseItemKind.Studio),
                // A filter entry as Genre named without prefix: list.html titles its grid with it.
                _ => NamedDto(new NameGuidPair { Id = id, Name = hit.Name }, BaseItemKind.Genre)
            });
        }

        var game = hit.Game!;
        var group = hit.Group!;
        var dto = ToDto(group, game, libraryId.Value, Images());
        // Several versions: Jellyfin's "Version" list (filled by the web script, a virtual item gets none from Jellyfin Web), main version first.
        // A twin left out of its group gets the group's list too.
        if (group.Versions.Count > 1)
        {
            dto.MediaSources = group.Versions.Select(v => new MediaSourceInfo
            {
                Id = Guid.Parse(v.Id).ToString("N"),
                Name = GameGroups.OptionLabel(group, v),
                Type = MediaSourceType.Default,
                Protocol = MediaProtocol.File,
                RunTimeTicks = v.Playtime > 0 ? v.Playtime * TimeSpan.TicksPerSecond : null
            }).ToArray();
        }

        // Only the single item: Jellyfin Web shows the Trailer button and the Specials row by these counts, counted in the games the user may see.
        (dto.LocalTrailerCount, dto.SpecialFeatureCount) = ExtraCounts(Extras(true), data, group, game, Plugin.Instance!.Configuration);
        // The merged Steam Special Features are in the Special Features row, so the row (it shows by this count) counts them.
        var steamAll = SteamSpecials(data.DetailGroupOf(game.Id) ?? group, game.Id, Plugin.Instance.Configuration);
        var nativeRow = NativeRow(Enabled(Extras(true).ForVersion(data.DetailGroupOf(game.Id) ?? group, game.Id, Plugin.Instance.Configuration)), steamAll, Plugin.Instance.Configuration);
        dto.SpecialFeatureCount = nativeRow.Native.Count + nativeRow.Steam.Count;

        var detail = PlayniteReader.ReadDetail(Guid.Parse(game.Id));
        dto.Overview = PlayniteReader.HtmlToText(detail?.Description);
        // Only the single item: Jellyfin's genre and director rows read these; developers appear as directors.
        dto.GenreItems = game.GenreRefs;
        dto.People = game.DeveloperRefs.Select(d => new BaseItemPerson { Name = d.Name, Id = d.Id, Type = PersonKind.Director }).ToArray();
        var config = Plugin.Instance!.Configuration;
        dto.ExternalUrls = GameLinks.Arrange(detail?.Links ?? Array.Empty<ExternalUrl>(), config.LinkHostLimiter, config.LinkHosts, config.LinkHostOrder);
        return new OkObjectResult(dto);
    }

    private async Task<IActionResult?> ImageResult(ActionExecutingContext context)
    {
        var id = Arg<Guid>(context, "itemId");
        var type = Arg<ImageType>(context, "imageType");
        // The base game data, not the user's: Jellyfin's image endpoints are anonymous and answer every item whose id is known.
        var hit = Find(id, out var data);
        var game = hit?.Kind == GameHitKind.Game ? hit.Game : null;
        // The square list view image (the script asks it with pbIcon=1): the game's icon and/or square image instead of the cover, as List Image says.
        var icon = game is not null && type == ImageType.Primary && context.HttpContext.Request.Query["pbIcon"].FirstOrDefault() == "1";
        if (game is null)
        {
            // A collection folder shows its poster game's cover and backdrop, nothing else.
            var collection = hit?.Kind == GameHitKind.Collection ? hit.Collection : null;
            if (collection is null)
            {
                // A document: its cover (first page of a comic archive, cover of an EPUB, else a neutral tile) as Primary; no other image.
                if (id != Guid.Empty && OwnIds.Document(id, shortId => Data().HasShortId(shortId), DocumentsOfCached) is { } documentImage)
                {
                    if (type != ImageType.Primary || (Arg<int?>(context, "imageIndex") ?? 0) != 0)
                    {
                        return new NotFoundResult();
                    }

                    // The cover is worked out once per file version (Documents.CoverOf, bounded, single flight) and served by the image processor like any picture.
                    // A document without a picture gets the neutral tile (a small SVG).
                    var cover = Documents.CoverOf(documentImage.File, System.IO.Path.Combine(_paths.CachePath, "playnite-bridge", "document-covers"));
                    if (cover.FilePath is not null)
                    {
                        return await ProcessedImage(context, cover.FilePath, type, blurHash: false).ConfigureAwait(false);
                    }

                    context.HttpContext.Response.Headers.CacheControl = "public, max-age=86400";
                    return new FileContentResult(cover.Svg!, "image/svg+xml");
                }

                // A screenshot: its file as Primary, sized by Jellyfin's image processor; no other image and no BlurHash.
                if (id != Guid.Empty && ImageScreenshot(id) is ScreenshotFile screenshot)
                {
                    return type == ImageType.Primary && (Arg<int?>(context, "imageIndex") ?? 0) == 0
                        ? await ProcessedImage(context, screenshot.Path, type, blurHash: false).ConfigureAwait(false)
                        : new NotFoundResult();
                }

                // A wallpaper or artwork picture: as a screenshot.
                if (id != Guid.Empty && ImageWallpaper(id) is WallpaperFile wallpaper)
                {
                    return type == ImageType.Primary && (Arg<int?>(context, "imageIndex") ?? 0) == 0
                        ? await ProcessedImage(context, wallpaper.Path, type, blurHash: false).ConfigureAwait(false)
                        : new NotFoundResult();
                }

                // A Steam Special Feature: its picture as Primary, never a 404 for a known item. In order: the thumbnail read once by the server into the cache folder, a still frame of the
                // HLS stream made by Jellyfin's ffmpeg, a backdrop of the game (not the first of several), a neutral tile (SteamVideos.Picture). The thumbnail and the frame are final
                // (pinned under their tag); the backdrop and the neutral tile are not, since a later grab changes the tag.
                if (SteamEnabled() && OwnIds.Steam(id) is SteamVideoItem steam)
                {
                    if (type != ImageType.Primary || (Arg<int?>(context, "imageIndex") ?? 0) != 0)
                    {
                        return new NotFoundResult();
                    }

                    var picture = await SteamVideos.Picture(_paths.CachePath, steam, ExtractSteamFrame, () => SteamBackdrops(steam), _logger).ConfigureAwait(false);
                    if (picture.Path is null)
                    {
                        context.HttpContext.Response.Headers.CacheControl = "public, no-cache";
                        return new FileContentResult(SteamVideos.NeutralTile(), "image/svg+xml");
                    }

                    return await ProcessedImage(context, picture.Path, type, pinned: picture.Kind is "Thumbnail" or "Frame", blurHash: picture.Kind is "Thumbnail" or "Frame").ConfigureAwait(false);
                }

                // A local extra of the existing index (never a scan): its image file of the type and index, else for Primary of a trailer or special feature a still frame.
                if (!OwnIds.MayBeExtra(id) || OwnIds.Extra(id, Extras(false)) is not GameExtra extra)
                {
                    return null;
                }

                if (extra.Kind == ExtraKind.Soundtrack)
                {
                    var art = type == ImageType.Primary && (Arg<int?>(context, "imageIndex") ?? 0) == 0 ? await SoundtrackArt(extra).ConfigureAwait(false) : null;
                    return art is null ? new NotFoundResult() : await ProcessedImage(context, art, type).ConfigureAwait(false);
                }

                var (file, still) = GameExtras.Image(extra, type, Arg<int?>(context, "imageIndex") ?? 0);
                var image = file ?? (still ? await StillFrame(extra).ConfigureAwait(false) : null);
                return image is null ? new NotFoundResult() : await ProcessedImage(context, image, type).ConfigureAwait(false);
            }

            if (type is not (ImageType.Primary or ImageType.Backdrop))
            {
                return new NotFoundResult();
            }

            // An own poster (CollectionPictures) belongs to the collection, not to a game: it comes before the poster game's cover.
            if (type == ImageType.Primary && (Arg<int?>(context, "imageIndex") ?? 0) == 0 && OwnPictures(collection).Poster is string ownPosterFile)
            {
                var askedOwn = Arg<string>(context, "tag");
                return await ProcessedImage(context, ownPosterFile, type, pinned: string.IsNullOrEmpty(askedOwn) || CollectionPictures.Tag(ownPosterFile) == askedOwn).ConfigureAwait(false);
            }

            // A backdrop by its tag: the own backdrops, then the backdrops of the games the asking user may see (a collection page lists them, CollectionBackdropList).
            // No tag or no match: the own backdrop of the asked index, else the poster game's.
            if (type == ImageType.Backdrop)
            {
                var askedBackdrop = Arg<string>(context, "tag");
                string? file = null;
                if (!string.IsNullOrEmpty(askedBackdrop))
                {
                    var userData = Data(context);
                    file = CollectionBackdropByTag(userData, collection, userData.Find(id) is { Kind: GameHitKind.Collection } mine ? mine.Collection : null, askedBackdrop);
                }

                file ??= OwnBackdropsShown(Plugin.Instance!.Configuration) ? OwnPictures(collection).Backdrops.ElementAtOrDefault(Arg<int?>(context, "imageIndex") ?? 0) : null;
                if (file is not null)
                {
                    return await ProcessedImage(context, file, type, pinned: string.IsNullOrEmpty(askedBackdrop) || ImageCache.Tag(file) == askedBackdrop || CollectionPictures.Tag(file) == askedBackdrop).ConfigureAwait(false);
                }
            }

            // Parental controls may give a user's tile another poster game (CollectionDto of the user's game data): use the game whose tag the client asks for.
            var asked = Plugin.Instance!.Configuration.ApplyParentalControls ? Arg<string>(context, "tag") : null;
            game = (string.IsNullOrEmpty(asked) ? null : collection.Games.FirstOrDefault(g => type == ImageType.Primary
                    ? ImageCache.Tag(Cover(Images(), data.GroupOf(g), g)) == asked
                    : Images().Backdrops(data.GroupOf(g), g).Any(b => ImageCache.Tag(b) == asked)))
                ?? collection.Poster;
            if (game is null)
            {
                return new NotFoundResult();
            }
        }

        // A user's file in the game folder first, else the plugin's copy, else Playnite's or Steam's file in place; sized by Jellyfin's image processor as the client asks.
        var images = Images();
        var index = Arg<int?>(context, "imageIndex") ?? 0;
        // Artwork on Version Change: a version's image asked with another version's tag (ImageOwner).
        var askedTag = Arg<string>(context, "tag");
        var askedGame = game;
        if (hit?.Kind == GameHitKind.Game && !icon)
        {
            game = ImageOwner(images, data, game, type, index, askedTag);
        }

        var group = data.GroupOf(game);
        var path = type switch
        {
            ImageType.Primary => (icon ? ListImagePath(Plugin.Instance!.Configuration.ListImage, () => images.Icon(group, game), () => images.Square(game)) : null) ?? images.Get(group, game, type),
            ImageType.Backdrop when index == 0 => images.Get(group, game, type),
            // Only the game folder has more than one backdrop.
            ImageType.Backdrop when index > 0 => images.Backdrops(group, game).ElementAtOrDefault(index),
            ImageType.Logo or ImageType.Banner or ImageType.Thumb or ImageType.Art or ImageType.Disc => images.Get(group, game, type),
            _ => null
        };
        // Only a game image served as the asked version's own file under its own tag is cached for a year; another version's image (or the own one under a tag it does not carry)
        // must not stay pinned in the browser under that address.
        var pinned = hit?.Kind != GameHitKind.Game || icon || string.IsNullOrEmpty(askedTag)
            || (ReferenceEquals(game, askedGame) && path is not null && ImageCache.Tag(path) == askedTag);
        return path is null ? new NotFoundResult() : await ProcessedImage(context, path, type, icon, pinned).ConfigureAwait(false);
    }

    /// <summary>The list view image of a version in the order <paramref name="mode"/> sets (icon, square); null leaves the cover, the implicit last fallback.</summary>
    public static string? ListImagePath(Configuration.ListImageMode mode, Func<string?> icon, Func<string?> square) => mode switch
    {
        Configuration.ListImageMode.IconThenSquare => icon() ?? square(),
        Configuration.ListImageMode.Square => square(),
        Configuration.ListImageMode.SquareThenIcon => square() ?? icon(),
        _ => icon()
    };

    /// <summary>
    /// An image file sized by Jellyfin's image processor as the client asks. A list view icon (<paramref name="revalidate"/>) is asked with the cover's tag, so the browser
    /// must check it on every use: "no-cache", and 304 when If-Modified-Since equals its Last-Modified. Not <paramref name="pinned"/> (a game image that is not the asked
    /// version's own under its own tag): "no-cache" as well.
    /// </summary>
    private async Task<IActionResult> ProcessedImage(ActionExecutingContext context, string path, ImageType type, bool revalidate = false, bool pinned = true, bool blurHash = true)
    {
        try
        {
            return await ProcessedImageCore(context, path, type, revalidate, pinned, blurHash).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // A file listed a moment ago and deleted since (an own picture within the listing window): 404 like a missing image, no error entry with a stack trace.
            _logger.LogDebug("PlayniteBridge: the image \"{File}\" is gone", Path.GetFileName(path));
            return new NotFoundResult();
        }
    }

    private async Task<IActionResult> ProcessedImageCore(ActionExecutingContext context, string path, ImageType type, bool revalidate, bool pinned, bool blurHash)
    {
        var request = context.HttpContext.Request;
        var options = ImageCache.ProcessingOptions(
            _imageProcessor,
            path,
            type,
            Arg<int?>(context, "width"),
            Arg<int?>(context, "height"),
            Arg<int?>(context, "maxWidth"),
            Arg<int?>(context, "maxHeight"),
            Arg<int?>(context, "fillWidth"),
            Arg<int?>(context, "fillHeight"),
            Arg<int?>(context, "quality"),
            Arg<ImageFormat?>(context, "format"),
            request.Headers.GetCommaSeparatedValues(HeaderNames.Accept),
            request.Query[HeaderNames.Accept],
            request.Headers.UserAgent.ToString());
        var (resultPath, mimeType, modified) = await _imageProcessor.ProcessImage(options).ConfigureAwait(false);

        // The BlurHash of every image type served here is computed in the background (BlurHashCache), never in this request, unless it is stored (BlurHashStore). An icon has none.
        if (blurHash && !revalidate && !BlurHashStore.Shared(_logger).TryGet(path, out _) && ImageCache.Tag(path) is string tag)
        {
            _blurHashes.Get(path, tag);
        }

        // Response headers as Jellyfin's ImageController.GetImageResult, without its DLNA headers and (except for an icon) its 304 answer.
        var headers = context.HttpContext.Response.Headers;
        headers.Vary = HeaderNames.Accept;
        headers.CacheControl = revalidate || !pinned ? "public, no-cache"
            : string.IsNullOrEmpty(Arg<string>(context, "tag")) ? "public" : "public, max-age=" + TimeSpan.FromDays(365).TotalSeconds;
        var lastModified = modified.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss \"GMT\"", CultureInfo.InvariantCulture);
        headers.LastModified = lastModified;
        if (revalidate && request.Headers.IfModifiedSince.ToString() == lastModified)
        {
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        // The bytes are read once and sent with the length of what was read (StableFile): a file rewritten or deleted meanwhile (the processor's cache, an own picture)
        // can then neither make the length header wrong (500 "too many bytes written") nor throw while the response is sent.
        return await StableFile.Result(resultPath, mimeType ?? MediaTypeNames.Text.Plain).ConfigureAwait(false);
    }

    /// <summary>
    /// A still frame of a trailer or special feature as Jellyfin's VideoImageProvider makes it (at 10 % of the runtime, 10 seconds when unknown) with Jellyfin's ffprobe
    /// and ffmpeg; kept under &lt;cache&gt;\playnite-bridge\extras\, never in the game folder. Null when it cannot be made (one warning per file and video state).
    /// </summary>
    private Task<string?> StillFrame(GameExtra extra) => StillFrame(extra, _paths.CachePath, _mediaInfo, _mediaEncoder, _logger);

    /// <summary>
    /// The still frame of <paramref name="extra"/> below <paramref name="cachePath"/>, made when missing. Shared with Sync Artwork, which makes the missing ones
    /// before it computes the BlurHashes, as Jellyfin's library scan extracts them.
    /// </summary>
    public static async Task<string?> StillFrame(GameExtra extra, string cachePath, MediaInfoCache mediaInfo, IMediaEncoder mediaEncoder, ILogger logger)
    {
        var tag = ImageCache.Tag(extra.Path);
        if (tag is null || (FailedFrames.TryGetValue(extra.Id, out var failed) && failed == tag))
        {
            return null;
        }

        var target = StillFramePath(cachePath, extra, tag);
        if (File.Exists(target))
        {
            return target;
        }

        var gate = FrameLocks.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        string? extracted = null;
        try
        {
            if (File.Exists(target))
            {
                return target;
            }

            // The probe is shared with the extra's runtime (MediaInfoCache), so each file is probed once; a stored answer (mediainfo.json) needs none.
            var info = await mediaInfo.Get(extra.Path, tag).ConfigureAwait(false) ?? throw new InvalidOperationException("ffprobe failed");
            var stream = info.MediaStreams?.FirstOrDefault(s => s.Type == MediaStreamType.Video)
                ?? throw new InvalidOperationException("the file has no video stream");
            var offset = info.RunTimeTicks > 0 ? TimeSpan.FromTicks(info.RunTimeTicks.Value / 10) : TimeSpan.FromSeconds(10);
            extracted = await mediaEncoder.ExtractVideoImage(extra.Path, info.Container, ExtraSource(extra.Path), stream, null, offset, CancellationToken.None)
                .ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(extracted, target, true);
            FailedFrames.TryRemove(extra.Id, out _);
            return target;
        }
        catch (Exception ex)
        {
            if (!FailedFrames.TryGetValue(extra.Id, out var known) || known != tag)
            {
                logger.LogWarning("PlayniteBridge: cannot make a still frame of the local extra \"{File}\": {Message}", extra.RelativePath, ex.Message);
            }

            FailedFrames[extra.Id] = tag;
            if (extracted is not null)
            {
                try
                {
                    File.Delete(extracted);
                }
                catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
                {
                }
            }

            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Where the still frame of <paramref name="extra"/> in the video state <paramref name="tag"/> is kept: &lt;cache&gt;\playnite-bridge\extras\.</summary>
    private string StillFramePath(GameExtra extra, string tag) => StillFramePath(_paths.CachePath, extra, tag);

    /// <summary>The still frame path of <paramref name="extra"/> below the cache path <paramref name="cachePath"/> (also for Sync Artwork's BlurHashes).</summary>
    public static string StillFramePath(string cachePath, GameExtra extra, string tag) =>
        Path.Combine(cachePath, "playnite-bridge", "extras", extra.Id.ToString("N") + "-" + tag + ".jpg");

    /// <summary>The soundtrack of a version of a group (<see cref="ExtrasIndex.SoundtrackOf"/>); none with Enable Soundtrack off.</summary>
    internal IReadOnlyList<GameExtra> SoundtrackOfVersion(GameGroup group, string versionId) =>
        Plugin.Instance!.Configuration.EnableSoundtrack ? Extras(true).SoundtrackOf(group, versionId, Plugin.Instance.Configuration) : Array.Empty<GameExtra>();

    /// <summary>The tracks as Jellyfin's Audio items in list order (track number, then title); a file ffprobe cannot read as audio is left out (one warning).</summary>
    internal async Task<List<BaseItemDto>> SoundtrackDtos(IReadOnlyList<GameExtra> tracks)
    {
        var infos = await Task.WhenAll(tracks.Select(async t => (Track: t, Info: await TrackInfoOf(t).ConfigureAwait(false)))).ConfigureAwait(false);
        return Soundtrack.Order(infos.Where(t => t.Info is not null).Select(t => (t.Track, t.Info!))).Select(t => TrackDto(t.Track, t.Info)).ToList();
    }

    private async Task<BaseItemDto?> SoundtrackDto(GameExtra track) => await TrackInfoOf(track).ConfigureAwait(false) is { } info ? TrackDto(track, info) : null;

    private async Task<TrackInfo?> TrackInfoOf(GameExtra track) =>
        ImageCache.Tag(track.Path) is string tag ? Soundtrack.Info(track, await _trackInfo.Get(track.Path, tag).ConfigureAwait(false)) : null;

    // An Audio item without album: Album and AlbumId stay empty (the web would treat the track as a music album track), artists only as names (no artist link).
    // The Primary tag is the file's own when it carries a picture, else the folder cover's.
    private BaseItemDto TrackDto(GameExtra track, TrackInfo info)
    {
        var tag = ImageCache.Tag(info.EmbeddedImage is null ? track.ImagePath : track.Path);
        var source = ExtraMediaSource(track, new List<MediaStream>());
        source.RunTimeTicks = info.RunTimeTicks;
        return new BaseItemDto
        {
            Id = track.Id,
            Name = info.Name,
            SortName = GameExtras.SortName(info.Name, _config.Configuration),
            ServerId = _host.SystemId,
            Type = BaseItemKind.Audio,
            MediaType = MediaType.Audio,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            IndexNumber = info.Number,
            Artists = info.Artists.ToArray(),
            RunTimeTicks = info.RunTimeTicks,
            Container = track.Container,
            MediaSources = new[] { source },
            ImageTags = tag is null ? new Dictionary<ImageType, string>() : new Dictionary<ImageType, string> { [ImageType.Primary] = tag },
            BackdropImageTags = Array.Empty<string>(),
            PrimaryImageAspectRatio = tag is null ? null : 1.0,
            UserData = new UserItemDataDto { Key = track.Id.ToString("N"), ItemId = track.Id, Played = false }
        };
    }

    /// <summary>
    /// The picture of a track: the one embedded in the file, extracted once by Jellyfin's ffmpeg into &lt;cache&gt;\playnite-bridge\soundtrack\ (the temp file is moved),
    /// else the folder's cover, else null. A failed extraction is warned once per file state and falls back to the cover.
    /// </summary>
    private async Task<string?> SoundtrackArt(GameExtra track)
    {
        var tag = ImageCache.Tag(track.Path);
        var info = tag is null || (FailedArt.TryGetValue(track.Id, out var failed) && failed == tag) ? null : await TrackInfoOf(track).ConfigureAwait(false);
        if (info?.EmbeddedImage is not int stream)
        {
            return track.ImagePath;
        }

        var folder = Path.Combine(_paths.CachePath, "playnite-bridge", "soundtrack");
        var name = track.Id.ToString("N") + "-" + tag;
        string? Cached() => Directory.Exists(folder) ? Directory.EnumerateFiles(folder, name + ".*").FirstOrDefault() : null;
        if (Cached() is string done)
        {
            return done;
        }

        var gate = ArtLocks.GetOrAdd(name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        string? extracted = null;
        try
        {
            if (Cached() is string again)
            {
                return again;
            }

            extracted = await _mediaEncoder.ExtractAudioImage(track.Path, stream, CancellationToken.None).ConfigureAwait(false);
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, name + Path.GetExtension(extracted));
            File.Move(extracted, target, true);
            return target;
        }
        catch (Exception ex)
        {
            FailedArt[track.Id] = tag!;
            _logger.LogWarning("PlayniteBridge: cannot extract the picture of the soundtrack file \"{File}\": {Message}", track.RelativePath, ex.Message);
            if (extracted is not null)
            {
                try
                {
                    File.Delete(extracted);
                }
                catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
                {
                }
            }

            return track.ImagePath;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>A local extra's video as the media source Jellyfin's ffprobe and ffmpeg are given.</summary>
    private static MediaSourceInfo ExtraSource(string path) => new() { Path = path, Protocol = MediaProtocol.File, VideoType = VideoType.VideoFile };

    /// <summary>A game has no intros: answer empty instead of Jellyfin's 404.</summary>
    private IActionResult? EmptyForGame(ActionExecutingContext context, Func<object> empty) =>
        FindGame(context, Arg<Guid>(context, "itemId")) is null ? null : new OkObjectResult(empty());

    /// <summary>
    /// For a game id (any version): the local extras of its group from its game folders; empty when there are none or the games library is not visible to the user;
    /// null (Jellyfin answers) for any other id and for a game blocked by parental controls.
    /// </summary>
    /// <summary>
    /// The theme media of a virtual collection: a valid empty result (OwnerId = the id) instead of Jellyfin's 404 for an unknown id, which the web client logs as a failed
    /// theme-media load. Null for any other id (a game has its own answer, a real item is Jellyfin's).
    /// </summary>
    private IActionResult? EmptyThemeMedia(ActionExecutingContext context, bool all)
    {
        var id = Arg<Guid>(context, "itemId");
        if (Find(context, id, out _) is not { Kind: GameHitKind.Collection })
        {
            return null;
        }

        return new OkObjectResult(EmptyThemeMediaResult(id, all));
    }

    /// <summary>The empty answer: one empty ThemeMediaResult, or the three of AllThemeMediaResult, each with the owner id.</summary>
    public static object EmptyThemeMediaResult(Guid id, bool all)
    {
        ThemeMediaResult Empty() => new() { OwnerId = id, Items = Array.Empty<BaseItemDto>(), TotalRecordCount = 0 };
        return all ? new AllThemeMediaResult { ThemeVideosResult = Empty(), ThemeSongsResult = Empty(), SoundtrackSongsResult = Empty() } : Empty();
    }

    private async Task<IActionResult?> ExtrasForGame(ActionExecutingContext context, Func<IReadOnlyList<GameExtra>, Task<object>> answer)
    {
        if (Find(context, Arg<Guid>(context, "itemId"), out var data) is not { Kind: GameHitKind.Game } hit)
        {
            return null;
        }

        return new OkObjectResult(await answer(GamesLibrary(context) is null ? Array.Empty<GameExtra>() : ExtrasOfPage(data, hit)).ConfigureAwait(false));
    }

    /// <summary>
    /// The special features of a game id (any version): the local ones (<see cref="ExtrasForGame"/>) and, while the plan merges them, the Steam
    /// Special Features of the page (<see cref="SteamSpecials"/>) in the order of <see cref="VideoRows.NativeCards"/>. Null for any other id.
    /// </summary>
    private async Task<IActionResult?> SpecialFeaturesResult(ActionExecutingContext context)
    {
        if (Find(context, Arg<Guid>(context, "itemId"), out var data) is not { Kind: GameHitKind.Game } hit)
        {
            return null;
        }

        var visible = GamesLibrary(context) is not null;
        var config = Plugin.Instance!.Configuration;
        var page = visible ? ExtrasOfPage(data, hit) : Array.Empty<GameExtra>();
        var steamAll = visible ? SteamSpecials(data.DetailGroupOf(hit.Game!.Id) ?? hit.Group!, hit.Game.Id, config) : Array.Empty<SteamVideoItem>();
        var seed = VideoRows.Seed(VisibleGamesAccess(_libraryManager, _userManager, context.HttpContext.User, Arg<Guid?>(context, "userId"))?.User?.Id, hit.Game!.Id, DateTime.UtcNow);
        var (native, steam) = NativeRow(page, steamAll, config, seed);
        var local = await ExtraDtos(native, ExtraKind.Special, CanDownload(context)).ConfigureAwait(false);
        if (steam.Count == 0)
        {
            return new OkObjectResult(NativeShape(local, config));
        }

        await SteamVideos.EnsureFormats(steam, DateTime.UtcNow, context.HttpContext.RequestAborted).ConfigureAwait(false);
        var dtos = local.Concat(steam.Select(SteamDto)).GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First());
        var cards = VideoRows.NativeCards(config, native, steam.Select(s => s.Id).ToList());
        return new OkObjectResult(NativeShape(cards.Where(c => dtos.ContainsKey(c.Id)).Select(c => dtos[c.Id]).ToList(), config));
    }

    /// <summary>
    /// The content of Jellyfin's Special Features row of a game page: <paramref name="page"/> = the shown extras, <paramref name="steamAll"/> = the Steam clips of the page.
    /// It holds the special features of the rows the plan (<see cref="VideoRows.Plan"/>) merges, in the order of <see cref="VideoRows.OrderNative"/>, and the Steam clips when
    /// the plan merges them. Everything else is a row of its own (<see cref="OwnRows"/>).
    /// </summary>
    public static (IReadOnlyList<GameExtra> Native, IReadOnlyList<SteamVideoItem> Steam) NativeRow(IReadOnlyList<GameExtra> page, IReadOnlyList<SteamVideoItem> steamAll, Configuration.PluginConfiguration config, int? seed = null)
    {
        var specials = page.Where(e => e.Kind == ExtraKind.Special).ToList();
        var plan = VideoRows.Plan(config, specials, steamAll.Count > 0);
        var merged = specials.Where(e => plan.NativeKeys.Contains(VideoRows.RowKey(e))).ToList();
        return (VideoRows.OrderNative(config, merged, seed), plan.SteamMerged ? steamAll : Array.Empty<SteamVideoItem>());
    }

    /// <summary>The rows of their own on a game page: the special features of the rows the plan does not merge, by row in table order, each sorted by VideosSort. SteamOwn: the Steam clips are a row of their own.</summary>
    public static (List<(string Key, IReadOnlyList<GameExtra> Files)> Rows, bool SteamOwn) OwnRows(IReadOnlyList<GameExtra> page, IReadOnlyList<SteamVideoItem> steamAll, Configuration.PluginConfiguration config, int? seed = null)
    {
        var specials = page.Where(e => e.Kind == ExtraKind.Special).ToList();
        var plan = VideoRows.Plan(config, specials, steamAll.Count > 0);
        var rows = specials.Where(e => !plan.NativeKeys.Contains(VideoRows.RowKey(e))).GroupBy(VideoRows.RowKey).OrderBy(g => VideoRows.TableIndex(config, g.Key))
            .Select(g => (g.Key, VideoRows.OrderRow(config, g, g.Key, seed))).ToList();
        return (rows, steamAll.Count > 0 && !plan.SteamMerged);
    }

    /// <summary>
    /// Tile Shape for Jellyfin's merged Special Features row: Jellyfin Web picks the shape of a row's cards from the items' PrimaryImageAspectRatio (the median),
    /// so the answer says 2:3 (Portrait), 1:1 (Square) or 16:9 (Landscape) for every item. Auto leaves the ratios as they are.
    /// </summary>
    public static List<BaseItemDto> NativeShape(List<BaseItemDto> items, Configuration.PluginConfiguration config)
    {
        if (config.VideosTileShape != Configuration.TileShape.Auto)
        {
            var ratio = config.VideosTileShape == Configuration.TileShape.Portrait ? 2.0 / 3.0 : config.VideosTileShape == Configuration.TileShape.Square ? 1.0 : 16.0 / 9.0;
            foreach (var item in items)
            {
                item.PrimaryImageAspectRatio = ratio;
            }
        }

        return items;
    }

    /// <summary>The table position the merged row stands at: that of its first row (<see cref="VideoRows.TableIndex"/>, Steam included while its clips are merged); 99 when there is no merged row.</summary>
    public static int NativeAt(IReadOnlyList<GameExtra> page, IReadOnlyList<SteamVideoItem> steamAll, Configuration.PluginConfiguration config)
    {
        var plan = VideoRows.Plan(config, page.Where(e => e.Kind == ExtraKind.Special), steamAll.Count > 0);
        var at = plan.NativeKeys.Select(k => VideoRows.TableIndex(config, k)).ToList();
        if (plan.SteamMerged)
        {
            at.Add(VideoRows.TableIndex(config, VideoRows.SteamKey));
        }

        return at.DefaultIfEmpty(99).Min();
    }

    /// <summary>
    /// The Steam Special Features of the game page of <paramref name="currentId"/> in its <paramref name="group"/>: none unless Enable Videos and Enable Steam Special Features are on;
    /// the clips of the shown version's AppID, then, with Special Features of Other Versions on All Versions, those of the other versions' AppIDs in main order; a clip once.
    /// Whether they stand in Jellyfin's Special Features row or in a row of their own is the plan's decision (<see cref="NativeRow"/>).
    /// The group is the one the asking user may see, so a clip of a game the user may not see is never listed.
    /// </summary>
    public static IReadOnlyList<SteamVideoItem> SteamSpecials(GameGroup group, string currentId, Configuration.PluginConfiguration config)
    {
        if (!config.EnableVideos || !config.EnableSteamSpecialFeatures)
        {
            return Array.Empty<SteamVideoItem>();
        }

        return SteamAppIds(group, currentId, config).SelectMany(SteamVideos.Items).GroupBy(i => i.Id).Select(g => g.First()).ToList();
    }

    /// <summary>The AppIDs whose clips the page of <paramref name="currentId"/> shows: its own, then (unless Special Features of Other Versions is This Version Only) those of the other versions in main order.</summary>
    public static IReadOnlyList<string> SteamAppIds(GameGroup group, string currentId, Configuration.PluginConfiguration config)
    {
        var (shown, ordered) = ExtrasIndex.Shown(group, currentId);
        return ordered.Where(v => v == shown || config.ExtrasOfOtherVersions != Configuration.OtherVersionsMode.ThisVersionOnly)
            .Select(SteamVideos.AppIdOf).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).Distinct().ToList();
    }

    /// <summary>
    /// The extras of the game page of a hit: the version-aware list (<see cref="ExtrasIndex.ForVersion"/> in the detail page's group, with the trailer and extras settings),
    /// without the theme songs while Enable Theme Music is on (the plugin's own player plays them, so Jellyfin's theme song setting must not play them a second time).
    /// </summary>
    private IReadOnlyList<GameExtra> ExtrasOfPage(GameData data, GameHit hit)
    {
        var config = Plugin.Instance!.Configuration;
        var list = Enabled(Extras(true).ForVersion(data.DetailGroupOf(hit.Game!.Id) ?? hit.Group!, hit.Game.Id, config));
        return list.Where(e => !(config.EnableThemeMusic && e.Kind == ExtraKind.ThemeSong) && !(config.EnableThemeVideos && e.Kind == ExtraKind.ThemeVideo)).ToList();
    }

    /// <summary>
    /// The answer of games/{id}/videorows for the game page of <paramref name="game"/> in <paramref name="group"/>: { native: [{ id, run, label }], steamOwn, shape, rows: [{ key, label, title, count, items }] }.
    /// native = every card of Jellyfin's merged Special Features row in its order with the row it came from (run = "steam" or the row key) and that row's label, for the dividers;
    /// rows = the rows of their own in table order with their first <see cref="VideoRows.FirstTiles"/> items (<paramref name="row"/> and <paramref name="from"/> ask for that row's next ones);
    /// steamOwn = the Steam clips are a row of their own; shape = the tile shape of the rows (Tile Shape; Auto measures the first pictures).
    /// The list is the page's (<see cref="Enabled"/>) without the theme files, as in <see cref="ExtrasOfPage"/>.
    /// </summary>
    internal async Task<object> VideoRowsOf(GameGroup group, GameEntry game, bool canDownload, string? row, int from, Guid? userId)
    {
        var config = Plugin.Instance!.Configuration;
        var list = Enabled(Extras(true).ForVersion(group, game.Id, config))
            .Where(e => !(config.EnableThemeMusic && e.Kind == ExtraKind.ThemeSong) && !(config.EnableThemeVideos && e.Kind == ExtraKind.ThemeVideo)).ToList();
        var steamAll = SteamSpecials(group, game.Id, config);
        var seed = VideoRows.Seed(userId, game.Id, DateTime.UtcNow);
        var (native, steam) = NativeRow(list, steamAll, config, seed);
        var (own, steamOwn) = OwnRows(list, steamAll, config, seed);
        var nativeAt = NativeAt(list, steamAll, config);
        var slots = VideoRows.Slots(config);
        string Label(string key) => key.StartsWith("slot", StringComparison.Ordinal)
            ? slots.FirstOrDefault(s => "slot" + s.Index == key)?.Label ?? string.Empty
            : VideoRows.TypeTitle(config, Enum.Parse<VideoRowType>(key, true)) == "Own" ? VideoRows.TypeLabel(config, Enum.Parse<VideoRowType>(key, true)) : string.Empty;
        // The native row's cards in the order SpecialFeatures answers.
        var nativeCards = VideoRows.NativeCards(config, native, steam.Select(s => s.Id).ToList());
        var shown = own.Where(r => row is null || r.Key == row).ToList();
        var shape = RowShape(config.VideosTileShape, shown.SelectMany(r => r.Files).Take(VideoRows.FirstTiles).Where(e => e.ImagePath is not null).Select(e => (e.ImagePath!, ImageCache.Tag(e.ImagePath!) ?? string.Empty)));
        var rows = new List<object>();
        foreach (var (key, files) in shown)
        {
            var page = files.Skip(Math.Max(row is null ? 0 : from, 0)).Take(VideoRows.FirstTiles).ToList();
            var items = (await Task.WhenAll(page.Select(e => ExtraDto(e, canDownload))).ConfigureAwait(false)).ToList();
            rows.Add(new { key, at = VideoRows.TableIndex(config, key), before = VideoRows.TableIndex(config, key) < nativeAt, title = key.StartsWith("slot", StringComparison.Ordinal) ? "Own" : VideoRows.TypeTitle(config, Enum.Parse<VideoRowType>(key, true)), label = Label(key), count = files.Count, items });
        }

        return new
        {
            native = nativeCards.Select(c => new { id = c.Id.ToString("N"), run = c.Run, label = c.Run == VideoRows.SteamKey ? string.Empty : Label(c.Run) }).ToArray(),
            steamOwn,
            steamAt = VideoRows.TableIndex(config, VideoRows.SteamKey),
            nativeAt,
            shape,
            rows
        };
    }

    /// <summary>The trailer and special feature counts of the game page of <paramref name="game"/> in <paramref name="data"/> (the user's data): the version-aware list the page shows.</summary>
    public static (int Trailers, int Specials) ExtraCounts(ExtrasIndex index, GameData data, GameGroup group, GameEntry game, Configuration.PluginConfiguration config)
    {
        var extras = Enabled(index.ForVersion(data.DetailGroupOf(game.Id) ?? group, game.Id, config));
        return (extras.Count(e => e.Kind == ExtraKind.Trailer), extras.Count(e => e.Kind == ExtraKind.Special));
    }

    /// <summary>The theme songs of the game page of <paramref name="id"/> for the plugin's own player (Enable Theme Music): null for an id that is no game of the user's data.</summary>
    internal IReadOnlyList<GameExtra>? ThemeSongsOfPage(GameData data, Guid id, string? playingName, bool video = false) =>
        ThemeSongsOf(Extras(true), data, id, Plugin.Instance!.Configuration, playingName, video);

    /// <summary><see cref="ThemeSongsOfPage"/> over an index and the settings; <paramref name="data"/> must be the data the asking user may see, so a version they may not see gives no song.</summary>
    public static IReadOnlyList<GameExtra>? ThemeSongsOf(ExtrasIndex index, GameData data, Guid id, Configuration.PluginConfiguration config, string? playingName, bool video)
    {
        if (!(video ? config.EnableThemeVideos : config.EnableThemeMusic) || data.Find(id) is not { Kind: GameHitKind.Game } hit)
        {
            return null;
        }

        var group = data.DetailGroupOf(hit.Game!.Id) ?? hit.Group!;
        if (video)
        {
            return ThemeVideosOf(index, group, hit.Game.Id, config, playingName);
        }

        var songs = index.ThemeTracks(group, hit.Game.Id, config.ThemeSource, playingName, ExtraKind.ThemeSong);
        // Theme music only: a game without a theme song of its own plays its soundtrack (Soundtrack As Theme).
        return songs.Count == 0 && config.EnableSoundtrack && config.SoundtrackAsTheme ? index.SoundtrackOf(group, hit.Game.Id, config) : songs;
    }

    /// <summary>The kinds of the theme video sources by their keys; the keys in this order are the default order.</summary>
    public static readonly IReadOnlyList<(string Key, ExtraKind Kind)> ThemeVideoSourceKinds = new[]
    {
        ("themevideos", ExtraKind.ThemeVideo),
        ("trailers", ExtraKind.Trailer),
        ("specials", ExtraKind.Special)
    };

    /// <summary>The sources that are on in the user's order: the listed keys first (each once, unknown ones ignored), then the others in the default order; only the ticked ones.</summary>
    public static IReadOnlyList<(string Key, ExtraKind Kind)> ThemeVideoSourcesOn(Configuration.PluginConfiguration config)
    {
        static List<string> Keys(string? text) => (text ?? string.Empty).Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList();
        var order = Keys(config.ThemeVideoSourceOrder).Where(k => ThemeVideoSourceKinds.Any(s => s.Key == k)).ToList();
        order.AddRange(ThemeVideoSourceKinds.Select(s => s.Key).Where(k => !order.Contains(k)));
        var on = Keys(config.ThemeVideoSources);
        return order.Where(on.Contains).Select(k => ThemeVideoSourceKinds.First(s => s.Key == k)).ToList();
    }

    /// <summary>
    /// The theme videos of a game page: each source that is on gives its videos (Version Fallback decides the version, as for the folder backdrops). Only the first source
    /// that has any is used, or with several sources all together, one after the other in the user's order.
    /// </summary>
    public static IReadOnlyList<GameExtra> ThemeVideosOf(ExtrasIndex index, GameGroup group, string currentId, Configuration.PluginConfiguration config, string? playingName)
    {
        var all = new List<GameExtra>();
        foreach (var (_, kind) in ThemeVideoSourcesOn(config))
        {
            var videos = index.ThemeTracks(group, currentId, config.ThemeVideoSource, playingName, kind);
            if (videos.Count > 0 && config.ThemeVideoSeveralSources == Configuration.ThemeSourcesMode.FirstAvailable)
            {
                return videos;
            }

            all.AddRange(videos);
        }

        return all;
    }

    /// <summary>
    /// The extras the settings let a game page show: none with Enable Videos off; trailers only with Enable Trailers; special features only with Enable Extras;
    /// the theme video and the theme song with Enable Videos.
    /// </summary>
    internal static IReadOnlyList<GameExtra> Enabled(IReadOnlyList<GameExtra> extras)
    {
        var config = Plugin.Instance?.Configuration;
        return config is null ? extras : !config.EnableVideos ? Array.Empty<GameExtra>()
            : extras.Where(e => e.Kind != ExtraKind.Trailer || config.EnableTrailers).Where(e => e.Kind != ExtraKind.Special || (config.EnableExtras && VideoRows.Shown(config, e))).ToList();
    }

    private async Task<ThemeMediaResult> ThemeResult(ActionExecutingContext context, IReadOnlyList<GameExtra> extras, ExtraKind kind)
    {
        var items = await ExtraDtos(extras, kind, CanDownload(context)).ConfigureAwait(false);
        return new ThemeMediaResult { OwnerId = Arg<Guid>(context, "itemId"), Items = items, TotalRecordCount = items.Count };
    }

    // The extras are probed side by side (only the first time per file), in their order.
    private async Task<List<BaseItemDto>> ExtraDtos(IReadOnlyList<GameExtra> extras, ExtraKind kind, bool canDownload) =>
        (await Task.WhenAll(extras.Where(e => e.Kind == kind).Select(e => ExtraDto(e, canDownload))).ConfigureAwait(false)).ToList();

    /// <summary>
    /// A local extra as Jellyfin's item: LocationType FileSystem (Jellyfin Web plays no Virtual item), never a server path. A video extra carries the tags of its images
    /// (<see cref="ExtraImages"/>: Primary or the still frame of a trailer or special feature, Logo, Art, Disc, Banner, Thumb and the backdrops in order) and the BlurHash of each once
    /// stored or computed; PrimaryImageAspectRatio of the Primary file (16:9 for a still frame); DateCreated = the video's creation time, the current time when it has none (as Jellyfin's
    /// ResolverHelper.SetDateCreated); SortName as BaseItem.CreateSortName (GameExtras.SortName, the server's sort settings read anew). Trailers and special features get the runtime,
    /// the media streams and one direct media source from Jellyfin's ffprobe (stored in mediainfo.json; none of them when the probe fails). A video extra with a trickplay folder next to it
    /// gets Jellyfin's Trickplay map { extra id "N": { width: TrickplayInfo } }, none without a valid layout or while ShowTrickplay is off.
    /// </summary>
    private async Task<BaseItemDto> ExtraDto(GameExtra extra, bool canDownload)
    {
        // Image files for every video extra (none are found for theme songs); a still frame only for trailers and special features.
        var stillFrame = extra.Kind is ExtraKind.Trailer or ExtraKind.Special;
        var info = stillFrame && ImageCache.Tag(extra.Path) is string videoTag ? await _mediaInfo.Get(extra.Path, videoTag).ConfigureAwait(false) : null;
        var images = ExtraImages(extra, _paths.CachePath);
        var tag = images.FirstOrDefault(i => i.Type == ImageType.Primary).Tag;
        var created = File.GetCreationTimeUtc(extra.Path);
        var dto = new BaseItemDto
        {
            ImageTags = new Dictionary<ImageType, string>(),
            BackdropImageTags = Array.Empty<string>(),
            // Jellyfin Web picks the card shape from this ratio (shape 'autooverflow'); without it the card is square.
            PrimaryImageAspectRatio = extra.ImagePath is not null && tag is not null ? _ratios.Get(extra.ImagePath, tag)
                : stillFrame ? ImageRatioCache.Default : null,
            Id = extra.Id,
            Name = extra.Name,
            SortName = GameExtras.SortName(extra.Name, _config.Configuration),
            DateCreated = created == DateTime.MinValue ? DateTime.UtcNow : created,
            Overview = extra.Overview,
            ServerId = _host.SystemId,
            Type = extra.Kind switch
            {
                ExtraKind.ThemeSong => BaseItemKind.Audio,
                ExtraKind.Trailer => BaseItemKind.Trailer,
                _ => BaseItemKind.Video
            },
            ExtraType = extra.ExtraType,
            MediaType = extra.Kind == ExtraKind.ThemeSong ? MediaType.Audio : MediaType.Video,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            Container = extra.Container,
            RunTimeTicks = info?.RunTimeTicks > 0 ? info.RunTimeTicks : null,
            UserData = new UserItemDataDto { Key = extra.Id.ToString("N"), ItemId = extra.Id, Played = false }
        };
        SetImages(dto, images);
        if (info is not null)
        {
            // As Jellyfin's DtoService fills MediaStreams from the item's own media source.
            dto.MediaStreams = (info.MediaStreams ?? Array.Empty<MediaStream>()).ToArray();
            var source = ExtraMediaSource(extra, dto.MediaStreams);
            source.RunTimeTicks = dto.RunTimeTicks;
            source.Bitrate = info.Bitrate;
            dto.MediaSources = new[] { source };
        }

        // The server's interval is read anew for every item; theme videos have no runtime (no probe), so their count is the sheets' capacity.
        var interval = _config.Configuration.TrickplayOptions.Interval;
        var layouts = ((Plugin.Instance!.Configuration.ShowTrickplay ? extra.Trickplay : null) ?? Array.Empty<TrickplaySet>())
            .Select(s => GameExtras.TrickplayValuesFor(s, SheetHeight(extra, s.Sheets[0]), interval, dto.RunTimeTicks / TimeSpan.TicksPerMillisecond))
            .OfType<TrickplayValues>()
            .ToDictionary(v => v.Width, v => new TrickplayInfo
            {
                Width = v.Width,
                Height = v.Height,
                TileWidth = v.TileWidth,
                TileHeight = v.TileHeight,
                ThumbnailCount = v.ThumbnailCount,
                Interval = v.Interval,
                Bandwidth = v.Bandwidth
            });
        if (layouts.Count > 0)
        {
#if NET10_0_OR_GREATER
            // Jellyfin 12.x: the DTO carries TrickplayInfoDto, made from the same TrickplayInfo values.
            dto.Trickplay = new Dictionary<string, Dictionary<int, TrickplayInfoDto>>
            {
                [extra.Id.ToString("N")] = layouts.ToDictionary(l => l.Key, l => new TrickplayInfoDto(l.Value))
            };
#else
            dto.Trickplay = new Dictionary<string, Dictionary<int, TrickplayInfo>> { [extra.Id.ToString("N")] = layouts };
#endif
        }

        return ExtraFileFields(dto, extra, canDownload);
    }

    /// <summary>
    /// The file name of a local extra, the only part of its path the server shows: its Path and its source's Path (so a client sees the extension) without any directory.
    /// </summary>
    public static string ExtraFileName(GameExtra extra) => System.IO.Path.GetFileName(extra.Path);

    /// <summary>The download name as Jellyfin gives it for a film (LibraryController.GetDownload): the file name with quotes removed.</summary>
    public static string ExtraDownloadName(GameExtra extra) => ExtraFileName(extra).Replace("\"", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="dto"/> of a local extra with Path = <see cref="ExtraFileName"/> (also on its sources) and CanDownload = <paramref name="canDownload"/>
    /// (Video.CanDownload and Audio.CanDownload are true for a file, then the user's EnableContentDownloading decides, BaseItem.CanDownload(user)).
    /// </summary>
    public static BaseItemDto ExtraFileFields(BaseItemDto dto, GameExtra extra, bool canDownload)
    {
        dto.Path = ExtraFileName(extra);
        dto.CanDownload = canDownload;
        foreach (var source in dto.MediaSources ?? Array.Empty<MediaSourceInfo>())
        {
            source.Path = dto.Path;
        }

        return dto;
    }

    /// <summary>
    /// Whether the request's user may download as Jellyfin decides (BaseItem.CanDownload(user): the EnableContentDownloading permission; an API key without a user may).
    /// False when the games library is not visible to the user.
    /// </summary>
    /// <summary>True for an unlimited user (none) and a user with the download permission.</summary>
    internal static bool MayDownload(User? user) => user is null || user.HasPermission(PermissionKind.EnableContentDownloading);

    private bool CanDownload(ActionExecutingContext context) =>
        VisibleGamesAccess(_libraryManager, _userManager, context.HttpContext.User, Arg<Guid?>(context, "userId")) is { } access
        && (access.User is null || access.User.HasPermission(PermissionKind.EnableContentDownloading));

    /// <summary>
    /// Items/{id}/Download for a local extra as Jellyfin's LibraryController.GetDownload answers a file: the file with range processing, its media type by extension and
    /// Content-Disposition attachment with the original file name (<see cref="ExtraFileName"/>). 403 without the permission (Jellyfin's Download policy answers that before the filter),
    /// 404 for a vanished file; null for an id that is no extra the user may see (Jellyfin answers: unknown id = 404). The path is the scan's, nothing from the request becomes a path.
    /// </summary>
    public static IActionResult? ExtraDownload(GameExtra? extra, bool allowed)
    {
        if (extra is null)
        {
            return null;
        }

        if (!allowed)
        {
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        return File.Exists(extra.Path)
            ? new PhysicalFileResult(extra.Path, MimeTypes.GetMimeType(extra.Path) ?? MediaTypeNames.Application.Octet)
            {
                EnableRangeProcessing = true,
                FileDownloadName = ExtraDownloadName(extra)
            }
            : new NotFoundResult();
    }

    /// <summary>
    /// The height of a trickplay sheet by Jellyfin's image processor, kept per file and state; null when it cannot be read (one warning per file and state).
    /// </summary>
    private int? SheetHeight(GameExtra extra, string sheet)
    {
        var tag = ImageCache.Tag(sheet);
        if (tag is null)
        {
            return null;
        }

        if (_sheetHeights.TryGetValue(sheet, out var known) && known.Tag == tag)
        {
            return known.Height;
        }

        int? height;
        try
        {
            height = _imageProcessor.GetImageDimensions(sheet).Height;
        }
        catch (Exception ex)
        {
            height = null;
            _logger.LogWarning("PlayniteBridge: cannot read the trickplay sheet \"{File}\" of the local extra \"{Extra}\": {Message}",
                Path.GetFileName(Path.GetDirectoryName(sheet)) + "/" + Path.GetFileName(sheet), extra.RelativePath, ex.Message);
        }

        _sheetHeights[sheet] = (tag, height);
        return height;
    }

    /// <summary>A local extra: one direct source (no transcoding), no media streams, no path. A Steam Special Feature: its HLS source (<see cref="SteamMediaSource"/>).</summary>
    private IActionResult? PlaybackInfoResult(ActionExecutingContext context)
    {
        var source = FindExtra(context) is GameExtra extra ? ExtraMediaSource(extra, new List<MediaStream>())
            : FindSteam(context) is SteamVideoItem steam ? SteamMediaSource(steam, SteamVideos.CachedFormat(steam.Video.Hls)) : null;
        return source is null ? null : new OkObjectResult(new PlaybackInfoResponse
        {
            MediaSources = new[] { source },
            PlaySessionId = Guid.NewGuid().ToString("N")
        });
    }

    /// <summary>
    /// A Steam Special Feature as Jellyfin Web plays a remote stream itself: Protocol Http with Steam's HLS address as Path and nothing but direct play, so the web player
    /// takes it without asking the server and plays the Path as it is. Container "hls" makes the video player use hls.js (always with MSE while RunTimeTicks is unknown).
    /// IsRemote: no crossorigin attribute. No media streams and no runtime: nothing is probed; the player takes the duration from the stream.
    /// </summary>
    public static MediaSourceInfo SteamMediaSource(SteamVideoItem item, SteamFormat? format = null) => new()
    {
        Id = item.Id.ToString("N"),
        Name = item.Video.Name,
        Path = item.Video.Hls,
        Protocol = MediaProtocol.Http,
        Type = MediaSourceType.Default,
        Container = "hls",
        IsRemote = true,
        VideoType = VideoType.VideoFile,
        SupportsDirectPlay = true,
        SupportsDirectStream = false,
        SupportsTranscoding = false,
        MediaStreams = SteamStreams(format)
    };

    /// <summary>
    /// The one video stream of a Steam clip whose frame rate Steam's manifest names (<see cref="SteamVideos.ParseManifest"/>), so that addons reading the frame rate of the
    /// playing item find it. Kept minimal (Type, Index, frame rate, size when known; no codec): the source stays a direct-play HLS address. No format, no stream.
    /// </summary>
    public static List<MediaStream> SteamStreams(SteamFormat? format) => format is null
        ? new List<MediaStream>()
        : new List<MediaStream>
        {
            new()
            {
                Type = MediaStreamType.Video,
                Index = 0,
                RealFrameRate = (float)format.FrameRate,
                AverageFrameRate = (float)format.FrameRate,
                Width = format.Width > 0 ? format.Width : null,
                Height = format.Height > 0 ? format.Height : null
            }
        };

    /// <summary>
    /// A Steam Special Feature as Jellyfin's item, shaped as a local special feature (<see cref="ExtraDto"/>: Video, LocationType FileSystem, no ParentId) with ExtraType Unknown
    /// (Jellyfin writes no label itself; the web script writes it, <see cref="SteamVideoLabels"/>, whose key rides in Etag), its thumbnail as Primary (16:9, tag <see cref="SteamVideos.ThumbnailTag"/>),
    /// no runtime and its one media source.
    /// </summary>
    public static BaseItemDto SteamItemDto(SteamVideoItem item, string serverId, MediaBrowser.Model.Configuration.ServerConfiguration config, SteamFormat? format = null, bool thumbnail = true, string? primaryTag = null)
    {
        var source = SteamMediaSource(item, format);
        return new BaseItemDto
        {
            ImageTags = thumbnail ? new Dictionary<ImageType, string> { [ImageType.Primary] = primaryTag ?? SteamVideos.ThumbnailTag(item.Video) } : new Dictionary<ImageType, string>(),
            BackdropImageTags = Array.Empty<string>(),
            PrimaryImageAspectRatio = ImageRatioCache.Default,
            Id = item.Id,
            Name = item.Video.Name,
            SortName = GameExtras.SortName(item.Video.Name, config),
            ServerId = serverId,
            Type = BaseItemKind.Video,
            ExtraType = MediaBrowser.Model.Entities.ExtraType.Unknown,
            Etag = SteamVideoLabels.Label(item.Video.Name),
            MediaType = MediaType.Video,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            Container = source.Container,
            MediaStreams = source.MediaStreams.ToArray(),
            MediaSources = new[] { source },
            UserData = new UserItemDataDto { Key = item.Id.ToString("N"), ItemId = item.Id, Played = false }
        };
    }

    // The item with the BlurHash of its picture once that file exists (computed in the background). It always announces a Primary image, because the image route never answers 404 for it
    // (SteamVideos.Picture ends in a neutral tile) and the first display of the tile is what starts a still frame. The tag is that of the Steam thumbnail while that file is there, of the still frame
    // once that exists, else of the thumbnail's address (what the route answers meanwhile is not pinned).
    private BaseItemDto SteamDto(SteamVideoItem item)
    {
        var thumbnail = SteamVideos.ThumbnailPath(_paths.CachePath, item);
        var frame = SteamVideos.FramePath(_paths.CachePath, item);
        var (tag, file) = File.Exists(thumbnail) ? (SteamVideos.ThumbnailTag(item.Video), thumbnail)
            : File.Exists(frame) ? (SteamVideos.FrameTag(item.Video), frame)
            : (SteamVideos.ThumbnailTag(item.Video), (string?)null);
        var dto = SteamItemDto(item, _host.SystemId, _config.Configuration, SteamVideos.CachedFormat(item.Video.Hls), true, tag);
        if (file is not null)
        {
            AddBlurHash(dto, ImageType.Primary, tag, file);
        }

        return dto;
    }

    // The backdrops of the game that owns the Steam item's AppID (its own Steam game, else the first game that links it), as files of the game data the request is answered from.
    private IReadOnlyList<string> SteamBackdrops(SteamVideoItem item)
    {
        var data = Data();
        var game = data.Games.FirstOrDefault(g => g.StoreKey == "steam" && g.SteamAppId == item.AppId) ?? data.Games.FirstOrDefault(g => SteamVideos.AppIdOf(g) == item.AppId);
        return game is null ? Array.Empty<string>() : Images().Backdrops(data.GroupOf(game), game);
    }

    // One grab of the still frame: the encoder binary runs with a fixed argument list and the stored, checked address as ONE argument (SteamVideos.RunEncoder), not IMediaEncoder.ExtractVideoImage,
    // which seeks before the input, gets an empty result on Steam's streams, ends at its own 10 seconds and logs stack traces. Nothing of the request is in the command.
    private Task<string?> ExtractSteamFrame(string url, CancellationToken cancellationToken) =>
        SteamVideos.RunEncoder(_mediaEncoder.EncoderPath, Array.Empty<string>(), url, System.IO.Path.Combine(_paths.CachePath, "playnite-bridge", "steam"), cancellationToken);

    // The item once its clip's frame rate is read (Steam's manifest, once per clip).
    private async Task<BaseItemDto> SteamDtoWithFormat(SteamVideoItem item, CancellationToken cancellationToken)
    {
        await SteamVideos.EnsureFormats(new[] { item }, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
        return SteamDto(item);
    }

    /// <summary>The Steam Special Features of an AppID as items (games/{id}/steamvideos); the caller checks the settings and the game.</summary>
    internal async Task<IReadOnlyList<BaseItemDto>> SteamDtos(IReadOnlyList<string> appIds, CancellationToken cancellationToken)
    {
        var items = appIds.SelectMany(SteamVideos.Items).GroupBy(i => i.Id).Select(g => g.First()).ToList();
        await SteamVideos.EnsureFormats(items, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
        return items.Select(SteamDto).ToList();
    }

    /// <summary>Enable Videos and Enable Steam Special Features are both on.</summary>
    internal static bool SteamEnabled()
    {
        var config = Plugin.Instance!.Configuration;
        return config.EnableVideos && config.EnableSteamSpecialFeatures;
    }

    /// <summary>
    /// The Steam Special Feature of the request's itemId while both settings are on and the games library is visible to the user, and only when its AppID belongs to a game the user may see; else null.
    /// </summary>
    private SteamVideoItem? FindSteam(ActionExecutingContext context)
    {
        var id = Arg<Guid>(context, "itemId");
        if (!SteamEnabled() || OwnIds.Steam(id) is not SteamVideoItem item || GamesLibrary(context) is null)
        {
            return null;
        }

        return Data(context).Games.Any(g => SteamVideos.AppIdOf(g) == item.AppId) ? item : null;
    }

    /// <summary>
    /// The game an extra id belongs to among <paramref name="games"/> (the user's visible games, grouped in <paramref name="groups"/>): a local extra of <paramref name="index"/> = the game whose
    /// short id names its game folder; a Steam Special Feature (only with <paramref name="steam"/>) = the Steam game whose own AppID it is, else the main version of the group of the first game that
    /// links the AppID, else that game; null for any other id.
    /// </summary>
    public static string? ExtraGame(Guid id, ExtrasIndex index, IReadOnlyList<GameEntry> games, IEnumerable<GameGroup> groups, bool steam)
    {
        if (OwnIds.Extra(id, index) is { } extra)
        {
            return games.FirstOrDefault(g => GameExtras.ShortIdOf(g.Id) == extra.ShortId)?.Id;
        }

        if (!steam || OwnIds.Steam(id) is not SteamVideoItem item)
        {
            return null;
        }

        var own = games.FirstOrDefault(g => g.StoreKey == "steam" && g.SteamAppId == item.AppId);
        var first = games.FirstOrDefault(g => SteamVideos.AppIdOf(g) == item.AppId);
        return own?.Id ?? (first is null ? null : groups.FirstOrDefault(g => g.Versions.Contains(first))?.Main.Id ?? first.Id);
    }

    /// <summary>The game of an extra id for PlayniteBridge/extras/{id}/game (<see cref="ExtraGame"/>, from the existing extras index, never a scan).</summary>
    internal string? ExtraGameOf(Guid id, IReadOnlyList<GameEntry> games, IEnumerable<GameGroup> groups) => ExtraGame(id, Extras(false), games, groups, SteamEnabled());

    /// <summary>
    /// A local extra as one direct media source with <paramref name="streams"/> (no transcoding, no path): the playback info's source (no streams) and the base of the item's own source.
    /// </summary>
    public static MediaSourceInfo ExtraMediaSource(GameExtra extra, IReadOnlyList<MediaStream> streams) => new()
    {
        Id = extra.Id.ToString("N"),
        Name = extra.Name,
        Protocol = MediaProtocol.File,
        Type = MediaSourceType.Default,
        Container = extra.Container,
        Size = extra.Size,
        SupportsDirectPlay = true,
        SupportsDirectStream = true,
        SupportsTranscoding = false,
        MediaStreams = streams
    };

    /// <summary>A local extra's file as found by the scan under its id (ranges allowed); nothing from the request becomes a path.</summary>
    private IActionResult? StreamResult(ActionExecutingContext context) => FindExtra(context) is GameExtra extra
        ? new PhysicalFileResult(extra.Path, MimeTypes.GetMimeType(extra.Path) ?? MediaTypeNames.Application.Octet) { EnableRangeProcessing = true }
        : null;

    /// <summary>
    /// A trickplay sheet of a local extra as Jellyfin's TrickplayController answers it (image/jpeg, Content-Disposition attachment, no Cache-Control): the path comes from the scan,
    /// never from the request. 404 for another width or index of an extra id, null (Jellyfin answers) for any other id.
    /// </summary>
    private IActionResult? TrickplayTileResult(ActionExecutingContext context)
    {
        if (FindExtra(context) is not GameExtra extra)
        {
            return null;
        }

        var sheet = GameExtras.TrickplaySheet(extra, Arg<int>(context, "width"), Arg<int>(context, "index"));
        if (sheet is null || !File.Exists(sheet))
        {
            return new NotFoundResult();
        }

        context.HttpContext.Response.Headers.ContentDisposition = "attachment";
        return new PhysicalFileResult(sheet, MediaTypeNames.Image.Jpeg);
    }

    /// <summary>
    /// The local extra of the request's itemId in the existing index (never a scan) when the games library is visible to the user, else null; also null when its folder's short id
    /// belongs to no game the user may see (a game hidden by Playnite or Hide Titles, with ApplyParentalControls also a blocked one).
    /// </summary>
    private GameExtra? FindExtra(ActionExecutingContext context)
    {
        // The cheap id test first: a foreign id builds no index and looks up no user.
        var id = Arg<Guid>(context, "itemId");
        if (!OwnIds.MayBeExtra(id))
        {
            return null;
        }

        var index = Extras(false);
        return OwnIds.Extra(id, index) is not null && GamesLibrary(context) is not null ? ExtraOf(index, Data(context), id) : null;
    }

    /// <summary>The extra of <paramref name="id"/> in <paramref name="index"/> when its folder belongs to a game of <paramref name="data"/> (set lookup, no scan of the games), else null.</summary>
    public static GameExtra? ExtraOf(ExtrasIndex index, GameData data, Guid id) =>
        OwnIds.Extra(id, index) is { } extra && data.HasShortId(extra.ShortId) ? extra : null;

    /// <summary>
    /// The screenshots of the version with the short id <paramref name="shortId"/> (Screenshots.Read in its folder of the extras root), or null while the area is off, the root is refused
    /// or the version has no folder. The version's own folder only. The extras index is the existing one (re-read at most every 10 seconds); the folder of the area is read only here.
    /// </summary>
    private ScreenshotArea? ScreenshotsOf(string shortId) => ScreenshotsOf(shortId, true);

    // The same for the anonymous image routes: only the existing extras index, no rescan of the extras folder for a request without a user.
    private ScreenshotArea? ScreenshotsOfCached(string shortId) => ScreenshotsOf(shortId, false);

    private ScreenshotArea? ScreenshotsOf(string shortId, bool allowScan)
    {
        var config = Plugin.Instance!.Configuration;
        if (!Screenshots.Enabled(config))
        {
            return null;
        }

        var index = Extras(allowScan);
        return index.Root is not null && index.Folders.FirstOrDefault(f => f.ShortId == shortId) is { } folder
            ? Screenshots.Read(System.IO.Path.Combine(index.Root, folder.Name), config)
            : null;
    }

    /// <summary>The wallpapers and artwork of the version with the short id <paramref name="shortId"/> (as <see cref="ScreenshotsOf"/>), or null while the area is off or the version has no folder.</summary>
    private WallpaperArea? WallpapersOf(string shortId) => WallpapersOf(shortId, true);

    // The same for the anonymous image routes: only the existing extras index, no rescan of the extras folder for a request without a user.
    private WallpaperArea? WallpapersOfCached(string shortId) => WallpapersOf(shortId, false);

    private WallpaperArea? WallpapersOf(string shortId, bool allowScan)
    {
        var config = Plugin.Instance!.Configuration;
        if (!Wallpapers.Enabled(config))
        {
            return null;
        }

        var index = Extras(allowScan);
        return index.Root is not null && index.Folders.FirstOrDefault(f => f.ShortId == shortId) is { } folder
            ? Wallpapers.Read(System.IO.Path.Combine(index.Root, folder.Name), config)
            : null;
    }

    /// <summary>The documents of the version with the short id <paramref name="shortId"/> (the version's own folder of the extras root), or null while the area is off or the version has no folder.</summary>
    private DocumentArea? DocumentsOf(string shortId) => DocumentsOf(shortId, true);

    // The same for the anonymous image routes: only the existing extras index, no rescan of the extras folder for a request without a user.
    private DocumentArea? DocumentsOfCached(string shortId) => DocumentsOf(shortId, false);

    private DocumentArea? DocumentsOf(string shortId, bool allowScan)
    {
        var config = Plugin.Instance!.Configuration;
        if (!Documents.Enabled(config))
        {
            return null;
        }

        var index = Extras(allowScan);
        return index.Root is not null && index.Folders.FirstOrDefault(f => f.ShortId == shortId) is { } folder
            ? Documents.Read(System.IO.Path.Combine(index.Root, folder.Name), config)
            : null;
    }

    /// <summary>The document an id names, for a caller that checked the user itself (<paramref name="mayShow"/> accepts the version's short id); null for any other id.</summary>
    internal (DocumentFile File, DocumentKey Key)? DocumentOfId(Guid id, Func<string, bool> mayShow) => OwnIds.Document(id, mayShow, DocumentsOf);

    /// <summary>The rows of the documents of a version (the counts of the game payload and the list); none while the area is off or empty.</summary>
    internal IReadOnlyList<DocumentRow> DocumentRowsOf(string gameId) =>
        DocumentsOf(GameExtras.ShortIdOf(gameId)) is { } area ? Documents.Rows(area, Plugin.Instance!.Configuration) : Array.Empty<DocumentRow>();

    /// <summary>The files of a row of a version in the shown order; empty without the area.</summary>
    internal IReadOnlyList<DocumentFile> DocumentFilesOf(string gameId, int row, int seed) =>
        DocumentsOf(GameExtras.ShortIdOf(gameId)) is { } area ? Documents.Files(area, row, Plugin.Instance!.Configuration, seed) : Array.Empty<DocumentFile>();

    /// <summary>The document of the request's itemId while the games library is visible to the user and the id's version is a game the user may see; else null.</summary>
    private (DocumentFile File, DocumentKey Key)? FindDocument(ActionExecutingContext context) =>
        Documents.Decode(Arg<Guid>(context, "itemId")) is null ? null : OwnIds.Document(Arg<Guid>(context, "itemId"), shortId => ShortIdVisible(context, shortId), DocumentsOf);

    /// <summary>
    /// A document as Jellyfin's Book item (LocationType FileSystem): Path = "documents/&lt;file name&gt;", never a server path, ending in the file's extension (Jellyfin Web's readers
    /// choose by it), its cover as Primary, CanDownload as for a local extra (the reader downloads the file by Items/{id}/Download).
    /// </summary>
    public static BaseItemDto DocumentDto(DocumentFile file, DocumentKey key, string serverId, bool canDownload)
    {
        var id = Documents.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash);
        return new BaseItemDto
        {
            Id = id,
            Name = System.IO.Path.GetFileNameWithoutExtension(file.FileName),
            SortName = file.FileName,
            ServerId = serverId,
            Type = BaseItemKind.Book,
            MediaType = MediaType.Book,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = Documents.Tag(file) },
            BackdropImageTags = Array.Empty<string>(),
            PrimaryImageAspectRatio = 0.75,
            DateCreated = file.Modified,
            Path = "documents/" + file.FileName,
            Container = System.IO.Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
            CanDownload = canDownload,
            UserData = new UserItemDataDto { Key = id.ToString("N"), ItemId = id, Played = false }
        };
    }

    /// <summary>Items/{id}/Download for a document: the file with range processing, 403 without the permission, 404 for a vanished file; null for any other id.</summary>
    private IActionResult? DocumentDownload(ActionExecutingContext context) =>
        FindDocument(context) is (DocumentFile file, _) ? ScreenshotDownloadAnswer(new ScreenshotFile(file.Slot, file.Path, file.FileName, file.Modified, file.Length, file.Hash), CanDownload(context)) : null;

    /// <summary>
    /// The tile shape of a picture row (<see cref="AreaCommon.ShapeOf"/>): fixed for Landscape and Portrait, for Auto from the ratios of <paramref name="firstFiles"/> (the row's first tiles in the shown order),
    /// each measured once per file and state by the ratio cache.
    /// </summary>
    internal string RowShape(Configuration.TileShape setting, IEnumerable<(string Path, string Tag)> firstFiles) =>
        AreaCommon.ShapeOf(setting, () => firstFiles.Select(f => _ratios.Get(f.Path, f.Tag)).ToList());

    /// <summary>The rows of the screenshots of a version (GamesController: the counts of the game payload and the list); none while the area is off or empty.</summary>
    internal IReadOnlyList<ScreenshotRow> ScreenshotRowsOf(string gameId) =>
        ScreenshotsOf(GameExtras.ShortIdOf(gameId)) is { } area ? Screenshots.Rows(area, Plugin.Instance!.Configuration) : Array.Empty<ScreenshotRow>();

    /// <summary>The files of a row of a version in the shown order (<see cref="Screenshots.Files"/>); empty without the area.</summary>
    internal IReadOnlyList<ScreenshotFile> ScreenshotFilesOf(string gameId, int row, int seed) =>
        ScreenshotsOf(GameExtras.ShortIdOf(gameId)) is { } area ? Screenshots.Files(area, row, Plugin.Instance!.Configuration, seed) : Array.Empty<ScreenshotFile>();

    /// <summary>
    /// The screenshot of the request's itemId and what its id says, while the games library is visible to the user and the id's version is a game the user may see; else null.
    /// The path is the listing's, nothing from the request becomes a path.
    /// </summary>
    private (ScreenshotFile File, ScreenshotKey Key)? FindScreenshot(ActionExecutingContext context) =>
        ScreenshotOfId(Arg<Guid>(context, "itemId"), shortId => ShortIdVisible(context, shortId), ScreenshotsOf);

    /// <summary>True when the games library is visible to the request's user and <paramref name="shortId"/> is a game they may see; asked only after an id passed its own decode (OwnIds).</summary>
    private bool ShortIdVisible(ActionExecutingContext context, string shortId) =>
        !UnknownShortId(shortId) && GamesLibrary(context) is not null && Data(context).HasShortId(shortId);

    // True when the stored game data, current for this settings object and copy, has no game with this short id: no user lookup, no build for ids of other libraries that end in the marker byte.
    private bool UnknownShortId(string shortId) =>
        _data.Current is { } stored && stored.IsFor(Plugin.Instance!.Configuration, PlayniteSnapshot.Generation) && !stored.HasShortId(shortId);

    /// <summary>
    /// The screenshot an item id names: the id must decode as an item id (no disk, no lookup before that), <paramref name="mayShow"/> must accept its version's short id
    /// (the version is a game the asking user may see) and <paramref name="areaOf"/> (the version's listing) must hold the file; else null.
    /// </summary>
    public static (ScreenshotFile File, ScreenshotKey Key)? ScreenshotOfId(Guid id, Func<string, bool> mayShow, Func<string, ScreenshotArea?> areaOf) =>
        OwnIds.Screenshot(id, mayShow, areaOf);

    /// <summary>
    /// The screenshot an image request asks for: image endpoints are anonymous (as for every item), so only the id decides; it must name a version of the base game data
    /// (Playnite's, not the user's), which only the list route hands out.
    /// </summary>
    private ScreenshotFile? ImageScreenshot(Guid id) => ScreenshotOfId(id, shortId => Data().HasShortId(shortId), ScreenshotsOfCached)?.File;

    /// <summary>
    /// A screenshot as Jellyfin's Photo item (LocationType FileSystem, never a server path): its file as Primary (tag from the file), ParentId = the id of its row (the sibling list
    /// the viewer asks for), SortName the file name, CanDownload as for a local extra. <paramref name="key"/> is the id it was found by (its seed).
    /// </summary>
    public static BaseItemDto ScreenshotDto(ScreenshotFile file, ScreenshotKey key, string serverId, bool canDownload, Configuration.PluginConfiguration config, ScreenshotArea area)
    {
        var id = Screenshots.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash);
        return new BaseItemDto
        {
            Id = id,
            Name = System.IO.Path.GetFileNameWithoutExtension(file.FileName),
            SortName = file.FileName,
            ServerId = serverId,
            Type = BaseItemKind.Photo,
            MediaType = MediaType.Photo,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            ParentId = Screenshots.RowId(key.ShortId, Screenshots.RowOf(area, config, file.Slot), key.Seed),
            ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = Screenshots.Tag(file) },
            BackdropImageTags = Array.Empty<string>(),
            DateCreated = file.Modified,
            Path = file.FileName,
            Container = System.IO.Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
            CanDownload = canDownload,
            UserData = new UserItemDataDto { Key = id.ToString("N"), ItemId = id, Played = false }
        };
    }

    /// <summary>
    /// The answer to the sibling query of Jellyfin's photo viewer (Items?ParentId=&lt;row id&gt;&amp;MediaTypes=Photo,Video, no limit): every screenshot of the row in the shown order,
    /// as Photo items. Null (Jellyfin answers) while the row has no screenshots.
    /// </summary>
    private IActionResult? ScreenshotSiblings(ActionExecutingContext context, ScreenshotKey row)
    {
        // The row id is proved ours by the gate (OwnIds.ScreenshotRow); the row must also exist (files behind it), else Jellyfin answers.
        var config = Plugin.Instance!.Configuration;
        var area = ScreenshotsOf(row.ShortId);
        var files = area is not null ? Screenshots.Files(area, row.Slot, config, row.Seed) : Array.Empty<ScreenshotFile>();
        if (area is null || files.Count == 0)
        {
            return null;
        }

        return new OkObjectResult(ScreenshotSiblingList(row, files, Arg<int?>(context, "startIndex") ?? 0, Arg<int?>(context, "limit"), _host.SystemId, CanDownload(context), config, area));
    }

    /// <summary>The sibling list as Jellyfin's items query answers it: <paramref name="files"/> (the row in the shown order) from <paramref name="start"/>, at most <paramref name="limit"/>; TotalRecordCount counts all of them.</summary>
    public static QueryResult<BaseItemDto> ScreenshotSiblingList(ScreenshotKey row, IReadOnlyList<ScreenshotFile> files, int start, int? limit, string serverId, bool canDownload,
        Configuration.PluginConfiguration config, ScreenshotArea area) =>
        new(start, files.Count, files.Skip(start).Take(limit ?? int.MaxValue).Select(f => ScreenshotDto(f, row, serverId, canDownload, config, area)).ToList());

    /// <summary>Items/{id}/Download for a screenshot as for a local extra: the file with range processing, 403 without the permission, 404 for a vanished file; null for any other id.</summary>
    private IActionResult? ScreenshotDownload(ActionExecutingContext context) =>
        FindScreenshot(context) is (ScreenshotFile file, _) ? ScreenshotDownloadAnswer(file, CanDownload(context)) : null;

    /// <summary>The download answer for <paramref name="file"/> (null = no screenshot: Jellyfin answers): 403 when not <paramref name="allowed"/>, 404 for a vanished file, else the file with range processing and its file name.</summary>
    public static IActionResult? ScreenshotDownloadAnswer(ScreenshotFile? file, bool allowed)
    {
        if (file is null)
        {
            return null;
        }

        if (!allowed)
        {
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        return File.Exists(file.Path)
            ? new PhysicalFileResult(file.Path, MimeTypes.GetMimeType(file.Path) ?? MediaTypeNames.Application.Octet)
            {
                EnableRangeProcessing = true,
                FileDownloadName = file.FileName.Replace("\"", string.Empty, StringComparison.Ordinal)
            }
            : new NotFoundResult();
    }

    /// <summary>The rows of the wallpapers and artwork of a version (GamesController); none while the area is off or empty.</summary>
    internal IReadOnlyList<WallpaperRow> WallpaperRowsOf(string gameId) =>
        WallpapersOf(GameExtras.ShortIdOf(gameId)) is { } area ? Wallpapers.Rows(area, Plugin.Instance!.Configuration) : Array.Empty<WallpaperRow>();

    /// <summary>The files of a wallpapers row of a version in the shown order; empty without the area.</summary>
    internal IReadOnlyList<WallpaperFile> WallpaperFilesOf(string gameId, int row, int seed) =>
        WallpapersOf(GameExtras.ShortIdOf(gameId)) is { } area ? Wallpapers.Files(area, row, Plugin.Instance!.Configuration, seed) : Array.Empty<WallpaperFile>();

    /// <summary>The picture of the request's itemId and what its id says (as <see cref="FindScreenshot"/>); else null.</summary>
    private (WallpaperFile File, WallpaperKey Key)? FindWallpaper(ActionExecutingContext context) =>
        OwnIds.Wallpaper(Arg<Guid>(context, "itemId"), shortId => ShortIdVisible(context, shortId), WallpapersOf);

    /// <summary>The picture an image request asks for (as <see cref="ImageScreenshot"/>): only the id decides, it must name a version of the base game data.</summary>
    private WallpaperFile? ImageWallpaper(Guid id) => OwnIds.Wallpaper(id, shortId => Data().HasShortId(shortId), WallpapersOfCached)?.File;

    /// <summary>A wallpapers picture as Jellyfin's Photo item (as <see cref="ScreenshotDto"/>, with the wallpapers row id as ParentId).</summary>
    public static BaseItemDto WallpaperDto(WallpaperFile file, WallpaperKey key, string serverId, bool canDownload, Configuration.PluginConfiguration config, WallpaperArea area)
    {
        var id = Wallpapers.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash);
        return new BaseItemDto
        {
            Id = id,
            Name = System.IO.Path.GetFileNameWithoutExtension(file.FileName),
            SortName = file.FileName,
            ServerId = serverId,
            Type = BaseItemKind.Photo,
            MediaType = MediaType.Photo,
            LocationType = LocationType.FileSystem,
            IsFolder = false,
            ParentId = Wallpapers.RowId(key.ShortId, Wallpapers.RowOf(area, config, file.Slot), key.Seed),
            ImageTags = new Dictionary<ImageType, string> { [ImageType.Primary] = Wallpapers.Tag(file) },
            BackdropImageTags = Array.Empty<string>(),
            DateCreated = file.Modified,
            Path = file.FileName,
            Container = System.IO.Path.GetExtension(file.FileName).TrimStart('.').ToLowerInvariant(),
            CanDownload = canDownload,
            UserData = new UserItemDataDto { Key = id.ToString("N"), ItemId = id, Played = false }
        };
    }

    /// <summary>The sibling list of a wallpapers row for Jellyfin's photo viewer (as <see cref="ScreenshotSiblings"/>); null while the row has no pictures.</summary>
    private IActionResult? WallpaperSiblings(ActionExecutingContext context, WallpaperKey row)
    {
        var config = Plugin.Instance!.Configuration;
        var area = WallpapersOf(row.ShortId);
        var files = area is not null ? Wallpapers.Files(area, row.Slot, config, row.Seed) : Array.Empty<WallpaperFile>();
        if (area is null || files.Count == 0)
        {
            return null;
        }

        var start = Arg<int?>(context, "startIndex") ?? 0;
        var limit = Arg<int?>(context, "limit");
        var canDownload = CanDownload(context);
        return new OkObjectResult(new QueryResult<BaseItemDto>(start, files.Count, files.Skip(start).Take(limit ?? int.MaxValue).Select(f => WallpaperDto(f, row, _host.SystemId, canDownload, config, area)).ToList()));
    }

    /// <summary>Items/{id}/Download for a wallpapers picture, as for a screenshot; null for any other id.</summary>
    private IActionResult? WallpaperDownload(ActionExecutingContext context) =>
        FindWallpaper(context) is (WallpaperFile file, _) ? ScreenshotDownloadAnswer(new ScreenshotFile(file.Slot, file.Path, file.FileName, file.Modified, file.Length, file.Hash), CanDownload(context)) : null;

    /// <summary>
    /// The index of the game folders in the root GameExtras.Root chooses (the cache folder, or the extras folder with SeparateExtrasFolder); re-read at most every 10 seconds, and only with
    /// <paramref name="allowScan"/>, else the existing one. Empty when that root is refused (the cache folder by ImageCache's path checks, so only a writable folder is ever read; the extras folder
    /// by GameExtras.ExtrasFolderError or when it is missing). A refused extras folder and a read error are logged once per changed state.
    /// </summary>
    private ExtrasIndex Extras(bool allowScan)
    {
        var cached = CurrentImages();
        var (root, refused) = GameExtras.Root(cached.Config, cached.Cache.CacheFolder, cached.Cache.PathError,
            f => ImageCache.CheckPaths(f, cached.Folder, cached.Steam, cached.Jellyfin));
        var index = root is null ? ExtrasIndex.Empty : GameExtras.Index(root, allowScan, Screenshots.AreaName(cached.Config), Soundtrack.FolderOf(cached.Config), GameExtras.OtherAreas(cached.Config), VideoRows.Slots(cached.Config));
        var state = refused is not null ? "|" + refused : index.Error is null ? null : root + "|" + index.Error;
        if (Interlocked.Exchange(ref _lastExtrasFailure, state) != state && state is not null)
        {
            if (refused is not null)
            {
                _logger.LogWarning("PlayniteBridge: no local extras from the extras folder \"{Folder}\": {Message}", cached.Config.ExtrasFolder, refused);
            }
            else
            {
                _logger.LogWarning("PlayniteBridge: cannot read local extras in \"{Folder}\": {Message}", root, index.Error);
            }
        }

        return index;
    }

    /// <summary>A version (<paramref name="game"/>) of a group as Jellyfin's item; its images lie in its own game folder or are read in place.</summary>
    private BaseItemDto ToDto(GameGroup group, GameEntry game, Guid libraryId, ImageCache images)
    {
        var id = Guid.Parse(game.Id);
        var dto = new BaseItemDto
        {
            Id = id,
            Name = GameGroups.Title(group),
            SortName = GameQuery.SortName(game),
            ServerId = _host.SystemId,
            Type = BaseItemKind.Movie,
            MediaType = MediaType.Video,
            IsFolder = false,
            ParentId = libraryId,
            ProductionYear = game.Year,
            PremiereDate = game.ReleaseDate,
            DateCreated = game.Added,
            CommunityRating = game.CommunityScore / 10f,
            CriticRating = game.CriticScore,
            OfficialRating = GameQuery.OfficialRating(game),
            RunTimeTicks = game.Playtime > 0 ? game.Playtime * TimeSpan.TicksPerSecond : null,
            Genres = game.Genres,
            Studios = game.PublisherRefs,
            Tags = game.Tags,
            LocationType = LocationType.Virtual,
            ImageTags = new Dictionary<ImageType, string>(),
            BackdropImageTags = Array.Empty<string>(),
            UserData = new UserItemDataDto
            {
                Key = game.Id,
                ItemId = id,
                IsFavorite = game.Favorite,
                Played = GameQuery.IsPlayed(game, Plugin.Instance!.Configuration.PlayedMark),
                PlayCount = (int)Math.Min(game.PlayCount, int.MaxValue),
                LastPlayedDate = game.LastActivity
            }
        };
        SetGameImages(dto, GameImages(images, group, game));
        return dto;
    }

    /// <summary>A tile of a list: the group's main version; MediaSourceCount lets Jellyfin draw its own number marker.</summary>
    private BaseItemDto ListDto(GameGroup group, Guid libraryId, ImageCache images)
    {
        var dto = ToDto(group, group.Main, libraryId, images);
        dto.MediaSourceCount = GameGroups.MediaSourceCount(group, Plugin.Instance!.Configuration);
        return dto;
    }

    /// <summary>
    /// A collection folder: name and added date of the series, cover and backdrop of its poster game; no year and no premiere date, like Jellyfin's box sets
    /// (the Collections tab sorts by GameCollection.Released on the server).
    /// </summary>
    private BaseItemDto CollectionDto(GameCollection collection, GameData data, Guid libraryId, ImageCache images)
    {
        var dto = new BaseItemDto
        {
            Id = collection.Id,
            Name = collection.Name,
            SortName = collection.Name,
            ServerId = _host.SystemId,
            Type = BaseItemKind.Folder,
            IsFolder = true,
            ParentId = libraryId,
            ChildCount = collection.GroupCount,
            LocationType = LocationType.Virtual,
            DateCreated = collection.Added,
            ImageTags = new Dictionary<ImageType, string>(),
            BackdropImageTags = Array.Empty<string>()
        };
        IEnumerable<(ImageType Type, string Tag, string Path)> shown = collection.Poster is null
            ? Array.Empty<(ImageType, string, string)>()
            : GameImages(images, data.GroupOf(collection.Poster), collection.Poster).Where(i => i.Type is ImageType.Primary or ImageType.Backdrop);
        // Own pictures (CollectionPictures) come before the poster game's: an own poster replaces its cover; own backdrops replace its backdrops only while Show on Collections
        // is on and the Source includes Own (OwnBackdropsShown), else Jellyfin's page keeps the poster game's backdrop.
        var own = OwnPictures(collection);
        if (own.Poster is string ownPoster && CollectionPictures.Tag(ownPoster) is string posterTag)
        {
            shown = shown.Where(i => i.Type != ImageType.Primary).Prepend((ImageType.Primary, posterTag, ownPoster));
        }

        if (OwnBackdropsShown(Plugin.Instance!.Configuration) && own.Backdrops.Select(b => (Path: b, Tag: CollectionPictures.Tag(b))).Where(b => b.Tag is not null).ToList() is { Count: > 0 } ownBackdrops)
        {
            shown = shown.Where(i => i.Type != ImageType.Backdrop).Concat(ownBackdrops.Select(b => (ImageType.Backdrop, b.Tag!, b.Path)));
        }

        SetGameImages(dto, shown);
        return dto;
    }

    /// <summary>True when the own backdrops of a collection may stand in Jellyfin's own page and image route: Show on Collections is on and the Source is Own, Own then Games or Mixed.</summary>
    public static bool OwnBackdropsShown(Configuration.PluginConfiguration config) => config.ArtBackdropsCollectionShow && config.ArtBackdropsCollectionSource != Configuration.CollectionBackdropSource.Games;

    private BaseItemDto GenreDto(NameGuidPair genre) => NamedDto(genre, BaseItemKind.Genre);

    /// <summary>A genre, developer (Person) or publisher (Studio) of the games: id, name and type only.</summary>
    private BaseItemDto NamedDto(NameGuidPair named, BaseItemKind type) => new()
    {
        Id = named.Id,
        Name = named.Name,
        ServerId = _host.SystemId,
        Type = type
    };

    /// <summary>Images of a game (<see cref="GameImages"/>) on its item with their BlurHashes, a cover as a 2:3 poster; tags change with the source file (time and length).</summary>
    private void SetGameImages(BaseItemDto dto, IEnumerable<(ImageType Type, string Tag, string Path)> images)
    {
        SetImages(dto, images);
        if (dto.ImageTags!.ContainsKey(ImageType.Primary))
        {
            dto.PrimaryImageAspectRatio = 2.0 / 3.0;
        }
    }

    /// <summary>ImageTags, BackdropImageTags (in the given order) and the BlurHash of every image (<see cref="AddBlurHash"/>) of an item.</summary>
    private void SetImages(BaseItemDto dto, IEnumerable<(ImageType Type, string Tag, string Path)> images)
    {
        var backdrops = new List<string>();
        foreach (var (type, tag, path) in images)
        {
            if (type == ImageType.Backdrop)
            {
                backdrops.Add(tag);
            }
            else
            {
                dto.ImageTags![type] = tag;
            }

            AddBlurHash(dto, type, tag, path);
        }

        dto.BackdropImageTags = backdrops.ToArray();
    }

    /// <summary>
    /// The images of a version with the tags its item carries, each only with a file: Primary (the cover), the backdrops in order (index i = Backdrop/{i}), Logo, Banner, Thumb, Art and Disc.
    /// The one list used by the game's item and by Sync Artwork's BlurHashes.
    /// </summary>
    public static IReadOnlyList<(ImageType Type, string Tag, string Path)> GameImages(ImageCache images, GameGroup group, GameEntry game)
    {
        var shown = new List<(ImageType Type, string Tag, string Path)>();
        if (TaggedImage(images, group, game, ImageType.Primary) is var (cover, coverTag))
        {
            shown.Add((ImageType.Primary, coverTag, cover));
        }

        shown.AddRange(TaggedBackdrops(images, group, game).Select(b => (ImageType.Backdrop, b.Tag, b.Path)));
        foreach (var type in new[] { ImageType.Logo, ImageType.Banner, ImageType.Thumb, ImageType.Art, ImageType.Disc })
        {
            if (TaggedImage(images, group, game, type) is var (path, tag))
            {
                shown.Add((type, tag, path));
            }
        }

        return shown;
    }

    /// <summary>
    /// The images of a local extra with the tags its item carries: Primary = the image next to the video, else for a trailer or special feature the still frame under the video's tag
    /// (its file may not exist yet, <see cref="StillFramePath(string, GameExtra, string)"/>); then Logo, Art, Disc, Banner and Thumb and the backdrops in order (GameExtras.LocalImages),
    /// each only with a file. The one list used by the extra's item and by Sync Artwork's BlurHashes.
    /// </summary>
    public static IReadOnlyList<(ImageType Type, string Tag, string Path)> ExtraImages(GameExtra extra, string cachePath)
    {
        var shown = new List<(ImageType Type, string Tag, string Path)>();
        var primaryTag = extra.ImagePath is not null ? ImageCache.Tag(extra.ImagePath)
            : extra.Kind is ExtraKind.Trailer or ExtraKind.Special ? ImageCache.Tag(extra.Path) : null;
        if (primaryTag is not null)
        {
            shown.Add((ImageType.Primary, primaryTag, extra.ImagePath ?? StillFramePath(cachePath, extra, primaryTag)));
        }

        foreach (var (type, file) in extra.Images ?? new Dictionary<ImageType, string>())
        {
            if (ImageCache.Tag(file) is string tag)
            {
                shown.Add((type, tag, file));
            }
        }

        foreach (var backdrop in extra.Backdrops ?? Array.Empty<string>())
        {
            if (ImageCache.Tag(backdrop) is string tag)
            {
                shown.Add((ImageType.Backdrop, tag, backdrop));
            }
        }

        return shown;
    }

    /// <summary>
    /// The file of a single image type of a version (ImageCache.Image: the user's file, the plugin's copy or the source in place) with its tag, as a game's ImageTags carry it;
    /// null without a file. Shared by the game's item and ArtworkPlusLink.
    /// </summary>
    public static (string Path, string Tag)? TaggedImage(ImageCache images, GameGroup group, GameEntry game, ImageType type) =>
        images.Image(group, game, type) is string path && ImageCache.Tag(path) is string tag ? (path, tag) : null;

    /// <summary>
    /// The backdrops of a version (ImageCache.Backdrops) that exist, with their tags, in order: index i is Backdrop/{i} of the game's BackdropImageTags. Shared by the game's item and ArtworkPlusLink.
    /// </summary>
    public static IReadOnlyList<(string Path, string Tag)> TaggedBackdrops(ImageCache images, GameGroup group, GameEntry game) =>
        images.Backdrops(group, game).Select(b => (Path: b, Tag: ImageCache.Tag(b))).Where(b => b.Tag is not null).Select(b => (b.Path, b.Tag!)).ToList();

    /// <summary>
    /// Artwork on Version Change: the version whose image serves a request for <paramref name="game"/>'s cover, backdrop (of <paramref name="index"/>), logo or clearart asked with
    /// <paramref name="asked"/>: another version of its game (detail group, twins included) whose image of that type carries the tag, else <paramref name="game"/> itself (other types, no tag,
    /// its own tag, a tag of no version of the game).
    /// </summary>
    public static GameEntry ImageOwner(ImageCache images, GameData data, GameEntry game, ImageType type, int index, string? asked)
    {
        if (type is not (ImageType.Primary or ImageType.Backdrop or ImageType.Logo or ImageType.Art) || string.IsNullOrEmpty(asked))
        {
            return game;
        }

        var versions = data.DetailGroupOf(game.Id) ?? data.GroupOf(game);
        var others = versions.Versions.Select(v => v.Id).Concat(versions.Twins?.Keys ?? Enumerable.Empty<string>())
            .Select(v => data.Find(Guid.Parse(v))).Where(h => h?.Kind == GameHitKind.Game).Select(h => h!.Game!);
        return TagOwner(game, others, asked, v => VersionTag(images, data.GroupOf(v), v, type, index));
    }

    /// <summary>
    /// Artwork on Version Change: the version whose image carries the asked tag (<paramref name="tagOf"/>): <paramref name="own"/> when the tag is
    /// empty, its own or nobody's, else the first of <paramref name="others"/> with it.
    /// </summary>
    public static T TagOwner<T>(T own, IEnumerable<T> others, string? asked, Func<T, string?> tagOf)
        where T : class =>
        string.IsNullOrEmpty(asked) || tagOf(own) == asked ? own : others.FirstOrDefault(v => !ReferenceEquals(v, own) && tagOf(v) == asked) ?? own;

    /// <summary>The tag of a version's image of a type as its item carries it (the backdrop of <paramref name="index"/>); null without one.</summary>
    private static string? VersionTag(ImageCache images, GameGroup group, GameEntry game, ImageType type, int index) =>
        type == ImageType.Backdrop
            ? TaggedBackdrops(images, group, game).Select(b => b.Tag).ElementAtOrDefault(index)
            : TaggedImage(images, group, game, type)?.Tag;

    /// <summary>The cover of a version: the user's Primary file in its own game folder, else the plugin's copy, else Playnite's cover.</summary>
    private static string? Cover(ImageCache images, GameGroup group, GameEntry game) => images.Image(group, game, ImageType.Primary);

    /// <summary>
    /// The BlurHash of <paramref name="file"/> under the image tag <paramref name="tag"/>, in Jellyfin's form { type: { tag: hash } }: the one stored for the file in its current state
    /// (BlurHashStore, by Sync Artwork or an earlier background computation; none when the image gives none), else once BlurHashCache has it (otherwise the file is queued and nothing is added;
    /// the request never waits). Several tags of a type are collected.
    /// </summary>
    private void AddBlurHash(BaseItemDto dto, ImageType type, string tag, string file)
    {
        var hash = BlurHashStore.Shared(_logger).TryGet(file, out var stored) ? (stored.Length == 0 ? null : stored)
            : ImageCache.Tag(file) is string fileTag ? _blurHashes.Get(file, fileTag) : null;
        if (hash is not null)
        {
            dto.ImageBlurHashes ??= new Dictionary<ImageType, Dictionary<string, string>>();
            if (!dto.ImageBlurHashes.TryGetValue(type, out var hashes))
            {
                dto.ImageBlurHashes[type] = hashes = new Dictionary<string, string>();
            }

            hashes[tag] = hash;
        }
    }

    /// <summary>The distinct genres of the given games, sorted by name.</summary>
    private static List<NameGuidPair> Genres(IEnumerable<GameEntry> games) =>
        games.SelectMany(g => g.GenreRefs).GroupBy(r => r.Id).Select(r => r.First())
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

    // The names of a filter answer go through GameQuery.FilterSafe, then distinct; the ids stay those of the raw names.
    private static string[] Distinct(IEnumerable<string> names) =>
        names.Select(GameQuery.FilterSafe).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    private static NameGuidPair[] Distinct(IEnumerable<NameGuidPair> pairs) =>
        pairs.Select(p => new NameGuidPair { Id = p.Id, Name = GameQuery.FilterSafe(p.Name ?? string.Empty) })
            .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    // Set by the web script on its filter requests.
    private static string? FilterMarker(ActionExecutingContext context) => context.HttpContext.Request.Query["pbFilters"].FirstOrDefault();

    private static T? Arg<T>(ActionExecutingContext context, string name) =>
        context.ActionArguments.TryGetValue(name, out var value) && value is T typed ? typed : default;

    // Not null while a request is answered: OnActionExecutionAsync hands a refused folder to Jellyfin first.
    private static string Folder() => PlayniteReader.ResolveFolder(Plugin.Instance!.Configuration.PlayniteFolder)!;

    /// <summary>
    /// The image cache, reused while the settings object, the Playnite folder and the cache folder setting stay the same and for at most five minutes, so the Steam registry read
    /// and the folder checks do not run on every request.
    /// </summary>
    internal ImageCache Images() => CurrentImages().Cache;

    private sealed record CollectionFolderState(Configuration.PluginConfiguration Config, string Setting, DateTime Created, string? Folder);

    /// <summary>
    /// The folder of the own collection pictures (CollectionPicturesFolder, empty = Jellyfin's metadata path + Game Collections) while its path checks pass (<see cref="CollectionPictures.FolderError"/>),
    /// else null = no own pictures; checked once per settings object and five minutes. Nothing is created or written.
    /// </summary>
    internal string? CollectionFolder()
    {
        var config = Plugin.Instance!.Configuration;
        var cached = _collectionFolder;
        if (cached is not null && ReferenceEquals(cached.Config, config) && cached.Setting == config.CollectionPicturesFolder && DateTime.UtcNow - cached.Created < TimeSpan.FromMinutes(5))
        {
            return cached.Folder;
        }

        var images = CurrentImages();
        var folder = CollectionPictures.Resolve(config.CollectionPicturesFolder, _paths.InternalMetadataPath);
        var refused = CollectionPictures.FolderError(folder, images.Cache.CacheFolder, f => ImageCache.CheckPaths(f, images.Folder, images.Steam, images.Jellyfin)) is not null;
        _collectionFolder = new CollectionFolderState(config, config.CollectionPicturesFolder, DateTime.UtcNow, refused ? null : folder);
        return _collectionFolder.Folder;
    }

    /// <summary>The own pictures of a collection (<see cref="CollectionPictures.Read"/>); none while the folder is refused or missing.</summary>
    internal CollectionPicture OwnPictures(GameCollection collection) => CollectionPictures.Read(CollectionFolder(), collection.Name);

    /// <summary>The games of a collection as <paramref name="data"/> shows them: the main version of each group once, by sort name (each game independent: its own backdrops).</summary>
    public static IReadOnlyList<GameEntry> CollectionMains(GameData data, GameCollection collection) =>
        collection.Games.Select(g => data.GroupOf(g).Main).DistinctBy(g => g.Id).OrderBy(g => GameQuery.SortName(g), StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>
    /// The backdrop list of a collection page for one visit (<see cref="CollectionBackdrops.Compose"/>): the own backdrops and the backdrops of the main versions of the collection's games in
    /// <paramref name="data"/> (the asking user's games), as [tag, index] entries.
    /// </summary>
    internal List<CollectionBackdrop> CollectionBackdropList(GameData data, GameCollection collection, Configuration.PluginConfiguration config, Random random)
    {
        var images = Images();
        var own = OwnPictures(collection).Backdrops.Select((path, index) => (Tag: CollectionPictures.Tag(path), Index: index)).Where(b => b.Tag is not null)
            .Select(b => new CollectionBackdrop(b.Tag!, b.Index)).ToList();
        var games = CollectionMains(data, collection)
            .Select(g => new CollectionGameBackdrops(g.ReleaseDate, GameQuery.SortName(g), TaggedBackdrops(images, data.GroupOf(g), g).Select((b, i) => new CollectionBackdrop(b.Tag, i)).ToList()))
            .ToList();
        return CollectionBackdrops.Compose(CollectionBackdropSettings.Of(config), own, games, random);
    }

    private readonly CollectionTagIndex _collectionTags = new();

    /// <summary>
    /// The backdrop file of a collection page that carries <paramref name="tag"/>: the own backdrops first, then the backdrops of the games of <paramref name="userCollection"/>
    /// (as the asking user's data <paramref name="data"/> holds it, main versions first; null = own pictures only); null when none does. One map per collection and game data is reused for
    /// 10 seconds (<see cref="CollectionTagIndex"/>): no file-system call per request. The path comes from the files found, never from the request.
    /// </summary>
    internal string? CollectionBackdropByTag(GameData data, GameCollection collection, GameCollection? userCollection, string tag) =>
        _collectionTags.Find(collection.Id, data, tag, () =>
        {
            var images = Images();
            var files = OwnPictures(collection).Backdrops.Select(b => (Path: b, Tag: CollectionPictures.Tag(b)!));
            return userCollection is null
                ? files
                : files.Concat(CollectionMains(data, userCollection).Concat(userCollection.Games).SelectMany(g => TaggedBackdrops(images, data.GroupOf(g), g)));
        });

    /// <summary>The cached image cache with the Steam folder and Jellyfin's folders it was checked against (<see cref="Images"/>).</summary>
    private CachedImages CurrentImages()
    {
        var config = Plugin.Instance!.Configuration;
        var folder = Folder();
        var cached = _images;
        if (cached is not null && ReferenceEquals(cached.Config, config) && cached.Folder == folder && cached.CacheSetting == config.CacheFolder
            && DateTime.UtcNow - cached.Created < TimeSpan.FromMinutes(5))
        {
            return cached;
        }

        var steam = ImageCache.SteamFolder();
        var jellyfin = JellyfinFolders.Of(_paths, _libraryManager);
        var cache = new ImageCache(config, folder, ImageCache.ResolveCacheFolder(config.CacheFolder, _paths.InternalMetadataPath), steam, jellyfin, _logger);
        cached = new CachedImages(config, folder, config.CacheFolder, DateTime.UtcNow, cache, steam, jellyfin);
        _images = cached;
        return cached;
    }

    /// <summary>
    /// The shared game data of the current copy and settings (GameDataCache); empty when Playnite cannot be read (one warning per changed state, not one error per request),
    /// and then read again on the next call.
    /// </summary>
    internal GameData Data()
    {
        var config = Plugin.Instance!.Configuration;
        // The generation before the folder: a sync in between leaves the data one generation behind, built again once.
        var generation = PlayniteSnapshot.Generation;
        var set = PlayniteSnapshot.CurrentSet();
        if (set is null)
        {
            // Cheap check first, so not every request throws while there is no copy of Playnite's data.
            WarnOnce(Folder(), PlayniteReader.NoSync);
            return GameData.Empty;
        }

        return _data.Get(set, config, generation, () =>
        {
            try
            {
                var games = WithoutRealItems(PlayniteReader.ReadVisibleGames(config.ShowHidden));
                Interlocked.Exchange(ref _lastReadFailure, null);
                return games;
            }
            catch (Exception ex)
            {
                WarnOnce(Folder(), ex.Message);
                return null;
            }
        });
    }

    /// <summary>
    /// The games without those whose id Jellyfin knows as a real item (OwnIds.Check, batched, once per build of the game data): such a game is not listed and never answered.
    /// The other ids are remembered as free, so the requests that follow cost no query.
    /// </summary>
    private List<GameEntry> WithoutRealItems(List<GameEntry> games) =>
        WithoutRealItems(games, batch => _libraryManager.GetItemIds(new MediaBrowser.Controller.Entities.InternalItemsQuery { ItemIds = batch.ToArray() }), _data, CheckBudget);

    /// <summary>
    /// <see cref="WithoutRealItems(List{GameEntry})"/> with the question to Jellyfin given (<paramref name="existing"/>: the ids of a batch that are items) and the <paramref name="budget"/> of time;
    /// an incomplete check (a failing or slow database) tells <paramref name="cache"/>, which builds the data again soon.
    /// </summary>
    public static List<GameEntry> WithoutRealItems(List<GameEntry> games, Func<IReadOnlyList<Guid>, IEnumerable<Guid>> existing, GameDataCache cache, TimeSpan budget)
    {
        var result = OwnIds.CheckAll(games.Select(g => Guid.TryParse(g.Id, out var id) ? id : Guid.Empty), existing, budget);
        if (!result.Complete)
        {
            // Not every game was checked: the data is built again soon, and single ids are asked one by one meanwhile (OwnIds.Refused).
            cache.NoteIncomplete();
        }

        var known = result.Known;
        return known.Count == 0 ? games : games.Where(g => !(Guid.TryParse(g.Id, out var id) && known.Contains(id))).ToList();
    }

    // The batches run inside the build lock of the game data (GameDataCache.Get): after this time no further batch is started.
    private static readonly TimeSpan CheckBudget = TimeSpan.FromSeconds(5);

    /// <summary>One warning per changed state, not one per request.</summary>
    private void WarnOnce(string folder, string message)
    {
        var state = folder + "|" + message;
        if (Interlocked.Exchange(ref _lastReadFailure, state) != state)
        {
            _logger.LogWarning("PlayniteBridge: cannot read Playnite in \"{Folder}\": {Message}", folder, message);
        }
    }

    /// <summary>
    /// The game data the request's user may see: with ApplyParentalControls and a user, the base game data (<see cref="Data()"/>) without the games that user's parental controls block
    /// (ParentalDataCache, built once per policy and base data); else the base game data itself.
    /// </summary>
    private GameData Data(ActionExecutingContext context) => DataFor(Parental(context));

    /// <summary>For GamesController: <see cref="Data(ActionExecutingContext)"/> for a request's user (<paramref name="principal"/>, no userId argument).</summary>
    internal GameData DataFor(ClaimsPrincipal principal) => DataFor(ParentalFor(_libraryManager, _userManager, principal, null));

    private GameData DataFor((ParentalPolicy Policy, string[] LibraryTags)? parental)
    {
        var data = Data();
        return parental is (ParentalPolicy policy, string[] libraryTags) ? ParentalData(data, policy, libraryTags) : data;
    }

    /// <summary>The games of <paramref name="data"/> that <paramref name="policy"/> allows (ParentalDataCache, built once per policy and base data).</summary>
#if NET10_0_OR_GREATER
    private GameData ParentalData(GameData data, ParentalPolicy policy, string[] libraryTags)
    {
        // One rating function per request: the metadata country is looked up at most once per build.
        var ratingScore = RatingScore();
        return _parental.Get(data, policy.Key(libraryTags), g => ParentalControl.IsAllowed(g, policy, libraryTags, ratingScore));
    }
#else
    private GameData ParentalData(GameData data, ParentalPolicy policy, string[] libraryTags) =>
        _parental.Get(data, policy.Key(libraryTags), g => ParentalControl.IsAllowed(g, policy, libraryTags, RatingLevel));
#endif

    /// <summary>
    /// For ArtworkPlusLink.IsVisibleTo with ApplyParentalControls on: the games of <paramref name="data"/> the parental control policy of <paramref name="user"/> allows,
    /// with the games library's tags, as <see cref="Data(ActionExecutingContext)"/> gives them to that user's request.
    /// </summary>
    internal GameData ParentalDataFor(User user, GameData data) =>
        ParentalData(data, ParentalControl.PolicyFor(true, user)!,
            (GamesLibrary(_libraryManager) is Guid libraryId ? _libraryManager.GetItemById(libraryId)?.Tags : null) ?? Array.Empty<string>());

    /// <summary>
    /// For the admin page's Scenario Tests (ApplyParentalControls on): every enabled user who may see the games library, with a function that says whether that user's parental control
    /// policy allows a game (the rule of <see cref="ParentalData"/>; one memo per policy key, so users with equal policies cost one evaluation per game). Empty without a games library.
    /// </summary>
    internal IReadOnlyList<ScenarioTests.ScenarioUser> ScenarioUsers()
    {
        if (GamesLibrary(_libraryManager) is not Guid libraryId || _libraryManager.GetItemById(libraryId) is not { } library)
        {
            return Array.Empty<ScenarioTests.ScenarioUser>();
        }

        var libraryTags = library.Tags ?? Array.Empty<string>();
        var memos = new Dictionary<string, Func<GameEntry, bool>>(StringComparer.Ordinal);
#if NET10_0_OR_GREATER
        var ratingScore = RatingScore();
#endif
#if NET10_0_OR_GREATER
        var users = _userManager.GetUsers();
#else
        var users = _userManager.Users;
#endif
        return users.Where(u => !u.HasPermission(PermissionKind.IsDisabled) && library.IsVisible(u)).OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(user =>
            {
                var policy = ParentalPolicy.From(user);
                var key = policy.Key(libraryTags);
                if (!memos.TryGetValue(key, out var allowed))
                {
                    var known = new Dictionary<string, bool>(StringComparer.Ordinal);
#if NET10_0_OR_GREATER
                    allowed = g => known.TryGetValue(g.Id, out var yes) ? yes : known[g.Id] = ParentalControl.IsAllowed(g, policy, libraryTags, ratingScore);
#else
                    allowed = g => known.TryGetValue(g.Id, out var yes) ? yes : known[g.Id] = ParentalControl.IsAllowed(g, policy, libraryTags, RatingLevel);
#endif
                    memos[key] = allowed;
                }

                return new ScenarioTests.ScenarioUser(user.Username, allowed);
            }).ToList();
    }

    /// <summary>For ArtworkPlusLink.IsVisibleTo: the user of <paramref name="userId"/> when the games library exists and that user may see it (<see cref="VisibleUser(ILibraryManager, IUserManager, Guid, Guid)"/>).</summary>
    internal User? VisibleUser(Guid userId) =>
        GamesLibrary(_libraryManager) is Guid libraryId ? VisibleUser(_libraryManager, _userManager, libraryId, userId) : null;

    /// <summary>For ArtworkPlusLink.LibraryBackdrops: the games library id (<see cref="GamesLibrary(ILibraryManager)"/>).</summary>
    internal Guid? GamesLibrary() => GamesLibrary(_libraryManager);

#if NET10_0_OR_GREATER
    /// <summary>
    /// Jellyfin 12.x: the rating score of a form the own table does not know, as BaseItem.IsParentalAllowed asks it for an item of the games library:
    /// ILocalizationManager.GetRatingScore with the country of <see cref="MetadataCountry"/>, looked up once per function and only when a rating needs it.
    /// </summary>
    internal Func<string, ParentalRatingScore?> RatingScore()
    {
        var country = new Lazy<string?>(MetadataCountry);
        return rating => _localization.GetRatingScore(rating, country.Value);
    }

    /// <summary>
    /// The metadata country of an item of the games library as 12.1 BaseItem.GetPreferredMetadataCountryCode finds it: the library's (CollectionFolder) PreferredMetadataCountryCode,
    /// else its LibraryOptions.MetadataCountryCode, else the server's MetadataCountryCode. Read anew on every call, like the policy.
    /// </summary>
    private string? MetadataCountry()
    {
        var library = GamesLibrary(_libraryManager) is Guid libraryId ? _libraryManager.GetItemById(libraryId) : null;
        var country = library?.PreferredMetadataCountryCode;
        if (string.IsNullOrEmpty(country) && library is not null)
        {
            country = _libraryManager.GetLibraryOptions(library)?.MetadataCountryCode;
        }

        return string.IsNullOrEmpty(country) ? _config.Configuration.MetadataCountryCode : country;
    }

    /// <summary>For GlobeLink.Games: a function that gives a game's strictest rating as [score, sub-score] (ParentalControl.Strictest with the same fallback IsAllowed uses); empty texts without one.</summary>
    internal Func<GameEntry, string[]> StrictestRating()
    {
        var ratingScore = RatingScore();
        return game => ParentalControl.Strictest(game, ratingScore) is { } strictest
            ? new[] { strictest.Score.ToString(System.Globalization.CultureInfo.InvariantCulture), strictest.SubScore?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty }
            : new[] { string.Empty, string.Empty };
    }
#else
    private Func<string, int?> RatingLevel => rating => _localization.GetRatingLevel(rating);

    /// <summary>For GlobeLink.Games: a function that gives a game's strictest age in years as [age, ""] (ParentalControl.Strictest with the same fallback IsAllowed uses); empty texts without one.</summary>
    internal Func<GameEntry, string[]> StrictestRating() =>
        game => ParentalControl.Strictest(game, RatingLevel) is int age
            ? new[] { age.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty }
            : new[] { string.Empty, string.Empty };
#endif

    private (ParentalPolicy Policy, string[] LibraryTags)? Parental(ActionExecutingContext context) =>
        ParentalFor(_libraryManager, _userManager, context.HttpContext.User, Arg<Guid?>(context, "userId"));

    /// <summary>
    /// The parental control policy of the request's user (read anew on every request, so a change in the dashboard counts at once) and the tags of the games library; null while
    /// ApplyParentalControls is off, for an API key without a user (Jellyfin restricts no query without a user) and when the user may not see the games library (the caller then
    /// hands the request to Jellyfin anyway).
    /// </summary>
    internal static (ParentalPolicy Policy, string[] LibraryTags)? ParentalFor(ILibraryManager libraryManager, IUserManager userManager, ClaimsPrincipal principal,
        Guid? requestedUserId)
    {
        // Off: the user is not even looked up.
        var apply = Plugin.Instance!.Configuration.ApplyParentalControls;
        if (!apply || VisibleGamesAccess(libraryManager, userManager, principal, requestedUserId) is not (Guid libraryId, var user)
            || ParentalControl.PolicyFor(apply, user) is not ParentalPolicy policy)
        {
            return null;
        }

        return (policy, libraryManager.GetItemById(libraryId)?.Tags ?? Array.Empty<string>());
    }

    private GameHit? FindGame(ActionExecutingContext context, Guid id) => Find(context, id, out _) is { Kind: GameHitKind.Game } hit ? hit : null;

    /// <summary>The hit of an id in the game data the request's user may see (<see cref="Data(ActionExecutingContext)"/>); a blocked game or collection gets null.</summary>
    private GameHit? Find(ActionExecutingContext context, Guid id, out GameData data)
    {
        var hit = Find(id, out data);
        if (hit is null || Parental(context) is null)
        {
            return hit;
        }

        data = Data(context);
        return data.Find(id);
    }

    /// <summary>
    /// The hit of an id and the game data it was found in (GameDataCache.Find): an id unknown in the stored game data gets null at once (no copy listing, no read), so other libraries
    /// pay nothing. Only when the stored data is for another settings object, ShowHidden or an older copy (PlayniteSnapshot.Generation: a sync of this process since), or before
    /// the first build, is it built again and the id looked up there. A known id is looked up again in the current game data.
    /// </summary>
    internal GameHit? Find(Guid id, out GameData data) =>
        _data.Find(id, Plugin.Instance!.Configuration, PlayniteSnapshot.Generation, Data, out data);

    /// <summary>
    /// The games library id, for a collection folder the collection, and the game data a request's parentId stands for; null for any other parentId, none, or a games library the user
    /// may not see. The parentId is compared with the cached games library id and the collection ids (<see cref="Find"/>) before the game data is read and the user is checked.
    /// </summary>
    private (Guid LibraryId, GameCollection? Collection, GameData Data)? ParentFor(ActionExecutingContext context)
    {
        var parentId = Arg<Guid?>(context, "parentId");
        if (parentId is null)
        {
            return null;
        }

        GameCollection? collection = null;
        var data = default(GameData);
        if (!OwnIds.IsGamesLibrary(parentId, GamesLibrary(_libraryManager)))
        {
            if (Find(context, parentId.Value, out data) is not { Kind: GameHitKind.Collection } hit)
            {
                return null;
            }

            collection = hit.Collection;
        }

        return GamesLibrary(context) is Guid libraryId ? (libraryId, collection, data ?? Data(context)) : null;
    }

    /// <summary>The games library id when the request's parentId is that library; the user is checked only then.</summary>
    private Guid? GamesLibraryFor(ActionExecutingContext context)
    {
        var parentId = Arg<Guid?>(context, "parentId");
        return OwnIds.IsGamesLibrary(parentId, GamesLibrary(_libraryManager)) ? GamesLibrary(context) : null;
    }

    private sealed record CachedImages(Configuration.PluginConfiguration Config, string Folder, string CacheSetting, DateTime Created, ImageCache Cache,
        string? Steam, JellyfinFolders Jellyfin);

    private Guid? GamesLibrary(ActionExecutingContext context) =>
        VisibleGamesLibrary(_libraryManager, _userManager, context.HttpContext.User, Arg<Guid?>(context, "userId"));

    /// <summary>
    /// The games library id when the request's user may see it, else null so Jellyfin answers itself. The user is found as in Jellyfin's RequestHelpers.GetUserId: the claim
    /// "Jellyfin-UserId", another <paramref name="requestedUserId"/> only for administrators; an API key without a user counts as allowed (Jellyfin gives it the role Administrator).
    /// "May see" = the library item's IsVisible (blocked/enabled folders, rating).
    /// </summary>
    internal static Guid? VisibleGamesLibrary(ILibraryManager libraryManager, IUserManager userManager, ClaimsPrincipal principal, Guid? requestedUserId) =>
        VisibleGamesAccess(libraryManager, userManager, principal, requestedUserId)?.LibraryId;

    /// <summary>As <see cref="VisibleGamesLibrary"/>, with the user the request counts for (null for an API key without a user).</summary>
    internal static (Guid LibraryId, User? User)? VisibleGamesAccess(ILibraryManager libraryManager, IUserManager userManager, ClaimsPrincipal principal,
        Guid? requestedUserId)
    {
        var libraryId = GamesLibrary(libraryManager);
        if (libraryId is null)
        {
            return null;
        }

        var claim = principal.Claims.FirstOrDefault(c => c.Type.Equals("Jellyfin-UserId", StringComparison.OrdinalIgnoreCase))?.Value;
        var ownId = Guid.TryParse(claim, out var parsed) ? parsed : Guid.Empty;
        var isAdmin = principal.IsInRole("Administrator");
        var userId = requestedUserId is Guid asked && asked != Guid.Empty ? asked : ownId;
        if (userId != ownId && !isAdmin)
        {
            return null;
        }

        if (userId == Guid.Empty)
        {
            return isAdmin ? (libraryId.Value, null) : null;
        }

        return VisibleUser(libraryManager, userManager, libraryId.Value, userId) is User user ? (libraryId.Value, user) : null;
    }

    /// <summary>The user of <paramref name="userId"/> when it exists and may see the games library (the library item's IsVisible); else null.</summary>
    private static User? VisibleUser(ILibraryManager libraryManager, IUserManager userManager, Guid libraryId, Guid userId)
    {
        var user = userManager.GetUserById(userId);
        return user is not null && libraryManager.GetItemById(libraryId)?.IsVisible(user) == true ? user : null;
    }

    /// <summary>
    /// The library named in the settings (<see cref="GamesLibrary(string, DateTime, Func{Guid, MediaBrowser.Controller.Entities.BaseItem?}, Func{IEnumerable{VirtualFolderInfo}}, Func{Guid, bool}, string)"/>); a library that holds real items is not served.
    /// </summary>
    internal static Guid? GamesLibrary(ILibraryManager libraryManager) =>
        GamesLibrary(Plugin.Instance!.Configuration.LibraryName, DateTime.UtcNow, id => libraryManager.GetItemById(id), () => libraryManager.GetVirtualFolders(),
            id => HoldsRealItems(libraryManager, id), Plugin.Instance.Configuration.GamesLibraryId);

    /// <summary>
    /// True when Jellyfin's database holds real items under the library <paramref name="libraryId"/> (its physical folders themselves not counted). The games library is virtual with an
    /// empty folder; a library with real items is somebody else's and is never served as the games library.
    /// </summary>
    public static bool HoldsRealItems(ILibraryManager libraryManager, Guid libraryId)
    {
        // Jellyfin has no item for the library id (just removed, or not loaded yet): without the item the query below would match nothing and the library would count as free,
        // so this cannot be proved either way. The caller treats a throw as "holds" (not served, asked again after NoLibrarySeconds); a library Jellyfin lists always has its item,
        // so an empty games library is served again at the next try. Any other item (a CollectionFolder as always) is queried.
        if (libraryManager.GetItemById(libraryId) is not { } item)
        {
            throw new InvalidOperationException("Jellyfin has no item for the library " + libraryId.ToString("N"));
        }

        var physical = (item as MediaBrowser.Controller.Entities.CollectionFolder)?.PhysicalFolderIds ?? Array.Empty<Guid>();
        var query = new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            ParentId = libraryId,
            Recursive = true,
            Limit = 1,
            ExcludeItemIds = physical.Append(libraryId).ToArray()
        };
        return libraryManager.GetItemList(query).Count > 0;
    }

    /// <summary>
    /// The games library: the library with the id <paramref name="pinnedId"/> when that setting is given (it wins over the name), else the one named <paramref name="name"/>.
    /// The kept library stays while its name stays and Jellyfin's item of the kept id (<paramref name="item"/>) still carries the library's name (case-insensitive) and, as a collection folder,
    /// no other type than movies (a library re-created under the same name and path keeps its id, so its type is checked again; an item without a type counts as unchanged). Otherwise it is
    /// looked up again in <paramref name="folders"/> (<see cref="FindLibrary"/>: a renamed or new library gets another id). A library is served only while <paramref name="holdsRealItems"/>
    /// says it holds no real item of Jellyfin (asked at the lookup and then every <see cref="OwnIds.MemoSeconds"/>; a failing question counts as holding): else one error line, no games
    /// library, and Jellyfin answers everything itself. No games library for the name (none, not a movies library, or refused) is kept for <see cref="NoLibrarySeconds"/> from
    /// <paramref name="now"/>, so a request does not read Jellyfin's library folders each time; another name or id looks up at once.
    /// </summary>
    public static Guid? GamesLibrary(string name, DateTime now, Func<Guid, MediaBrowser.Controller.Entities.BaseItem?> item, Func<IEnumerable<VirtualFolderInfo>> folders,
        Func<Guid, bool>? holdsRealItems = null, string? pinnedId = null)
    {
        var pin = pinnedId?.Trim() ?? string.Empty;
        // The common case, no lock: the kept answer is still valid.
        if (_library is { } kept && Kept(kept, name, pin, now, item, holdsRealItems, out var answer))
        {
            return answer;
        }

        // One request at a time looks the library up or asks the database (single flight): a request that waited sees the answer of the one before it, so one question is asked
        // per check time and a refusal is never overwritten by a request that started its question earlier.
        lock (LibraryLock)
        {
            if (_library is { } again && Kept(again, name, pin, now, item, holdsRealItems, out var latest))
            {
                return latest;
            }

            var cached = _library;
            if (cached is not null && cached.Name == name && cached.Pin == pin && cached.Id is not null
                && item(cached.Id.Value) is MediaBrowser.Controller.Entities.BaseItem library && StillTheLibrary(cached, library))
            {
                // Valid, only the check is due.
                if (!HoldsItems(holdsRealItems!, cached.Id.Value, library.Name))
                {
                    _library = cached with { NextCheck = now.AddSeconds(OwnIds.MemoSeconds) };
                    return cached.Id;
                }

                _library = new CachedLibrary(name, null, now.AddSeconds(NoLibrarySeconds), pin, now, null);
                return null;
            }

            var folder = FindFolder(folders(), name, pin);
            var id = folder is not null && Guid.TryParse(folder.ItemId, out var parsed) ? parsed : (Guid?)null;
            if (id is not null && holdsRealItems is not null && HoldsItems(holdsRealItems, id.Value, folder!.Name))
            {
                id = null;
            }

            _library = new CachedLibrary(name, id, id is null ? now.AddSeconds(NoLibrarySeconds) : DateTime.MaxValue, pin, now.AddSeconds(OwnIds.MemoSeconds), folder?.Name);
            return id;
        }
    }

    private static readonly object LibraryLock = new();

    // True with the kept answer when nothing is due: no library kept for this name for NoLibrarySeconds, or the kept library still carries its name and type and its next check is not due.
    private static bool Kept(CachedLibrary cached, string name, string pin, DateTime now, Func<Guid, MediaBrowser.Controller.Entities.BaseItem?> item,
        Func<Guid, bool>? holdsRealItems, out Guid? answer)
    {
        answer = null;
        if (cached.Name != name || cached.Pin != pin)
        {
            return false;
        }

        if (cached.Id is null)
        {
            return now < cached.Until;
        }

        if (item(cached.Id.Value) is not MediaBrowser.Controller.Entities.BaseItem library || !StillTheLibrary(cached, library)
            || (holdsRealItems is not null && now >= cached.NextCheck))
        {
            return false;
        }

        answer = cached.Id;
        return true;
    }

    // The kept id's item still carries the library's name and, as a collection folder, no other type than movies.
    private static bool StillTheLibrary(CachedLibrary cached, MediaBrowser.Controller.Entities.BaseItem library) =>
        string.Equals(library.Name, cached.FolderName, StringComparison.OrdinalIgnoreCase)
        && !(library is MediaBrowser.Controller.Entities.CollectionFolder { CollectionType: CollectionType type } && type != CollectionType.movies);

    // True when the library holds real items (or this cannot be told); the error line once per library, again after the library was free.
    private static bool HoldsItems(Func<Guid, bool> holdsRealItems, Guid id, string libraryName)
    {
        bool held;
        try
        {
            held = holdsRealItems(id);
        }
        catch (Exception ex)
        {
            if (_refusedLogged != id)
            {
                _refusedLogged = id;
                _libraryLog?.LogError("PlayniteBridge: cannot tell whether the library \"{Library}\" ({Id}) holds real items ({Message}); it is not served as the games library", libraryName, id, ex.Message);
            }

            return true;
        }

        if (held && _refusedLogged != id)
        {
            _refusedLogged = id;
            _libraryLog?.LogError("PlayniteBridge: the library \"{Library}\" ({Id}) holds real items, so it is not served as the games library and Jellyfin answers it itself; the games library must be an empty library", libraryName, id);
        }
        else if (!held)
        {
            _refusedLogged = Guid.Empty;
        }

        return held;
    }

    /// <summary>
    /// The id of the library with the id <paramref name="pinnedId"/> when given (it wins over the name; one that is not a GUID or not among <paramref name="folders"/> gives null,
    /// never the name's library), else the one named <paramref name="name"/> (case-insensitive) among <paramref name="folders"/>. Null if there is none, its ItemId is no GUID
    /// (a library just created) or it is no movies library (<see cref="SupportedLibrary"/>): Jellyfin then answers everything itself.
    /// </summary>
    public static Guid? FindLibrary(IEnumerable<VirtualFolderInfo> folders, string name, string? pinnedId = null) =>
        FindFolder(folders, name, pinnedId?.Trim() ?? string.Empty) is { } folder && Guid.TryParse(folder.ItemId, out var id) ? id : null;

    private static VirtualFolderInfo? FindFolder(IEnumerable<VirtualFolderInfo> folders, string name, string pin)
    {
        var folder = pin.Length > 0
            ? Guid.TryParse(pin, out var pinned) ? folders.FirstOrDefault(f => Guid.TryParse(f.ItemId, out var id) && id == pinned) : null
            : folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        return folder is not null && SupportedLibrary(folder) ? folder : null;
    }

    /// <summary>
    /// Only a movies library can hold the games: its page (movies.html) gives the games the movies tabs, sort and filters. Every other type is refused, a mixed library (no type)
    /// included: the list.html types (boxsets, mixed, homevideos, musicvideos, books, photos) would list the games but without those tabs and with another sort and filter,
    /// and tvshows / music pages ask only for their own item types.
    /// </summary>
    public static bool SupportedLibrary(VirtualFolderInfo folder) => folder.CollectionType == CollectionTypeOptions.movies;

    private sealed record CachedLibrary(string Name, Guid? Id, DateTime Until, string Pin, DateTime NextCheck, string? FolderName);
}
