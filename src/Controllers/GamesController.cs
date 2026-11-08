using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Controllers;

[ApiController]
[Route("PlayniteBridge")]
[Authorize]
public class GamesController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILocalizationManager _localization;
    private readonly IPluginManager _pluginManager;
    private readonly IDtoService _dtoService;
    private readonly VirtualItemsFilter _filter;
    private readonly ILogger<GamesController> _logger;

    public GamesController(ILibraryManager libraryManager, IUserManager userManager, ILocalizationManager localization, IPluginManager pluginManager,
        IDtoService dtoService, VirtualItemsFilter filter, ILogger<GamesController> logger)
    {
        _libraryManager = libraryManager;
        _userManager = userManager;
        _localization = localization;
        _pluginManager = pluginManager;
        _dtoService = dtoService;
        _filter = filter;
        _logger = logger;
    }

    /// <summary>ArtworkPlus's plugin id.</summary>
    public static readonly Guid ArtworkPlusId = Guid.Parse("a32fa765-c917-4c30-a58c-5998015cd046");

    /// <summary>True when one of <paramref name="plugins"/> is ArtworkPlus with the status Active.</summary>
    public static bool ArtworkPlusActive(IEnumerable<(Guid Id, PluginStatus Status)> plugins) =>
        plugins.Any(p => p.Id == ArtworkPlusId && p.Status == PluginStatus.Active);

    /// <summary>VideoOSD Tweaks and Candy's plugin id.</summary>
    public static readonly Guid VideoOsdId = Guid.Parse("468b1980-7a6c-4e45-a129-24825085ece4");

    /// <summary>
    /// True when one of <paramref name="plugins"/> is VideoOSD Tweaks and Candy, active, with its Artwork addon on (the configuration is read by reflection:
    /// <c>Configuration.EnableArtwork</c>). Then VideoOSD draws the game artwork in the player. Any error or missing property counts as not responsible.
    /// </summary>
    public static bool VideoOsdArtworkActive(IEnumerable<(Guid Id, PluginStatus Status, object? Instance)> plugins) =>
        plugins.Any(p => p.Id == VideoOsdId && p.Status == PluginStatus.Active && ArtworkEnabled(p.Instance));

    /// <summary>
    /// Who draws the game pages' artwork: ArtworkPlus while it is active and Settings From (Display) is ArtworkPlus, otherwise Playnite Bridge.
    /// </summary>
    public static bool ArtworkPlusDraws(PluginConfiguration config, bool artworkPlusActive) =>
        artworkPlusActive && config.ArtworkSettingsFrom == ArtworkSettingsSource.ArtworkPlus;

    /// <summary>
    /// Who draws the player artwork, as far as the server can tell: VideoOSD while its Artwork addon is on and Settings From (Player Artwork) is VideoOSD,
    /// otherwise Playnite Bridge. The injector variant of the addon is only visible in the browser (html[data-jvosd-artwork]).
    /// </summary>
    public static bool VideoOsdDraws(PluginConfiguration config, bool videoOsdArtworkOn) =>
        videoOsdArtworkOn && config.OsdArtworkSettingsFrom == PlayerArtworkSettingsSource.VideoOSD;

    private static bool ArtworkEnabled(object? instance)
    {
        try
        {
            var configuration = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
            return configuration?.GetType().GetProperty("EnableArtwork")?.GetValue(configuration) is true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The OSD &amp; Artwork values for the web script (every ArtPoster*, ArtLogoArt*, ArtCharacterArt* and ArtBackdrops* property by name, enums as names).
    /// Null while ArtworkPlus is active, so the script cannot apply a saved value on a game page.
    /// </summary>
    public static Dictionary<string, object>? ClientArtwork(PluginConfiguration config, bool artworkPlusActive) => artworkPlusActive
        ? null
        : typeof(PluginConfiguration).GetProperties()
            .Where(p => p.Name.StartsWith("ArtPoster", StringComparison.Ordinal) || p.Name.StartsWith("ArtLogoArt", StringComparison.Ordinal)
                || p.Name.StartsWith("ArtCharacterArt", StringComparison.Ordinal) || p.Name.StartsWith("ArtBackdrops", StringComparison.Ordinal))
            .ToDictionary(p => p.Name, p => p.PropertyType.IsEnum ? p.GetValue(config)!.ToString()! : p.GetValue(config)!);

    /// <summary>
    /// The Player Artwork values for the web script (every PlayerArt* property by name, enums as names). Null while PlayerArtEnabled is off or while
    /// VideoOSD's Artwork addon is responsible (<see cref="VideoOsdArtworkActive"/>), so the script draws nothing in the player. The browser still checks
    /// html[data-jvosd-artwork] for VideoOSD's injector variant, which the server cannot see.
    /// </summary>
    public static Dictionary<string, object>? ClientPlayerArtwork(PluginConfiguration config, bool videoOsdDraws = false) => !config.PlayerArtEnabled || videoOsdDraws
        ? null
        : typeof(PluginConfiguration).GetProperties()
            .Where(p => p.Name.StartsWith("PlayerArt", StringComparison.Ordinal))
            .ToDictionary(p => p.Name, p => p.PropertyType.IsEnum ? p.GetValue(config)!.ToString()! : p.GetValue(config)!);

    /// <summary>
    /// What the web script needs to tell games views apart and draw version markers: the games library id, GamesLibraryIcon (only with a library id,
    /// see <see cref="LibraryIcon"/>), the collection folder ids (32 hex digits as in Jellyfin's item ids), marker kind and position, the icons per tile id (Markers)
    /// with their tooltips (MarkerTitles), DimUninstalled, DimStrength and the tile ids whose main version is not installed (Uninstalled, empty while DimUninstalled is off),
    /// SeriesDisplay, SourceOrder (source names of the visible games in platform list order, for the filter dialog), SeriesTerm, MediaInfo, SecondaryInfo,
    /// DetailRows and RowOrder. No library, a library the user may not see or a refused Playnite folder gives null and no collections, markers or uninstalled tiles.
    /// With ApplyParentalControls only the games the user may see count. VersionCounts is the number of versions per tile id for the list view
    /// (<see cref="VersionCounts"/>). ArtworkSource is "ArtworkPlus" while ArtworkPlus draws (<see cref="ArtworkPlusDraws"/>), else "PlayniteBridge";
    /// Artwork is null while ArtworkPlus draws. PlayerArtworkSource is the chosen Settings From of Player Artwork ("VideoOSD" or "PlayniteBridge"),
    /// PlayerArtwork is null while Player Artwork is off or VideoOSD draws (<see cref="VideoOsdDraws"/>).
    /// </summary>
    [HttpGet("ClientConfiguration")]
    public ActionResult GetClientConfiguration()
    {
        var config = Plugin.Instance!.Configuration;
        var artworkPlusDraws = ArtworkPlusDraws(config, ArtworkPlusActive(_pluginManager.Plugins.Select(p => (p.Id, p.Manifest.Status))));
        var videoOsdDraws = VideoOsdDraws(config, VideoOsdArtworkActive(_pluginManager.Plugins.Select(p => (p.Id, p.Manifest.Status, (object?)p.Instance))));
        var folder = PlayniteReader.ResolveFolder(config.PlayniteFolder);
        var libraryId = folder is null ? null : VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null);
        try
        {
            var groups = libraryId is null || folder is null || PlayniteSnapshot.CurrentSet() is null
                ? new List<GameGroup>()
                : GameGroups.Group(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), config);
            var marked = groups.Select(g => (Group: g, Icons: GameGroups.MarkerIcons(g, config))).Where(m => m.Icons.Length > 0).ToList();
            return Ok(new
            {
                LibraryId = libraryId?.ToString("N"),
                GamesLibraryIcon = libraryId is null ? null : LibraryIcon(config),
                CollectionIds = GameSuggestions.Collections(groups).Select(c => c.Id.ToString("N")).ToArray(),
                MarkerKind = config.MarkerKind.ToString(),
                MarkerPosition = config.MarkerPosition.ToString(),
                Markers = marked.ToDictionary(m => Guid.Parse(m.Group.Main.Id).ToString("N"), m => m.Icons),
                MarkerTitles = marked.ToDictionary(m => Guid.Parse(m.Group.Main.Id).ToString("N"), m => string.Join(", ", GameGroups.MarkerLabels(m.Group))),
                VersionCounts = VersionCounts(groups, config),
                config.DimUninstalled,
                DimStrength = DimStrength(config),
                Uninstalled = UninstalledTiles(groups, config.DimUninstalled),
                SourceOrder = GameGroups.Sources(groups.SelectMany(g => g.Versions), config.PlatformOrder),
                SeriesDisplay = config.SeriesDisplay.ToString(),
                SeriesTerm = SeriesTermLabel(config),
                MediaInfo = MediaInfoParts(config.MediaInfoItems),
                SecondaryInfo = SecondaryInfoParts(config),
                DetailRows = DetailRowParts(config.DetailRows),
                RowOrder = config.RowOrder,
                config.ShowMoreLikeThis,
                Connections = new
                {
                    config.EnableConnections,
                    Series = config.ConnectionsSeries.ToString(),
                    Media = config.ConnectionsMedia.ToString(),
                    Cast = config.ConnectionsCast.ToString(),
                    config.ConnectionsCustomPictures,
                    Name = config.ConnectionsName.ToString(),
                    OwnName = Clamp40(config.ConnectionsOwnName),
                    MediaName = config.ConnectionsMediaName.ToString(),
                    MediaOwnName = Clamp40(config.ConnectionsMediaOwnName),
                    CastName = config.ConnectionsCastName.ToString(),
                    CastOwnName = Clamp40(config.ConnectionsCastOwnName),
                    config.ConnectionsShowDividers,
                    config.ConnectionsNameSingleGroup,
                    GroupOrder = config.ConnectionsGroupOrder,
                    MediaOrder = config.ConnectionsMediaOrder
                },
                SteamSpecialFeaturesLabel = (config.SteamSpecialFeaturesLabel ?? "").Trim() is { Length: > 40 } label ? label[..40] : (config.SteamSpecialFeaturesLabel ?? "").Trim(),
                config.EnableVideos,
                config.EnableTrailers,
                config.EnableExtras,
                config.EnableSteamSpecialFeatures,
                SteamSpecialsPlacement = config.SteamSpecialsPlacement.ToString(),
                config.SteamSpecialsMerge,
                config.VideosShowDividers,
                config.VideosKeepSlotsTogether,
                config.ShowVideoIcon,
                config.ShowSubtext,
                config.ShowTitle,
                config.DocumentsShowTitle,
                config.DocumentsShowSubtext,
                DocumentsPictureFit = config.DocumentsPictureFit.ToString(),
                config.DocumentsCropLimitPercent,
                config.DocumentsGenerateThumbnails,
                ServerReadsComics = Documents.ServerReadsCbr,
                config.ScreenshotsShowTitle,
                config.ScreenshotsShowSubtext,
                ScreenshotsPictureFit = config.ScreenshotsPictureFit.ToString(),
                config.ScreenshotsCropLimitPercent,
                config.WallpapersShowTitle,
                config.WallpapersShowSubtext,
                WallpapersPictureFit = config.WallpapersPictureFit.ToString(),
                config.WallpapersCropLimitPercent,
                WallpapersTitle = TitleKey(config.WallpapersTitle, "WallpapersAndArtwork", "Wallpapers", "Artwork", "Gallery"),
                WallpapersLabel = Clamp40(config.WallpapersLabel),
                SpecialFeaturesTitle = TitleKey(config.SpecialFeaturesTitle, "SpecialFeatures", "Extras", "BonusMaterial"),
                ScreenshotsTitle = TitleKey(config.ScreenshotsTitle, "Screenshots", "Gameplay", "Gallery"),
                DocumentsTitle = TitleKey(config.DocumentsTitle, "Documents", "Library", "ManualsAndBooks"),
                SoundtrackTitle = TitleKey(config.SoundtrackTitle, "Soundtrack", "Music", "OriginalSoundtrack"),
                SteamSpecialFeaturesTitle = TitleKey(config.SteamSpecialFeaturesTitle, "SteamSpecialFeatures", "SteamVideos", "SteamTrailers"),
                SpecialFeaturesLabel = Clamp40(config.SpecialFeaturesLabel),
                ScreenshotsLabel = Clamp40(config.ScreenshotsLabel),
                DocumentsLabel = Clamp40(config.DocumentsLabel),
                SoundtrackLabel = Clamp40(config.SoundtrackLabel),
                ThemeMusic = config.EnableThemeMusic ? new
                {
                    PlayMode = config.ThemePlayMode.ToString(),
                    OnVersionSwitch = config.ThemeOnVersionSwitch.ToString(),
                    Volume = Math.Clamp(config.ThemeVolumePercent, 0, 100),
                    DelayedStart = Math.Clamp(config.ThemeDelayedStartSeconds, 0, 600),
                    FadeIn = Math.Clamp(config.ThemeFadeInSeconds, 0, 60),
                    FadeOut = Math.Clamp(config.ThemeFadeOutSeconds, 0, 60),
                    Loop = config.ThemeLoopMusic,
                    DelayedStartEvery = config.ThemeDelayedStartEverySong,
                    FadeEvery = config.ThemeFadeEverySong
                } : null,
                ThemeVideos = config.EnableThemeVideos ? new
                {
                    PlayMode = config.ThemeVideoPlayMode.ToString(),
                    OnVersionSwitch = config.ThemeVideoOnVersionSwitch.ToString(),
                    Volume = Math.Clamp(config.ThemeVideoVolumePercent, 0, 100),
                    DelayedStart = Math.Clamp(config.ThemeVideoDelayedStartSeconds, 0, 600),
                    Loop = config.ThemeLoopVideo,
                    DelayedStartEvery = config.ThemeDelayedStartEveryVideo,
                    KeepSourcesTogether = config.ThemeVideoKeepSourcesTogether
                } : null,
                VersionArtwork = VersionArtwork(config),
                ArtworkSource = artworkPlusDraws ? "ArtworkPlus" : "PlayniteBridge",
                Artwork = ClientArtwork(config, artworkPlusDraws),
                PlayerArtworkSource = config.OsdArtworkSettingsFrom.ToString(),
                PlayerArtwork = ClientPlayerArtwork(config, videoOsdDraws)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the collections from \"{Folder}\"", folder);
            return StatusCode(500);
        }
    }

    /// <summary>The "Artwork on Version Change" table for the web script: per artwork type, whether it follows the picked version and whether it stays when that version has none.</summary>
    public static Dictionary<string, object> VersionArtwork(PluginConfiguration config) =>
        new[] { "Cover", "Backdrop", "Logo", "Clearart", "Characterart" }.ToDictionary(
            t => t,
            t => (object)new
            {
                follow = (bool)typeof(PluginConfiguration).GetProperty("VersionFollow" + t)!.GetValue(config)!,
                keep = (bool)typeof(PluginConfiguration).GetProperty("VersionKeep" + t)!.GetValue(config)!
            });

    /// <summary>
    /// Number of versions per tile id (main version of each group, 32 hex) that the web script shows as Jellyfin's number in the list view while tiles show icons.
    /// Only groups with two or more versions; none with the number marker (Jellyfin draws MediaSourceCount there itself) or with MarkerPosition Off.
    /// </summary>
    public static Dictionary<string, int> VersionCounts(IEnumerable<GameGroup> groups, PluginConfiguration config) =>
        config.MarkerKind == VersionMarkerKind.Icons && config.MarkerPosition != VersionMarkerPosition.Off
            ? groups.Where(g => g.Versions.Count > 1).ToDictionary(g => Guid.Parse(g.Main.Id).ToString("N"), g => g.Versions.Count)
            : new Dictionary<string, int>();

    /// <summary>The Games library icon choice as the web script gets it: one of Default, Controller, Gamepad, Handheld, Tile; anything else is Default.</summary>
    public static string LibraryIcon(PluginConfiguration config) => new[] { "Controller", "Gamepad", "Handheld", "Tile" }.FirstOrDefault(v => v == config.GamesLibraryIcon) ?? "Default";

    /// <summary>How much the web script darkens an uninstalled tile: DimStrengthPercent within 0 to 100.</summary>
    public static int DimStrength(PluginConfiguration config) => Math.Clamp(config.DimStrengthPercent, 0, 100);

    /// <summary>The tile ids (main version of each group, 32 hex) whose main version is not installed; none when <paramref name="dim"/> is off.</summary>
    public static string[] UninstalledTiles(IEnumerable<GameGroup> groups, bool dim) =>
        dim ? groups.Where(g => !g.Main.Installed).Select(g => Guid.Parse(g.Main.Id).ToString("N")).ToArray() : Array.Empty<string>();

    /// <summary>The label of the game page's series row: null = automatic (the script's word in the UI language); "Own" = SeriesTermOwn trimmed, null when empty.</summary>
    public static string? SeriesTermLabel(PluginConfiguration config)
    {
        var term = config.SeriesTerm == "Own" ? config.SeriesTermOwn.Trim() : config.SeriesTerm;
        return string.IsNullOrEmpty(term) ? null : term;
    }

    // A typed title of the Connections rows: trimmed, at most 40 characters.
    private static string Clamp40(string? text) => (text ?? "").Trim() is { Length: > 40 } clipped ? clipped[..40] : (text ?? "").Trim();

    /// <summary>A title setting as the client gets it: one of the preset keys or Own; anything else (also empty) is the default, an empty key.</summary>
    private static string TitleKey(string? key, params string[] presets) => key == "Own" || (key is not null && Array.IndexOf(presets, key) > 0) ? key : "";

    /// <summary>The parts of the first info line in MediaInfoItems (keys trimmed, case ignored, unknown keys ignored), always in Jellyfin's order.</summary>
    public static string[] MediaInfoParts(string items)
    {
        var keys = items.Split(',').Select(k => k.Trim()).ToList();
        return new[] { "Year", "Playtime", "AgeRating", "CommunityRating", "CriticRating" }
            .Where(part => keys.Contains(part, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>The detail rows of the game page in DetailRows (keys trimmed, case ignored, unknown keys ignored), always in the fixed row order.</summary>
    public static string[] DetailRowParts(string rows)
    {
        var keys = rows.Split(',').Select(k => k.Trim()).ToList();
        return new[] { "Series", "Genres", "Developer", "Publisher", "Platform", "Features", "Size", "Status" }
            .Where(part => keys.Contains(part, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>The dates of the second info line that are on, in the chosen order; empty when both are off.</summary>
    public static string[] SecondaryInfoParts(PluginConfiguration config)
    {
        var parts = new List<string>();
        if (config.ShowLastPlayed)
        {
            parts.Add("LastPlayed");
        }

        if (config.ShowAdded)
        {
            parts.Insert(config.SecondaryInfoOrder == SecondaryInfoOrderMode.AddedLastPlayed ? 0 : parts.Count, "Added");
        }

        return parts.ToArray();
    }

    /// <summary>A store icon (embedded, Web\icons): fixed keys only, anonymous because &lt;img&gt; cannot send auth.</summary>
    [HttpGet("icons/{key}")]
    [AllowAnonymous]
    public ActionResult GetIcon(string key)
    {
        if (!GameGroups.IconKeys.Contains(key, StringComparer.Ordinal))
        {
            return NotFound();
        }

        var file = key == "custom" ? "custom.svg" : key + ".png";
        var stream = typeof(GamesController).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.icons." + file);
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, key == "custom" ? "image/svg+xml" : "image/png");
    }

    /// <summary>A link icon (embedded, Web\link-icons): fixed keys only, anonymous because &lt;img&gt; cannot send auth.</summary>
    [HttpGet("link-icons/{key}")]
    [AllowAnonymous]
    public ActionResult GetLinkIcon(string key)
    {
        if (!GameLinks.IconKeys.Contains(key, StringComparer.Ordinal))
        {
            return NotFound();
        }

        // MSBuild turns the hyphen of the folder name into "_" in the resource name.
        var stream = typeof(GamesController).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.link_icons." + key + ".svg");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, "image/svg+xml");
    }

    /// <summary>The logo of a detail page button (embedded, Web\action-logos): fixed keys only, anonymous because a CSS mask sends no auth.</summary>
    [HttpGet("action-logos/{key}")]
    [AllowAnonymous]
    public ActionResult GetActionLogo(string key)
    {
        if (!GameStart.LogoKeys.Contains(key, StringComparer.Ordinal))
        {
            return NotFound();
        }

        // MSBuild turns the hyphen of the folder name into "_" in the resource name.
        var stream = typeof(GamesController).Assembly.GetManifestResourceStream(typeof(Plugin).Namespace + ".Web.action_logos." + key + ".svg");
        if (stream is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(stream, "image/svg+xml");
    }

    /// <summary>
    /// The characterart images of a game as [{ Index, Tag }] in order, Tag from the file's time and length. 204 like games/{id} (no Playnite data copy, a user who may
    /// not see the games library, an unknown or blocked game) and for a game without game folder or while no game page can show characterart. Writes nothing.
    /// </summary>
    [HttpGet("games/{id}/characterart")]
    public ActionResult GetCharacterArt(Guid id, [FromQuery] int player = 0)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NoContent();
        }

        try
        {
            var mayOpen = VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is not null;
            _filter.Find(id, out var data);
            Func<GameEntry, bool>? allowed = VirtualItemsFilter.ParentalFor(_libraryManager, _userManager, User, null) is (ParentalPolicy policy, string[] libraryTags)
#if NET10_0_OR_GREATER
                ? game => ParentalControl.IsAllowed(game, policy, libraryTags, _filter.RatingScore())
#else
                ? game => ParentalControl.IsAllowed(game, policy, libraryTags, rating => _localization.GetRatingLevel(rating))
#endif
                : null;
            // player=1: the player artwork asks, and its CharacterArt layer does not depend on the game page's CharacterArt settings.
            var files = CharacterArtFiles.Visible(data, id, mayOpen, allowed, _filter.Images().GameFolder, player == 1 || CharacterArtFiles.Wanted(config), CharacterArtFiles.Name(config));
            return files is null ? NoContent() : Ok(files.Select((path, index) => new { Index = index, Tag = ImageCache.Tag(path) }).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not list the characterart of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The backdrops of a collection page for one visit, as [{ tag, index }] in final order (<see cref="VirtualItemsFilter.CollectionBackdropList"/>: own pictures and the
    /// games' backdrops by Source, Images and Game Order; Mixed is unordered and the page shuffles it; at most 100). The page loads Items/{id}/Images/Backdrop/{index}?tag={tag}.
    /// 204 without a Playnite data copy, for a user who may not see the games library, for an id that is no visible collection, while Show on Collections is off
    /// or when nothing is found (the page then keeps Jellyfin's own backdrop). Games blocked by the user's parental controls are left out.
    /// </summary>
    [HttpGet("collections/{id}/backdrops")]
    public ActionResult GetCollectionBackdrops(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.ArtBackdropsCollectionShow || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var data = _filter.DataFor(User);
            if (data.Find(id) is not { Kind: GameHitKind.Collection } hit)
            {
                return NoContent();
            }

            var list = _filter.CollectionBackdropList(data, hit.Collection!, config, new Random());
            return list.Count == 0 ? NoContent() : Ok(list.Select(b => new { tag = b.Tag, index = b.Index }).ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not list the backdrops of collection {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The theme songs of a game for the plugin's own theme player (Enable Theme Music): [{id, name}] in the order the settings give (the shown version's own,
    /// else the group's). <paramref name="playing"/> is the name of the song already playing, which stays chosen when the group's fallback has it.
    /// 204 without Enable Theme Music, without the games library for the user, for an unknown or blocked game. The audio itself is streamed by
    /// Jellyfin's Audio routes, which this plugin answers for those ids.
    /// </summary>
    [HttpGet("games/{id}/themes")]
    public ActionResult GetThemes(Guid id, [FromQuery] string? playing = null, [FromQuery] string? kind = null)
    {
        var config = Plugin.Instance!.Configuration;
        var video = string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase);
        if (!(video ? config.EnableThemeVideos : config.EnableThemeMusic) || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NoContent();
        }

        try
        {
            if (VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
            {
                return NoContent();
            }

            // The data this user may see: a blocked version gives no song and a blocked id answers like an unknown one (204).
            return _filter.ThemeSongsOfPage(_filter.DataFor(User), id, playing, video) is { } songs
                ? Ok(songs.Select(e => new { Id = e.Id.ToString("N"), e.Name, Source = e.Kind.ToString() }).ToArray())
                : NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not list the themes of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// One characterart image of a game: entry <paramref name="index"/> of the list the server computes (CharacterArtFiles.Served), never a file name from the request.
    /// Anonymous because &lt;img&gt; cannot send auth, so it answers only with the file's own tag (ImageCache.Tag), which only the list endpoint (visibility and
    /// parental controls checked) hands out. 404 for anything else. Cached for a year.
    /// </summary>
    [HttpGet("games/{id}/characterart/{index}")]
    [AllowAnonymous]
    public ActionResult GetCharacterArtFile(Guid id, int index, [FromQuery] int player = 0)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NotFound();
        }

        try
        {
            _filter.Find(id, out var data);
            if (CharacterArtFiles.Served(data, id, index, _filter.Images().GameFolder, player == 1 || CharacterArtFiles.Wanted(config), CharacterArtFiles.Name(config)) is not string path
                || !string.Equals(Request.Query["tag"], ImageCache.Tag(path), StringComparison.Ordinal))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=" + TimeSpan.FromDays(365).TotalSeconds;
            return PhysicalFile(path, "image/png");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not serve characterart {Index} of game {Id}", index, id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The single pictures a visible game has that Jellyfin has no image type for, as [{ Type, Tag }] (Type: square, box3d, boxfront, boxback, boxspine, icon;
    /// Tag from the file's time and length). The player artwork loads each as games/{id}/pictures/{type}?tag={tag}. 204 like games/{id} and for a game without any. Writes nothing.
    /// </summary>
    [HttpGet("games/{id}/pictures")]
    public ActionResult GetPictures(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var data = _filter.DataFor(User);
            var images = _filter.Images();
            var list = GamePictures.Types.Select(type => (Type: type, Tag: ImageCache.Tag(GamePictures.Picture(data, images, id, type)))).Where(p => p.Tag is not null)
                .Select(p => new { p.Type, p.Tag }).ToArray();
            return list.Length == 0 ? NoContent() : Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not list the pictures of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// One single picture of a game: <paramref name="type"/> of <see cref="GamePictures.Types"/>, the file the server finds in the game's own folder (never a name from the request).
    /// Anonymous because &lt;img&gt; cannot send auth, like Jellyfin's own image routes. A given ?tag= must match the file's own; without one the picture is
    /// revalidated on every request. 404 for anything else.
    /// </summary>
    [HttpGet("games/{id}/pictures/{type}")]
    [AllowAnonymous]
    public ActionResult GetPictureFile(Guid id, string type)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NotFound();
        }

        try
        {
            _filter.Find(id, out var data);
            return GamePictures.Picture(data, _filter.Images(), id, type) is string path ? ServePicture(path) : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not serve picture {Type} of game {Id}", type, id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The discs of a visible game as [{ Index, Tag }] in order (the plain names disc, cdart, discart and the chosen name, then their numbered forms 1 to 20;
    /// identical pictures once; index 0 is the disc Items/{id}/Images/Disc shows). The player artwork loads each as games/{id}/discs/{index}?tag={tag}.
    /// 204 like <see cref="GetPictures"/> and for a game without a disc.
    /// </summary>
    [HttpGet("games/{id}/discs")]
    public ActionResult GetDiscs(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var list = GamePictures.Discs(_filter.DataFor(User), _filter.Images(), id).Select((path, index) => new { Index = index, Tag = ImageCache.Tag(path) }).Where(d => d.Tag is not null).ToArray();
            return list.Length == 0 ? NoContent() : Ok(list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not list the discs of game {Id}", id);
            return StatusCode(500);
        }
    }

    // The file of a picture route: 404 when the file is gone or a given ?tag= is not its own tag; with the tag a year of cache, without it a revalidation every time.
    // An image is never run as a page: sandbox policy and no content sniffing (an .svg file included).
    private ActionResult ServePicture(string path)
    {
        if (ImageCache.Tag(path) is not string tag)
        {
            return NotFound();
        }

        var asked = Request.Query["tag"].ToString();
        if (asked.Length > 0 && !string.Equals(asked, tag, StringComparison.Ordinal))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = asked.Length > 0 ? "public, max-age=" + TimeSpan.FromDays(365).TotalSeconds : "no-cache";
        Response.Headers["Content-Security-Policy"] = "sandbox";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, GamePictures.ContentType(path));
    }

    /// <summary>Disc number <paramref name="index"/> of <see cref="GetDiscs"/>, anonymous like <see cref="GetPictureFile"/> (a given tag must match). 404 for anything else.</summary>
    [HttpGet("games/{id}/discs/{index}")]
    [AllowAnonymous]
    public ActionResult GetDiscFile(Guid id, int index)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NotFound();
        }

        try
        {
            _filter.Find(id, out var data);
            return GamePictures.Disc(data, _filter.Images(), id, index) is string path ? ServePicture(path) : NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not serve disc {Index} of game {Id}", index, id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The detail page extras of a visible game with its group's versions (main version first, one entry for a single version), the version shown first
    /// (the game itself, or for a twin left out of its group the version kept in its place), the group's sources (row "Source") and the cover marker
    /// (DetailMarkerIcons, tooltip as the tile's; no icons, no marker). 204 for any other id, for a game blocked by parental controls and for a user who
    /// may not see the games library (the web client asks on every detail page).
    /// </summary>
    [HttpGet("games/{id}")]
    public ActionResult GetGame(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        var folder = PlayniteReader.ResolveFolder(config.PlayniteFolder);
        // Asked on every detail page: no Playnite data copy means no game, not an error (same cheap check as the filter).
        if (folder is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            // The asked entry gives the top-level fields; a twin left out of its group shows the group with the version kept in its place.
            var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
            var game = OwnIds.Game(visible, id);
            var groups = GameGroups.Group(visible, config);
            var found = game is null ? null : GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id);
            var detail = game is null ? null : PlayniteReader.ReadDetail(id);
            if (game is null || found is null || detail is null)
            {
                return NoContent();
            }

            var (group, shown) = found.Value;
            var isAdmin = IsAdmin();
            var isLocal = IsLocal();
            var versions = group.Versions.Select(v =>
            {
                var versionDetail = v == game ? detail : PlayniteReader.ReadDetail(Guid.Parse(v.Id));
                var actions = GameStart.Decide(v, config, LauncherPaths.Find(v.StoreKey, folder), isAdmin, isLocal);
                return new
                {
                    id = Guid.Parse(v.Id).ToString("N"),
                    label = GameGroups.OptionLabel(group, v),
                    store = v.StoreKey,
                    storeName = v.StoreName,
                    installed = v.Installed,
                    installSize = versionDetail?.InstallSize,
                    completionStatus = StatusLink(versionDetail?.CompletionStatus),
                    lastPlayed = v.LastActivity,
                    playtime = v.Playtime > 0 ? v.Playtime * TimeSpan.TicksPerSecond : (long?)null,
                    links = GameLinks.OneIconPerService(GameLinks.Arrange(versionDetail?.Links ?? Array.Empty<MediaBrowser.Model.Providers.ExternalUrl>(), config.LinkHostLimiter, config.LinkHosts, config.LinkHostOrder), config)
                        .Select(l => new { name = l.Name, url = l.Url, icon = GameLinks.Icon(l, config) }).ToArray(),
                    // Tiles per row of this version's screenshots (empty = no area); the web client reserves the area's height from it.
                    screenshots = _filter.ScreenshotRowsOf(v.Id).Select(r => r.Count).ToArray(),
                    documents = _filter.DocumentRowsOf(v.Id).Select(r => r.Count).ToArray(),
                    wallpapers = _filter.WallpaperRowsOf(v.Id).Select(r => r.Count).ToArray(),
                    // Tracks of this version's soundtrack (its own folder, else the group's as Extras Of Other Versions allows); the list itself is asked when the row opens.
                    soundtrack = _filter.SoundtrackOfVersion(group, v.Id).Count,
                    // remote: the request is not from the PC, so a Server action runs on the PC and the button says so.
                    actions = actions.Select(a => new
                    {
                        kind = a.Kind.ToString(),
                        via = a.Via.ToString(),
                        uri = a.Via == StartVia.Link ? a.Action.Uri : null,
                        logo = a.Logo,
                        remote = !isLocal
                    }).ToArray()
                };
            }).ToArray();

            var collections = GameSuggestions.Collections(groups);
            return Ok(new
            {
                versions,
                version = Guid.Parse(shown.Id).ToString("N"),
                description = detail.Description,
                series = GameSuggestions.SeriesLinks(game, collections)
                    .Select(s => new { name = s.Name, collectionId = s.CollectionId?.ToString("N") }).ToArray(),
                features = detail.Features.Select(f => new { name = f, filterId = VirtualIds.Filter(GameQuery.FeatureFilter(f)).ToString("N") }).ToArray(),
                sources = GameGroups.Sources(group.Versions, config.PlatformOrder)
                    .Select(s => new { name = s, filterId = VirtualIds.Filter(GameQuery.SourceFilter(s)).ToString("N") }).ToArray(),
                marker = new
                {
                    icons = GameGroups.DetailMarkerIcons(group, config),
                    number = GameGroups.DetailMarkerNumber(group, config),
                    position = config.DetailMarkerPosition.ToString(),
                    title = string.Join(", ", GameGroups.MarkerLabels(group))
                },
                installSize = detail.InstallSize,
                completionStatus = StatusLink(detail.CompletionStatus),
                lastPlayed = game.LastActivity,
                added = game.Added
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The videos of a visible game's Steam store page (Steam Special Features) as Jellyfin items (VirtualItemsFilter.SteamItemDto), played by Jellyfin's own player.
    /// Empty with Enable Videos or Enable Steam Special Features off, for a game without a Steam AppID or a page without playable videos; 204 for an unknown game.
    /// Sync Playnite prefetches them; a game without an entry is asked from Steam once.
    /// </summary>
    [HttpGet("games/{id}/steamvideos")]
    public async Task<ActionResult> GetSteamVideos(Guid id, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        GameEntry? game;
        try
        {
            game = OwnIds.Game(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read game {Id}", id);
            return StatusCode(500);
        }

        if (game is null)
        {
            return NoContent();
        }

        if (!VirtualItemsFilter.SteamEnabled())
        {
            return Ok(Array.Empty<object>());
        }

        // The clips of the shown game's AppID and, since the Special Features row merges them, those of its other versions (Special Features of Other Versions).
        var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
        var found = GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id);
        var appIds = found is null ? new List<string>() : VirtualItemsFilter.SteamAppIds(found.Value.Group, game.Id, config).ToList();
        if (appIds.Count == 0 && SteamVideos.AppIdOf(game) is string own && own.Length > 0)
        {
            appIds.Add(own);
        }

        foreach (var appId in appIds)
        {
            await SteamVideos.Get(appId, _logger, cancellationToken).ConfigureAwait(false);
        }

        return Ok(await _filter.SteamDtos(appIds, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// The rows of the Videos area of a visible game: { native, steamOwn, shape, rows } as <see cref="VirtualItemsFilter.VideoRowsOf"/> (the cards of Jellyfin's merged row
    /// with the row each came from, and the own rows with their first 60 items; row and from ask for that row's next ones). Same guards as steamvideos: 204 for an
    /// unknown or blocked game and for a user who may not see the games library; empty with Enable Videos off.
    /// </summary>
    [HttpGet("games/{id}/videorows")]
    public async Task<ActionResult> GetVideoRows(Guid id, [FromQuery] string? row = null, [FromQuery] int from = 0)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesAccess(_libraryManager, _userManager, User, null) is not { } access)
        {
            return NoContent();
        }

        try
        {
            var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
            var game = OwnIds.Game(visible, id);
            if (game is null)
            {
                return NoContent();
            }

            if (!config.EnableVideos || GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id) is not { } found)
            {
                return Ok(new { native = Array.Empty<object>(), steamOwn = false, shape = "landscape", rows = Array.Empty<object>() });
            }

            var canDownload = VirtualItemsFilter.MayDownload(access.User);
            return Ok(await _filter.VideoRowsOf(found.Group, game, canDownload, row, from, access.User?.Id).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the video rows of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The Related Media and Actors rows of a visible game from its game.nfo (GameNfo; the game's own folder first, else the other versions of its group in group order):
    /// { related: [BaseItemDto], actors: [{ item: BaseItemDto, role }] } in the file's display order. Everything is resolved again on every call: an actor by name (GetPerson),
    /// tmdb = a movie, tvdb = a series, tmdbset = the box set of a movie with that TMDb collection id. Whatever Jellyfin does not know or the user may not see (IsVisible)
    /// is left out. 204 for an unknown or blocked game, a user who may not see the games library and a game without a usable game.nfo.
    /// </summary>
    [HttpGet("games/{id}/nfo")]
    public ActionResult GetNfo(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesAccess(_libraryManager, _userManager, User, null) is not (Guid, var user))
        {
            return NoContent();
        }

        try
        {
            var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
            var game = OwnIds.Game(visible, id);
            var found = game is null ? null : GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id);
            if (game is null || found is null)
            {
                return NoContent();
            }

            if (NfoSource(game) is not var (nfoFolder, nfo))
            {
                return NoContent();
            }

            // The movie and box set lists of a tmdbset entry are loaded once per request, the box set once per collection id.
            var movies = new Lazy<List<BaseItem>>(() => _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Movie }, Recursive = true }).ToList());
            var boxSets = new Lazy<List<BoxSet>>(() => _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.BoxSet }, Recursive = true }).OfType<BoxSet>().ToList());
            var series = new Lazy<List<BaseItem>>(() => _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = new[] { BaseItemKind.Series }, Recursive = true }).ToList());
            var sets = new Dictionary<string, BoxSet?>();
            var related = nfo.Media.Select(m => ResolveMedia(m, user, movies, series, boxSets, sets)).Where(item => item is not null).Select(item => item!).DistinctBy(item => item.Id).ToList();
            var people = nfo.Actors.Select((a, index) => (Person: _libraryManager.GetPerson(a.Name), a.Role, Index: index, Image: config.ConnectionsCustomPictures ? GameNfo.ActorImage(nfoFolder, a.Name) : null))
                .Where(a => a.Person is not null && (user is null || a.Person.IsVisible(user))).ToList();
            var options = new DtoOptions(false) { Fields = new[] { ItemFields.PrimaryImageAspectRatio }, ImageTypes = new[] { ImageType.Primary }, ImageTypeLimit = 1, EnableUserData = false };
            var relatedDtos = _dtoService.GetBaseItemDtos(related, options, user);
            var actorDtos = _dtoService.GetBaseItemDtos(people.Select(a => (BaseItem)a.Person!).ToList(), options, user);
            return Ok(new
            {
                related = relatedDtos,
                actors = people.Select(p => (Dto: actorDtos.FirstOrDefault(d => d.Id == p.Person!.Id), p.Role, p.Index, p.Image)).Where(p => p.Dto is not null)
                    .Select(p => new { item = p.Dto, role = p.Role, picture = p.Image is null ? null : new { index = p.Index, tag = ImageCache.Tag(p.Image) } }).ToArray()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the game.nfo of game {Id}", id);
            return StatusCode(500);
        }
    }

    // The game.nfo of a game: its own folder's file and content, never another version's (every version is one game with its own metadata).
    private (string Folder, GameNfoContent Content)? NfoSource(GameEntry game) =>
        _filter.Images().GameFolder(game) is string folder && GameNfo.Read(Path.Combine(folder, GameNfo.FileName), _logger) is { } content ? (folder, content) : null;

    /// <summary>
    /// The picture of an actor of a game's game.nfo from the ".actors" folder next to it (GameNfo.ActorImage): entry <paramref name="index"/> of the nfo's actor list,
    /// never a file name from the request. Anonymous because &lt;img&gt; cannot send auth, so it answers only with the file's own tag (ImageCache.Tag), which only
    /// games/{id}/nfo (visibility and parental controls checked) hands out. 404 for anything else and while Use Custom Pictures is off. Cached for a year.
    /// </summary>
    [HttpGet("games/{id}/actors/{index}")]
    [AllowAnonymous]
    public ActionResult GetActorPicture(Guid id, int index)
    {
        var config = Plugin.Instance!.Configuration;
        // A request without a tag of the right shape (12 hex characters) costs nothing.
        var asked = Request.Query["tag"].ToString();
        if (index < 0 || index >= GameNfo.MaxActors || !config.ConnectionsCustomPictures || asked.Length != 12 || !asked.All(Uri.IsHexDigit) || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NotFound();
        }

        try
        {
            // The lookup (library, groups, nfo, .actors folder) is kept for 10 seconds per game and entry, so a flood of requests with a wrong tag does it only once.
            var cacheKey = (id, index);
            if (ActorLookups.Count > 512)
            {
                ActorLookups.Clear();
            }

            if (!ActorLookups.TryGetValue(cacheKey, out var looked) || DateTime.UtcNow - looked.At > TimeSpan.FromSeconds(10))
            {
                // Ids of no game are not kept (a flood of random ids would only fill the cache): after 100 of them within 10 seconds the lookup is refused until the window is over.
                lock (UnknownIds)
                {
                    if (DateTime.UtcNow - UnknownSince > TimeSpan.FromSeconds(10))
                    {
                        UnknownSince = DateTime.UtcNow;
                        UnknownCount = 0;
                    }
                    else if (UnknownCount >= 100)
                    {
                        return NotFound();
                    }
                }

                var visible = PlayniteReader.ReadVisibleGames(config.ShowHidden);
                var game = OwnIds.Game(visible, id);
                var found = game is null ? null : GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id);
                string? located = game is null || found is null || NfoSource(game) is not var (folder, nfo) || index < 0 || index >= nfo.Actors.Count
                    ? null
                    : GameNfo.ActorImage(folder, nfo.Actors[index].Name);
                looked = (DateTime.UtcNow, located, located is null ? null : ImageCache.Tag(located));
                if (game is null)
                {
                    lock (UnknownIds)
                    {
                        UnknownCount++;
                    }
                }
                else
                {
                    ActorLookups[cacheKey] = looked;
                }
            }

            if (looked.Path is not string path || !string.Equals(asked, looked.Tag, StringComparison.Ordinal))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "public, max-age=" + TimeSpan.FromDays(365).TotalSeconds;
            return PhysicalFile(path, GameNfo.ImageType(path));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not serve actor picture {Index} of game {Id}", index, id);
            return StatusCode(500);
        }
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid, int), (DateTime At, string? Path, string? Tag)> ActorLookups = new();
    private static readonly object UnknownIds = new();
    private static DateTime UnknownSince;
    private static int UnknownCount;

    // A media entry of a game.nfo as the item Jellyfin has for it, fresh from the library; null when there is none or the user may not see it.
    // HasAnyProviderId is a substring match in 10.10.7 (Tmdb=105 also finds Tmdb=1050) and ignores TmdbCollection, so the id is compared again here.
    // A TMDb collection is reached through a movie that carries its id and the box set that links this movie (BoxSet.ContainsLinkedChildByItemId).
    // An entry without an id is found by name: a box set by its exact name (case ignored), a movie or series by "<Name> (<ProductionYear>)", or by the name alone
    // when it already ends in " (<year>)". Several visible matches: the entry is skipped and logged, never guessed.
    private BaseItem? ResolveMedia(NfoMedia media, User? user, Lazy<List<BaseItem>> allMovies, Lazy<List<BaseItem>> allSeries, Lazy<List<BoxSet>> allBoxSets, Dictionary<string, BoxSet?> sets)
    {
        bool Visible(BaseItem item) => user is null || item.IsVisible(user);
        if (media.Id.Length == 0)
        {
            List<BaseItem> candidates = media.Kind switch
            {
                "tmdbset" => allBoxSets.Value.Where(b => string.Equals(b.Name, media.Name, StringComparison.OrdinalIgnoreCase) && Visible(b)).Cast<BaseItem>().ToList(),
                _ => (media.Kind == "tmdb" ? allMovies : allSeries).Value.Where(i => string.Equals(NameWithYear(i), media.Name, StringComparison.OrdinalIgnoreCase) && Visible(i)).ToList()
            };
            if (candidates.Count > 1)
            {
                _logger.LogInformation("PlayniteBridge: game.nfo entry \"{Name}\" matches {Count} items, skipped", media.Name, candidates.Count);
            }

            return candidates.Count == 1 ? candidates[0] : null;
        }

        switch (media.Kind)
        {
            case "tmdb":
            case "tvdb":
                var movie = media.Kind == "tmdb";
                return _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = new[] { movie ? BaseItemKind.Movie : BaseItemKind.Series },
                    HasAnyProviderId = new Dictionary<string, string> { [movie ? "Tmdb" : "Tvdb"] = media.Id },
                    Recursive = true
                }).FirstOrDefault(i => i.GetProviderId(movie ? MediaBrowser.Model.Entities.MetadataProvider.Tmdb : MediaBrowser.Model.Entities.MetadataProvider.Tvdb) == media.Id && Visible(i));
            default:
                if (!sets.TryGetValue(media.Id, out var set))
                {
                    var movies = allMovies.Value.Where(i => i.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.TmdbCollection) == media.Id).ToList();
                    set = movies.Count == 0 ? null : allBoxSets.Value.FirstOrDefault(b => movies.Any(m => b.ContainsLinkedChildByItemId(m.Id)));
                    sets[media.Id] = set;
                }

                return set is not null && Visible(set) ? set : null;
        }
    }

    // "<Name> (<ProductionYear>)"; the name alone when it already ends in " (<year>)" or the item has no year.
    private static string NameWithYear(BaseItem item) =>
        item.ProductionYear is not int year || System.Text.RegularExpressions.Regex.IsMatch(item.Name, @" \(\d{4}\)$") ? item.Name : $"{item.Name} ({year})";

    /// <summary>
    /// The soundtrack of a visible game as Jellyfin Audio items in play order (VirtualItemsFilter.SoundtrackDtos). Files are streamed and pictures served by Jellyfin's
    /// own Audio and Image routes, which this plugin answers for those ids. 204 for an unknown or blocked game, a user who may not see the games library,
    /// Enable Soundtrack off and a game without a playable soundtrack.
    /// </summary>
    [HttpGet("games/{id}/soundtrack")]
    public async Task<ActionResult> GetSoundtrack(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.EnableSoundtrack || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
            var game = OwnIds.Game(visible, id);
            var found = game is null ? null : GameGroups.Find(GameGroups.DetailGroups(visible, config), game.Id);
            if (game is null || found is null)
            {
                return NoContent();
            }

            var items = await _filter.SoundtrackDtos(_filter.SoundtrackOfVersion(found.Value.Group, game.Id)).ConfigureAwait(false);
            return items.Count == 0 ? NoContent() : Ok(items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the soundtrack of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The documents of a visible game (its own version's folder only): { seed, rows: [{ row, key, label, count, slotNames, items: [{ id, tag, slot, name, kind, path }] }] },
    /// like the screenshots, with each row's first 60 tiles (the next ones by row and from). Each item is a Book item of the plugin (VirtualItemsFilter.DocumentDto)
    /// that Jellyfin Web's reader for the file type opens.
    /// </summary>
    [HttpGet("games/{id}/documents")]
    public ActionResult GetDocuments(Guid id, [FromQuery] int? row = null, [FromQuery] int from = 0, [FromQuery] int? seed = null)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var game = OwnIds.Game(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), id);
            if (game is null)
            {
                return NoContent();
            }

            var shortId = GameExtras.ShortIdOf(game.Id);
            var useSeed = Documents.SeedFor(config, seed);
            var slotNames = Documents.Slots(config).ToDictionary(s => s.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), s => new { key = s.Key, label = s.Label });
            var rows = _filter.DocumentRowsOf(game.Id).Where(r => row is null || r.Row == row).Select(r =>
            {
                var files = _filter.DocumentFilesOf(game.Id, r.Row, useSeed);
                var divided = r.Row == Documents.AllRows && config.DocumentsShowDividers && config.DocumentsKeepSlotsTogether && files.Select(f => f.Slot).Distinct().Count() > 1;
                return new
                {
                    row = r.Row,
                    key = r.Key,
                    label = r.Label,
                    count = r.Count,
                    slotNames = divided ? slotNames : null,
                    names = slotNames,
                    items = files.Skip(Math.Max(row is null ? 0 : from, 0)).Take(Screenshots.FirstTiles).Select(f => new
                    {
                        id = Documents.ItemId(shortId, f.Slot, useSeed, f.Hash).ToString("N"),
                        tag = Documents.Tag(f),
                        slot = f.Slot,
                        name = System.IO.Path.GetFileNameWithoutExtension(f.FileName),
                        kind = Documents.Kind(f),
                        size = f.Length,
                        path = "documents/" + f.FileName
                    }).ToArray()
                };
            }).ToArray();
            return Ok(new { seed = useSeed, rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the documents of game {Id}", id);
            return StatusCode(500);
        }
    }

    private static readonly System.Threading.SemaphoreSlim ThumbGate = new(2, 2);

    /// <summary>
    /// A browser reports that it could not draw page 1 of a document (password, damaged, rejected picture). The server remembers it per file state (path, time, length)
    /// for 7 days (<see cref="Documents.FailureRetry"/>) and leaves the document out of the pending list meanwhile. Same rules as the store route: any user with the download
    /// right, a PDF/CBR of a game the user may see; 204 (off), 403, 404, 204 when remembered. No body is read; nothing from the request becomes a path.
    /// </summary>
    [HttpPost("documents/{id}/thumbnail-failed")]
    public ActionResult PostDocumentThumbnailFailed(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.DocumentsGenerateThumbnails || !Documents.Enabled(config) || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NoContent();
        }

        if (VirtualItemsFilter.VisibleGamesAccess(_libraryManager, _userManager, User, null) is not { } access || !VirtualItemsFilter.MayDownload(access.User))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var allowed = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)).Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet(StringComparer.Ordinal);
            if (_filter.DocumentOfId(id, allowed.Contains) is not (DocumentFile file, _) || !Documents.BrowserThumbKind(file))
            {
                return NotFound();
            }

            Documents.MarkFailed(file, Documents.CoverDirectory);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not remember a document that cannot be drawn");
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The documents Sync Extras marked as lacking a thumbnail, for an administrator's browser to work off: { total, items: [{ id, tag, name, kind, size }] }, at most 25 per call.
    /// Entries that have a thumbnail now or whose file is gone are left out. 204 when there is none or Generate Thumbnails or Documents is off; 403 for a user who
    /// is no administrator or may not download. No path is exposed.
    /// </summary>
    [HttpGet("documents/pending")]
    public ActionResult GetPendingThumbnails()
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.DocumentsGenerateThumbnails || !Documents.Enabled(config) || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NoContent();
        }

        if (!IsAdmin() || VirtualItemsFilter.VisibleGamesAccess(_libraryManager, _userManager, User, null) is not { } access || !VirtualItemsFilter.MayDownload(access.User))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var known = PlayniteReader.ReadVisibleGames(config.ShowHidden).Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet(StringComparer.Ordinal);
            var items = new List<object>();
            var total = 0;
            foreach (var entry in DocumentThumbs.ReadPending())
            {
                if (!Guid.TryParseExact(entry.Id, "N", out var id) || _filter.DocumentOfId(id, known.Contains) is not (DocumentFile file, _) || !Documents.BrowserThumbKind(file) || Documents.HasThumb(file) || Documents.HasFailure(file))
                {
                    continue;
                }

                total++;
                if (items.Count < 25)
                {
                    items.Add(new { id = entry.Id, tag = Documents.Tag(file), name = entry.Name, kind = Documents.Kind(file), size = file.Length });
                }
            }

            return items.Count == 0 ? NoContent() : Ok(new { total, items });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the list of documents without a thumbnail");
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Stores the page-1 thumbnail a browser drew for a document (body: the JPEG, Content-Type image/jpeg). Any authenticated user with the download right; never over an existing thumbnail.
    /// Answers: 204 (Generate Thumbnails or Documents off, no library), 403 (no download right), 415 (not image/jpeg), 413 (over 512 KB), 404 (no such PDF/CBR document of a
    /// game the user may see, or a comic over the size limit), 400 (not a thumbnail-sized JPEG), 409 (it exists), 429 (busy), 201 { tag }. Nothing from the request becomes a path.
    /// </summary>
    [HttpPost("documents/{id}/thumbnail")]
    public async Task<ActionResult> PostDocumentThumbnail(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (!config.DocumentsGenerateThumbnails || !Documents.Enabled(config) || PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null)
        {
            return NoContent();
        }

        if (VirtualItemsFilter.VisibleGamesAccess(_libraryManager, _userManager, User, null) is not { } access || !VirtualItemsFilter.MayDownload(access.User))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!string.Equals(Request.ContentType?.Split(';')[0].Trim(), "image/jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (Request.ContentLength is long declared && (declared < 1 || declared > Documents.MaxThumbBytes))
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        try
        {
            var allowed = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)).Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet(StringComparer.Ordinal);
            if (_filter.DocumentOfId(id, allowed.Contains) is not (DocumentFile file, _) || !Documents.BrowserThumbKind(file))
            {
                return NotFound();
            }

            // Never trust the header alone: at most the limit plus one byte is read.
            using var body = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await Request.Body.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            {
                if (body.Length + read > Documents.MaxThumbBytes)
                {
                    return StatusCode(StatusCodes.Status413PayloadTooLarge);
                }

                body.Write(buffer, 0, read);
            }

            var jpeg = body.ToArray();
            if (!Documents.ValidThumbJpeg(jpeg))
            {
                return BadRequest();
            }

            if (!await ThumbGate.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests);
            }

            try
            {
                if (System.IO.File.Exists(Documents.ThumbFile(file, Documents.CoverDirectory)) || !Documents.StoreThumb(file, jpeg, Documents.CoverDirectory))
                {
                    return Conflict(new { exists = true });
                }
            }
            finally
            {
                ThumbGate.Release();
            }

            _logger.LogInformation("PlayniteBridge: {User} stored the thumbnail of the document \"{File}\"", User.Identity?.Name, file.FileName);
            return StatusCode(StatusCodes.Status201Created, new { tag = Documents.Tag(file) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not store a document thumbnail");
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The screenshots of a visible game (its own folder only): { seed, rows: [{ row, key, label, shape, count, items: [{ id, tag }] }] }, shape = landscape, square or portrait
    /// (the Tile Shape setting; with Auto the median ratio of the row's first tiles). Each row carries its first <see cref="Screenshots.FirstTiles"/> tiles in the shown order;
    /// <paramref name="row"/> and <paramref name="from"/> ask for that row's next tiles. Each item is a Photo item of the plugin (VirtualItemsFilter.ScreenshotDto)
    /// that Jellyfin's own viewer opens. The folders are read here, not with the game page. key is the predefined label to translate (null: label is the typed text).
    /// With Random, <paramref name="seed"/> keeps the order of rows asked later (and the viewer's list, whose ids carry it) the same. 204 for an unknown or blocked game
    /// and for a user who may not see the games library; no rows without screenshots.
    /// </summary>
    [HttpGet("games/{id}/screenshots")]
    public ActionResult GetScreenshots(Guid id, [FromQuery] int? row = null, [FromQuery] int from = 0, [FromQuery] int? seed = null)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var game = OwnIds.Game(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), id);
            if (game is null)
            {
                return NoContent();
            }

            var shortId = GameExtras.ShortIdOf(game.Id);
            var useSeed = Screenshots.SeedFor(config, seed);
            // Show Dividers: only the merged row with the slots together and more than one slot in it gets the slot names (key/label as the rows have them) and the slot of each tile.
            var slotNames = Screenshots.Slots(config).ToDictionary(s => s.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), s => new { key = s.Key, label = s.Label });
            var rows = _filter.ScreenshotRowsOf(game.Id).Where(r => row is null || r.Row == row).Select(r =>
            {
                var files = _filter.ScreenshotFilesOf(game.Id, r.Row, useSeed);
                var divided = r.Row == Screenshots.AllRows && config.ScreenshotsShowDividers && config.ScreenshotsKeepSlotsTogether && files.Select(f => f.Slot).Distinct().Count() > 1;
                return new
                {
                    row = r.Row,
                    key = r.Key,
                    label = r.Label,
                    shape = _filter.RowShape(config.ScreenshotsTileShape, files.Take(Screenshots.FirstTiles).Select(f => (f.Path, Screenshots.Tag(f)))),
                    count = r.Count,
                    slotNames = divided ? slotNames : null,
                    names = slotNames,
                    items = files.Skip(Math.Max(row is null ? 0 : from, 0)).Take(Screenshots.FirstTiles)
                        .Select(f => new { id = Screenshots.ItemId(shortId, f.Slot, useSeed, f.Hash).ToString("N"), tag = Screenshots.Tag(f), slot = f.Slot, name = System.IO.Path.GetFileNameWithoutExtension(f.FileName) }).ToArray()
                };
            }).ToArray();
            return Ok(new { seed = useSeed, rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the screenshots of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The wallpapers and artwork of a visible game, like <see cref="GetScreenshots"/>: { seed, rows: [{ row, key, label, shape, count, items: [{ id, tag, slot, name }] }] }.
    /// Pictures of any shape share a row. names maps a slot index to { key, label }; slotNames only for a merged row with dividers.
    /// </summary>
    [HttpGet("games/{id}/wallpapers")]
    public ActionResult GetWallpapers(Guid id, [FromQuery] int? row = null, [FromQuery] int from = 0, [FromQuery] int? seed = null)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        try
        {
            var game = OwnIds.Game(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), id);
            if (game is null)
            {
                return NoContent();
            }

            var shortId = GameExtras.ShortIdOf(game.Id);
            var useSeed = Wallpapers.SeedFor(config, seed);
            var slotNames = Wallpapers.Slots(config).ToDictionary(s => s.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), s => new { key = s.Key, label = s.Label });
            var rows = _filter.WallpaperRowsOf(game.Id).Where(r => row is null || r.Row == row).Select(r =>
            {
                var files = _filter.WallpaperFilesOf(game.Id, r.Row, useSeed);
                var divided = r.Row == Wallpapers.AllRows && config.WallpapersShowDividers && config.WallpapersKeepSlotsTogether && files.Select(f => f.Slot).Distinct().Count() > 1;
                return new
                {
                    row = r.Row,
                    key = r.Key,
                    label = r.Label,
                    shape = _filter.RowShape(config.WallpapersTileShape, files.Take(Wallpapers.FirstTiles).Select(f => (f.Path, Wallpapers.Tag(f)))),
                    count = r.Count,
                    slotNames = divided ? slotNames : null,
                    names = slotNames,
                    items = files.Skip(Math.Max(row is null ? 0 : from, 0)).Take(Wallpapers.FirstTiles)
                        .Select(f => new { id = Wallpapers.ItemId(shortId, f.Slot, useSeed, f.Hash).ToString("N"), tag = Wallpapers.Tag(f), slot = f.Slot, name = System.IO.Path.GetFileNameWithoutExtension(f.FileName) }).ToArray()
                };
            }).ToArray();
            return Ok(new { seed = useSeed, rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the wallpapers of game {Id}", id);
            return StatusCode(500);
        }
    }

    /// <summary>
    /// The game a played extra (a local extra or a Steam Special Feature) belongs to, for the player artwork when the game page did not list it in this tab
    /// (for example after a reload on the player page): { id } = the game id ("N"); null for any other id or a game the user may not see; 204 without Playnite or a visible games library.
    /// </summary>
    [HttpGet("extras/{id}/game")]
    public ActionResult GetExtraGame(Guid id)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        string? game;
        try
        {
            var visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
            game = _filter.ExtraGameOf(id, visible, GameGroups.Group(visible, config));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the game of extra {Id}", id);
            return StatusCode(500);
        }

        return Ok(new { id = game is null ? null : Guid.Parse(game).ToString("N") });
    }

    /// <summary>
    /// Reported by the web client when a Steam Special Feature failed in the player: the AppID's list is asked from Steam again (SteamVideos.Refetch: at most once per AppID
    /// in 6 hours, replaced only by a success:true answer). { refetched } = the list was replaced; 404 for an id that is no Steam Special Feature of a game the user may see
    /// or with a setting off; 204 without Playnite or a visible games library.
    /// </summary>
    [HttpPost("steamvideos/refetch/{itemId}")]
    public async Task<ActionResult> RefetchSteamVideos(Guid itemId, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance!.Configuration;
        if (PlayniteReader.ResolveFolder(config.PlayniteFolder) is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NoContent();
        }

        IReadOnlyList<GameEntry> visible;
        try
        {
            visible = AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read the games for the Steam video {Id}", itemId);
            return StatusCode(500);
        }

        if (!VirtualItemsFilter.SteamEnabled() || OwnIds.Steam(itemId) is not SteamVideoItem item || !visible.Any(g => SteamVideos.AppIdOf(g) == item.AppId))
        {
            return NotFound();
        }

        return Ok(new { refetched = await SteamVideos.Refetch(item.AppId, DateTime.UtcNow, _logger, cancellationToken).ConfigureAwait(false) });
    }

    // A completion status with the id of its filter entry (the link of the game page), null for none.
    private static object? StatusLink(string? status) =>
        status is null ? null : new { name = status, filterId = VirtualIds.Filter(GameQuery.StatusFilter(status)).ToString("N") };

    /// <summary>
    /// Starts the action <paramref name="name"/> (a GameStart.StartActionKind) of a visible game on this PC, only when the game's buttons hold it Via Server (GameStart.Pick);
    /// only the id and the name are used from the request. 404: no visible game (or blocked by parental controls) or no such button; 403: the button exists only with the
    /// permission; 409: the browser opens the link itself; 500: refused or failed.
    /// </summary>
    [HttpPost("games/{id}/action/{name}")]
    public ActionResult RunAction(Guid id, string name)
    {
        var config = Plugin.Instance!.Configuration;
        var folder = PlayniteReader.ResolveFolder(config.PlayniteFolder);
        // A user who cannot see the games library cannot start or install its games either.
        if (folder is null || PlayniteSnapshot.CurrentSet() is null
            || VirtualItemsFilter.VisibleGamesLibrary(_libraryManager, _userManager, User, null) is null)
        {
            return NotFound();
        }

        GameEntry? game;
        try
        {
            game = OwnIds.Game(AllowedGames(PlayniteReader.ReadVisibleGames(config.ShowHidden)), id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not read game {Id}", id);
            return StatusCode(500);
        }

        if (game is null)
        {
            return NotFound();
        }

        var (status, picked) = GameStart.Pick(game, config, LauncherPaths.Find(game.StoreKey, folder), IsAdmin(), IsLocal(), name);
        if (picked is null)
        {
            return StatusCode(status);
        }

        var action = picked.Action;
        var info = GameStart.ToStartInfo(action);
        if (info is null)
        {
            _logger.LogError("PlayniteBridge: refused to start {Target} for game {Id}", action.Target, id);
            return StatusCode(500);
        }

        try
        {
            using var process = Process.Start(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PlayniteBridge: could not start {Target} for game {Id}", action.Target, id);
            return StatusCode(500);
        }

        _logger.LogInformation("PlayniteBridge: {User} started {Kind} of game {Id} with {Target}", User.Identity?.Name, picked.Kind, id, action.Target);
        return NoContent();
    }

    // The games the user's parental controls allow (ApplyParentalControls, the filter's rule), before grouping; all of them while it is off.
    private IReadOnlyList<GameEntry> AllowedGames(IReadOnlyList<GameEntry> games) =>
        VirtualItemsFilter.ParentalFor(_libraryManager, _userManager, User, null) is (ParentalPolicy policy, string[] libraryTags)
#if NET10_0_OR_GREATER
            ? ParentalControl.Filter(games, policy, libraryTags, _filter.RatingScore())
#else
            ? ParentalControl.Filter(games, policy, libraryTags, rating => _localization.GetRatingLevel(rating))
#endif
            : games;

    // Jellyfin gives the role "Administrator" to administrators and API keys; Jellyfin.Api is not a package of this plugin, so the role name is written here.
    private bool IsAdmin() => User.IsInRole("Administrator");

    // A request from the PC itself and not through a reverse proxy (GameStart.IsLocal).
    private bool IsLocal() => GameStart.IsLocal(HttpContext.Connection.RemoteIpAddress, Request.Headers.Keys);
}
