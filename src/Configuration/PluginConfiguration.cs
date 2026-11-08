using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlayniteBridge.Configuration;

/// <summary>Where the Curator puts the links folder of a game.</summary>
public enum CuratorPlace
{
    Metadata,
    Extras,
    Both
}

/// <summary>Which artwork the plugin copies into the game folders: every image (served from there), or none (every image read in place).</summary>
public enum ArtworkCopyMode
{
    All,
    Nothing
}

/// <summary>Where a game's backdrop comes from.</summary>
/// <summary>Where a picture comes from; the order of "then" is the order the sources are tried in; Off = the plugin fetches none (a file the user places still shows).</summary>
public enum ImageSourceChoice
{
    Playnite,
    Steam,
    SteamThenPlaynite,
    PlayniteThenSteam,
    Off
}

/// <summary>Where a game's banner or thumb comes from; Off = no Steam copy, a file in the game folder is still shown.</summary>
public enum OptionalImageSource
{
    Off,
    Steam
}

/// <summary>
/// Where a game's logo comes from; Off = no copy, a file in the game folder is still shown. ExtraMetadata = the Logo.png that the Playnite
/// extension Extra Metadata Loader keeps in &lt;Playnite folder&gt;\ExtraMetadata\games\&lt;game id&gt;\, read only. The two Then values try
/// the first source and, when no version of the group has its file, the second (only x, only y, x then y, y then x).
/// </summary>
public enum LogoImageSource
{
    Off,
    Steam,
    ExtraMetadata,
    ExtraMetadataThenSteam,
    SteamThenExtraMetadata
}

/// <summary>What Sync Artwork does with the copies of a kind: Update replaces a copy whose source changed (and fills what is missing), Keep only fills what is missing. A page shows the file that is there.</summary>
public enum ArtworkMode
{
    Update,
    Keep
}

/// <summary>The image a list view row shows (pbIcon request), in order; the cover is always the last fallback.</summary>
public enum ListImageMode
{
    Icon,
    IconThenSquare,
    Square,
    SquareThenIcon
}

/// <summary>Screenshots: the name of a slot's row. A predefined key is shown in Jellyfin's UI language, Own is the typed text.</summary>
public enum ScreenshotLabel
{
    Title,
    TitleScreenshots,
    Select,
    SelectScreenshots,
    Gameplay,
    GameplayScreenshots,
    GameOver,
    GameOverScreenshots,
    HighScores,
    HighScoresScreenshots,
    Own
}

/// <summary>Documents: the name of a slot's row. A predefined key is shown in Jellyfin's UI language, Own is the typed text.</summary>
public enum DocumentLabel
{
    Manuals,
    Guides,
    Maps,
    ReferenceCards,
    Books,
    ArtBooks,
    Comics,
    Magazines,
    Articles,
    Booklets,
    Merchandise,
    Own
}

/// <summary>Connections: where a group (series, media, cast) shows on a game page: its own row, the shared Connections row, or not at all.</summary>
public enum ConnectionsShow
{
    OwnRow,
    ConnectionsRow,
    Off
}

/// <summary>Connections: the name of the shared row. A predefined key is shown in Jellyfin's UI language, Own is the typed text.</summary>
public enum ConnectionsRowName
{
    Connections,
    Linked,
    Universe,
    Related,
    Own
}

/// <summary>Connections: the title of the related media group. A predefined key is shown in Jellyfin's UI language, Own is the typed text.</summary>
public enum ConnectionsMediaName
{
    RelatedMedia,
    Related,
    MoreFromTheSeries,
    Own
}

/// <summary>Connections: the title of the cast group. CastAndCrew is Jellyfin's own word, Own is the typed text.</summary>
public enum ConnectionsCastName
{
    CastAndCrew,
    Characters,
    Own
}

/// <summary>Wallpapers &amp; Artwork: the name of a slot's row. A predefined key is shown in Jellyfin's UI language, Own is the typed text.</summary>
public enum WallpaperLabel
{
    Wallpapers,
    FanArt,
    ConceptArt,
    Posters,
    BoxArt,
    Renders,
    Logos,
    Icons,
    Own
}

/// <summary>The slot areas (Screenshots, Wallpapers &amp; Artwork, Documents): one row per slot, the ticked slots always merged into one row, or merged only while at least two ticked slots have files.</summary>
public enum ScreenshotDisplay
{
    OneRowPerSlot,
    Merged,
    Adaptive
}

/// <summary>Screenshots: the order inside a row (Date = the file's last write time).</summary>
public enum ScreenshotSort
{
    NameAscending,
    NameDescending,
    DateAscending,
    DateDescending,
    Random
}

/// <summary>Screenshots, Wallpapers &amp; Artwork: the shape of the tiles of a row. Landscape (16:9) and Portrait (2:3) are fixed; Auto takes Jellyfin's rule over the pictures of the row (median of their ratios: portrait, square or landscape).</summary>
public enum TileShape
{
    Landscape,
    Portrait,
    Auto,
    Square
}

/// <summary>Documents, Screenshots, Wallpapers &amp; Artwork: how a tile shows its picture (Adaptive = Crop while the cut loses at most the Crop Limit, else Blur Fill; Crop = cut to the tile; Blur Fill = the whole picture on a blurred, darkened copy of itself).</summary>
public enum PictureFit
{
    Adaptive,
    Crop,
    BlurFill
}

/// <summary>Which versions of a game page's group give their videos: only the shown version, or its own first and then those of every other version (same names once).</summary>
public enum OtherVersionsMode
{
    AllVersions,
    ThisVersionOnly
}

/// <summary>Where the merged Steam Special Features stand in Jellyfin's Special Features row: before or after the local special features.</summary>
public enum SteamSpecialsPlacement
{
    BeforeLocal,
    AfterLocal
}

/// <summary>Theme music: only the shown version's themes, or, when it has none, those of the group.</summary>
public enum ThemeSourceMode
{
    FallBackToGroup,
    ThisVersionOnly
}

/// <summary>Theme videos with several sources on: the first source (in the user's order) that has videos, or the videos of all of them one after the other.</summary>
public enum ThemeSourcesMode
{
    FirstAvailable,
    AllTogether
}

/// <summary>Theme music with several themes: the first only, one at random, all in order, or all shuffled.</summary>
public enum ThemePlayMode
{
    FirstOnly,
    RandomOne,
    AllInOrder,
    AllShuffled
}

/// <summary>Where a theme song or video starts: at its beginning, or at a random point between two percentages of its length.</summary>
public enum ThemeStartPosition
{
    Beginning,
    Random
}

/// <summary>Theme videos with several videos: the first only, all in order, or all shuffled.</summary>
public enum ThemeVideoPlayMode
{
    FirstOnly,
    AllInOrder,
    AllShuffled
}

/// <summary>Theme music on a version switch of the game page: look for the new version's theme, or keep the playing one.</summary>
public enum ThemeSwitchMode
{
    LookForNewTheme,
    KeepPlaying
}

/// <summary>How a tile shows that a game has several versions: store icons or Jellyfin's own number.</summary>
public enum VersionMarkerKind
{
    Icons,
    Number,
    Off
}

/// <summary>Corner of the version marker on a tile; Off = no marker.</summary>
public enum VersionMarkerPosition
{
    Off,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>Which tiles show the version marker: only those of games with several versions, or every game.</summary>
public enum MarkerScope
{
    MultipleVersions,
    AllGames
}

/// <summary>Which games show the version marker on the cover of their detail page; Off = none.</summary>
/// <summary>When the marker on the cover of the detail page shows: only while the version dropdown offers several versions, or always.</summary>
public enum DetailMarkerShowMode
{
    OffersVersions,
    Always
}

/// <summary>Which game's cover is the poster of a collection, among its games that have a cover.</summary>
public enum CollectionPosterOrder
{
    EarliestRelease,
    LatestRelease,
    FirstByName,
    NewestAdded,
    HighestScore
}

/// <summary>Where the backdrops of a collection page come from: its own pictures, its games' backdrops, own then games, or all of them mixed.</summary>
public enum CollectionBackdropSource
{
    Own,
    Games,
    OwnThenGames,
    Mixed
}

/// <summary>How many pictures of a block a collection page shows: the first one or all of them.</summary>
public enum CollectionImageCount
{
    Single,
    Multiple
}

/// <summary>The order of the games of a collection page: by release date, shuffled once per visit, or drawn with repeats.</summary>
public enum CollectionGameOrder
{
    ReleaseAscending,
    ReleaseDescending,
    Shuffle,
    Random
}

/// <summary>Order that picks a group's main version: installed first, then last played, then the platform list (each order keeps the named steps).</summary>
public enum MainVersionOrder
{
    InstalledLastPlayedPlatform,
    InstalledPlatform,
    LastPlayedPlatform,
    Platform,
    LastPlayedInstalledPlatform
}

/// <summary>Who may have the server start a game on the PC: nobody (only the browser link on the PC), administrators, every user.</summary>
public enum RemoteStartPermission
{
    Off,
    AdminsOnly,
    AllUsers
}

/// <summary>How the game page shows the links of the limited hosts: as text or as icons.</summary>
public enum LinkDisplayMode
{
    Text,
    Icon
}

/// <summary>How the game page shows a game's series: the row "Series", a row of collection tiles (as Jellyfin 12.1), or both.</summary>
public enum SeriesDisplayMode
{
    Text,
    Tiles,
    Both,
    Off
}

/// <summary>Which games count as played (check mark, played filters): none, every game with playtime or a last activity, or those Playnite marks Beaten or Completed.</summary>
public enum PlayedMarkMode
{
    Off,
    Started,
    Completed
}

/// <summary>Order of the two dates of the game page's second info line.</summary>
public enum SecondaryInfoOrderMode
{
    LastPlayedAdded,
    AddedLastPlayed
}

/// <summary>Order of the three tile rows of the game page: special features, collections, more like this.</summary>
/// <summary>Where a poster smaller than 100 % sits inside the space of the full-size poster; Default = Jellyfin's own place.</summary>
public enum ArtPosterAnchorMode
{
    Default,
    TopLeft,
    TopCenter,
    TopRight,
    CenterLeft,
    Center,
    CenterRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

/// <summary>The first stage of the logo slot of a game page: Jellyfin's logo, the game's Art image, its characterart files, or nothing.</summary>
public enum ArtLogoArtSourceKind
{
    VanillaLogo,
    Clearart,
    Characterart,
    Hide
}

/// <summary>A later stage of the logo slot, used when the stage before has no image; None = the slot stays empty.</summary>
public enum ArtLogoArtFallbackKind
{
    None,
    VanillaLogo,
    Clearart,
    Characterart
}

/// <summary>The side of a characterart box its size is given for; the other side follows the greatest value or the image.</summary>
public enum ArtScaleMode
{
    Height,
    Width
}

/// <summary>The side of a characterart box the image keeps while the box has a greatest size.</summary>
public enum ArtAlignMode
{
    Center,
    Left,
    Right
}

/// <summary>Order of the characterart images: file order, shuffled without repeats per round, or random draws with repeats.</summary>
public enum ArtOrderMode
{
    Sequential,
    Shuffle,
    Random
}

/// <summary>Corner of a game page that shows the characterart.</summary>
public enum ArtCharacterArtPositionKind
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>Which item a player artwork layer takes its image from: the playing extra and then its game, only the extra, or only the game.</summary>
public enum PlayerArtSource
{
    Default,
    Extra,
    Game
}

/// <summary>Horizontal place of a player artwork layer: at the left or right end of the control bar, or centred in the window.</summary>
public enum PlayerArtHorizontal
{
    Left,
    Center,
    Right
}

/// <summary>Vertical place of a player artwork layer: below the OSD header, above the control bar, or centred between them.</summary>
public enum PlayerArtVertical
{
    Top,
    Center,
    Bottom
}

/// <summary>Whose settings draw the artwork of game pages (Artwork tab, Display): Playnite Bridge's own, or ArtworkPlus's (only while it is installed).</summary>
public enum ArtworkSettingsSource
{
    PlayniteBridge,
    ArtworkPlus
}

/// <summary>Whose settings draw the game artwork in the video player: Playnite Bridge's own, or VideoOSD's Artwork addon (only while it is on).</summary>
public enum PlayerArtworkSettingsSource
{
    PlayniteBridge,
    VideoOSD
}

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>A shallow copy (every setting is a value or an immutable string), for a calculation with changed settings.</summary>
    public PluginConfiguration Clone() => (PluginConfiguration)MemberwiseClone();

    /// <summary>Playnite data folder (contains library\games.db). Empty = %AppData%\Playnite.</summary>
    public string PlayniteFolder { get; set; } = string.Empty;

    /// <summary>Name of the Jellyfin library that shows the games (case-insensitive).</summary>
    public string LibraryName { get; set; } = "Games";

    /// <summary>The id of the Jellyfin library that shows the games (32 hex digits as the dashboard shows it). Empty = the library named LibraryName; when set it wins over the name.</summary>
    public string GamesLibraryId { get; set; } = string.Empty;

    /// <summary>The icon the Games library shows in Jellyfin's menus: Default (Jellyfin's movie icon), Controller, Gamepad, Handheld or Tile; anything else counts as Default.</summary>
    public string GamesLibraryIcon { get; set; } = "Controller";

    /// <summary>Also show games that are hidden in Playnite.</summary>
    public bool ShowHidden { get; set; }

    /// <summary>Hide games from each user as that user's Jellyfin parental control settings hide movies.</summary>
    public bool ApplyParentalControls { get; set; }

    /// <summary>Show games that are only licensed (not installed) greyed out.</summary>
    public bool DimUninstalled { get; set; }

    /// <summary>How much DimUninstalled darkens a tile, 0 to 100 percent (50 = half).</summary>
    public int DimStrengthPercent { get; set; } = 50;

    /// <summary>How the game page shows the series: the row "Series", the collection tiles above "More like this", or both.</summary>
    public SeriesDisplayMode SeriesDisplay { get; set; } = SeriesDisplayMode.Tiles;

    /// <summary>Library View: which image a list view row shows, in order (the cover is always the last fallback).</summary>
    public ListImageMode ListImage { get; set; } = ListImageMode.Icon;

    /// <summary>Which games count as played: the check mark of their tiles and the played and unplayed filters.</summary>
    public PlayedMarkMode PlayedMark { get; set; } = PlayedMarkMode.Started;

    /// <summary>Label of the game page's series row: empty = automatic (the UI language's word), "Own" = SeriesTermOwn, else the chosen word.</summary>
    public string SeriesTerm { get; set; } = string.Empty;

    /// <summary>The own word of the series row, used while SeriesTerm is "Own".</summary>
    public string SeriesTermOwn { get; set; } = string.Empty;

    /// <summary>Parts of the info line under the game page's title, comma-separated: year, playtime, agerating, communityrating, criticrating.</summary>
    public string MediaInfoItems { get; set; } = "year,playtime,agerating,communityrating,criticrating";

    /// <summary>Detail rows of the game page that are shown, comma-separated: series, genres, developer, publisher, platform, features, size, status.</summary>
    public string DetailRows { get; set; } = "series,genres,developer,publisher,platform,features,size,status";

    /// <summary>Show the date the game was last played in the second info line.</summary>
    public bool ShowLastPlayed { get; set; }

    /// <summary>Show the date the game was added to Playnite in the second info line.</summary>
    public bool ShowAdded { get; set; }

    /// <summary>Order of the two dates in the second info line.</summary>
    public SecondaryInfoOrderMode SecondaryInfoOrder { get; set; } = SecondaryInfoOrderMode.LastPlayedAdded;

    /// <summary>The game folder. Empty = Jellyfin's internal metadata path + Games.</summary>
    public string CacheFolder { get; set; } = string.Empty;

    /// <summary>Read the local extras only from the game's folder in ExtrasFolder instead of the game folder; the images stay in the game folder.</summary>
    public bool SeparateExtrasFolder { get; set; }

    /// <summary>Folder of the local extras while SeparateExtrasFolder is on, read only; one sub folder &lt;title [short id]&gt; per game.</summary>
    public string ExtrasFolder { get; set; } = string.Empty;

    /// <summary>Curator: write one .url file per link of each game into <see cref="CuratorLinksFolder"/> (Generate Links).</summary>
    public bool CuratorLinksEnabled { get; set; }

    /// <summary>Curator: the folder inside each game's folder that holds the .url files.</summary>
    public string CuratorLinksFolder { get; set; } = "links";

    /// <summary>Curator: where the links folder goes, the game folder, the extras folder (needs Separate Extras Folder) or both.</summary>
    public CuratorPlace CuratorLinksWhere { get; set; } = CuratorPlace.Metadata;

    /// <summary>Curator: refresh the .url files whenever Sync Metadata or Sync Extras runs.</summary>
    public bool CuratorLinksOnSync { get; set; }

    /// <summary>Curator: name of the shortcut in a game's metadata folder that opens its extras folder (Windows, needs Separate Extras Folder).</summary>
    public string CuratorToExtrasName { get; set; } = "extras";

    /// <summary>Curator: name of the shortcut in a game's extras folder that opens its metadata folder (Windows, needs Separate Extras Folder).</summary>
    public string CuratorToMetadataName { get; set; } = "metadata";

    /// <summary>Curator: refresh the shortcuts whenever Sync Metadata or Sync Extras runs.</summary>
    public bool CuratorShortcutsOnSync { get; set; }

    /// <summary>Copy every image of a game (cover, backdrop, logo, banner, thumb, icon) into its game folder, or none and read them in place.</summary>
    public ArtworkCopyMode ArtworkCopy { get; set; } = ArtworkCopyMode.All;

    /// <summary>Source of the backdrop: Playnite's background or Steam's library_hero.jpg.</summary>
    public ImageSourceChoice BackdropSource { get; set; } = ImageSourceChoice.Playnite;

    /// <summary>Source of the logo: none, Steam's logo.png, Playnite's ExtraMetadata Logo.png (default), or one then the other.</summary>
    public LogoImageSource LogoSource { get; set; } = LogoImageSource.ExtraMetadata;

    /// <summary>Source of the banner: none or Steam's header.jpg.</summary>
    public OptionalImageSource BannerSource { get; set; } = OptionalImageSource.Off;

    /// <summary>Source of the thumb: none or Steam's header.jpg.</summary>
    public OptionalImageSource ThumbSource { get; set; } = OptionalImageSource.Off;

    /// <summary>Show the seek bar thumbnails of local extra videos that have a trickplay folder next to them.</summary>
    public bool ShowTrickplay { get; set; } = true;

    /// <summary>Admin page switch of the Artwork tab: compute and serve the BlurHash placeholders. Not wired yet (stored only).</summary>
    public bool EnableBlurHash { get; set; } = true;

    /// <summary>Admin page master switch of the Videos tab: off = no trailers and extras on game pages and nothing else of the video options. Not wired yet (stored only).</summary>
    public bool EnableVideos { get; set; } = true;

    /// <summary>Videos tab: show a game's trailers; Sync Extras makes the thumbnails of trailers only with it.</summary>
    public bool EnableTrailers { get; set; } = true;

    /// <summary>Videos tab (label "Enable Special Features"): show a game's special features; Sync Extras makes their thumbnails only with it.</summary>
    public bool EnableExtras { get; set; } = true;

    /// <summary>Videos tab: the trailers of the shown version only, or also those of the group's other versions.</summary>
    public OtherVersionsMode TrailersOfOtherVersions { get; set; } = OtherVersionsMode.AllVersions;

    /// <summary>Videos tab: the extras (special features, theme video) of the shown version only, or also those of the group's other versions.</summary>
    public OtherVersionsMode ExtrasOfOtherVersions { get; set; } = OtherVersionsMode.AllVersions;

    /// <summary>Soundtrack: the shown version's soundtrack only, or (when it has none of its own) the first other version's of the group.</summary>
    public OtherVersionsMode SoundtrackOfOtherVersions { get; set; } = OtherVersionsMode.AllVersions;

    /// <summary>Videos tab: plays a game page's theme music; independent of Enable Videos and of Jellyfin's own theme song setting (off: Jellyfin's own rules).</summary>
    public bool EnableThemeMusic { get; set; }

    /// <summary>Themes tab, Version Fallback of the theme music: the shown version's own themes, or, when it has none, those of another version (FallBackToGroup, "If This Version Has None"); ThisVersionOnly takes none.</summary>
    public ThemeSourceMode ThemeSource { get; set; } = ThemeSourceMode.FallBackToGroup;

    /// <summary>Videos tab: what plays when there are several themes.</summary>
    public ThemePlayMode ThemePlayMode { get; set; } = ThemePlayMode.FirstOnly;

    /// <summary>Videos tab: what the theme music does when another version is picked on the game page.</summary>
    public ThemeSwitchMode ThemeOnVersionSwitch { get; set; } = ThemeSwitchMode.LookForNewTheme;

    /// <summary>Themes tab: the volume of the theme music in percent.</summary>
    public int ThemeVolumePercent { get; set; } = 40;

    /// <summary>Themes tab: seconds the theme music waits before the first song starts.</summary>
    public double ThemeDelayedStartSeconds { get; set; }

    /// <summary>Themes tab: where each song of the theme music starts.</summary>
    public ThemeStartPosition ThemeStartPosition { get; set; } = ThemeStartPosition.Beginning;

    /// <summary>Themes tab: the lowest random start of a song, in percent of its length.</summary>
    public int ThemeStartMinPercent { get; set; }

    /// <summary>Themes tab: the highest random start of a song, in percent of its length.</summary>
    public int ThemeStartMaxPercent { get; set; } = 50;

    /// <summary>Themes tab: seconds a theme song fades in at each start.</summary>
    public double ThemeFadeInSeconds { get; set; } = 1;

    /// <summary>Themes tab: seconds a theme song fades out before its end and when the game page is left.</summary>
    public double ThemeFadeOutSeconds { get; set; } = 1;

    /// <summary>Themes tab: the theme music goes round again after its last song (off: it plays once through and stops).</summary>
    public bool ThemeLoopMusic { get; set; } = true;

    /// <summary>Themes tab: Delayed Start also comes before every next song and every repeat (off: before the first song only).</summary>
    public bool ThemeDelayedStartEverySong { get; set; }

    /// <summary>Themes tab: Fade In and Fade Out at every song (off: a fade in at the very start and a fade out at the very end of the theme only).</summary>
    public bool ThemeFadeEverySong { get; set; } = true;

    /// <summary>Themes tab: plays a game page's theme videos (the sub folder backdrops of its game folder) behind the page; independent of Enable Videos and of Jellyfin's theme video setting.</summary>
    public bool EnableThemeVideos { get; set; }

    /// <summary>Themes tab, Version Fallback of the theme videos: the shown version's own, or, when it has none, those of another version (FallBackToGroup, "If This Version Has None"); applies to every source.</summary>
    public ThemeSourceMode ThemeVideoSource { get; set; } = ThemeSourceMode.FallBackToGroup;

    /// <summary>Themes tab: the sources the theme videos come from that are on, comma-separated: themevideos (the folder backdrops), trailers, specials (the local special features, never the Steam ones).</summary>
    public string ThemeVideoSources { get; set; } = "themevideos";

    /// <summary>Themes tab: the order of the sources (all three keys), the order the videos come in.</summary>
    public string ThemeVideoSourceOrder { get; set; } = "themevideos,trailers,specials";

    /// <summary>Themes tab: with several sources on, play the first one that has videos or all of them.</summary>
    public ThemeSourcesMode ThemeVideoSeveralSources { get; set; } = ThemeSourcesMode.FirstAvailable;

    /// <summary>Themes tab: Play All Shuffled shuffles inside each source and keeps the sources in their order (off: everything mixed).</summary>
    public bool ThemeVideoKeepSourcesTogether { get; set; }

    /// <summary>Themes tab: what plays when there are several theme videos.</summary>
    public ThemeVideoPlayMode ThemeVideoPlayMode { get; set; } = ThemeVideoPlayMode.FirstOnly;

    /// <summary>Themes tab: what the theme video does when another version is picked on the game page.</summary>
    public ThemeSwitchMode ThemeVideoOnVersionSwitch { get; set; } = ThemeSwitchMode.LookForNewTheme;

    /// <summary>Themes tab: the volume of a theme video in percent (0 = muted).</summary>
    public int ThemeVideoVolumePercent { get; set; }

    /// <summary>Themes tab: seconds the theme video waits before the first video starts.</summary>
    public double ThemeVideoDelayedStartSeconds { get; set; }

    /// <summary>Themes tab: where each theme video starts.</summary>
    public ThemeStartPosition ThemeVideoStartPosition { get; set; } = ThemeStartPosition.Beginning;

    /// <summary>Themes tab: the lowest random start of a video, in percent of its length.</summary>
    public int ThemeVideoStartMinPercent { get; set; }

    /// <summary>Themes tab: the highest random start of a video, in percent of its length.</summary>
    public int ThemeVideoStartMaxPercent { get; set; } = 50;

    /// <summary>Themes tab: the theme videos go round again after the last one (off: they play once and the page's backdrop returns).</summary>
    public bool ThemeLoopVideo { get; set; } = true;

    /// <summary>Themes tab: Delayed Start also comes before every next video and every repeat (off: before the first video only).</summary>
    public bool ThemeDelayedStartEveryVideo { get; set; }

    /// <summary>Videos tab: the videos of a game's Steam store page as the row Steam Special Features, played in Jellyfin's player (off by default: it asks Steam).</summary>
    public bool EnableSteamSpecialFeatures { get; set; }

    /// <summary>Videos tab: where the merged Steam Special Features stand in Jellyfin's Special Features row, before or after the local ones (Special Features of Other Versions applies to them).</summary>
    public SteamSpecialsPlacement SteamSpecialsPlacement { get; set; } = SteamSpecialsPlacement.BeforeLocal;

    /// <summary>Videos tab: the Steam Special Features go into Jellyfin's Special Features row (off: a row of their own).</summary>
    public bool SteamSpecialsMerge { get; set; }

    /// <summary>Videos tab: one row for each type and slot, all ticked ones together in Jellyfin's Special Features row, or together only while at least two of them have videos.</summary>
    public ScreenshotDisplay VideosDisplay { get; set; } = ScreenshotDisplay.Merged;

    /// <summary>Videos tab: in the merged row the videos of each type and slot stay together in table order (off: all sorted together).</summary>
    public bool VideosKeepSlotsTogether { get; set; }

    /// <summary>Videos tab: the order inside a type or slot (Date = the file's last write time; Steam clips keep Steam's order).</summary>
    public ScreenshotSort VideosSort { get; set; } = ScreenshotSort.NameAscending;

    /// <summary>Videos tab: the order of the types and own slots in the merged row and among the rows of their own, as keys (the nine types, slot1 to slot10), comma-separated.</summary>
    public string VideosRowOrder { get; set; } = "extras,clips,behindthescenes,deletedscenes,interviews,scenes,samples,shorts,featurettes,slot1,slot2,slot3,slot4,slot5,slot6,slot7,slot8,slot9,slot10";

    /// <summary>Videos tab: the shape of the tiles of the rows the plugin draws, fixed or taken from the pictures of the row.</summary>
    public TileShape VideosTileShape { get; set; } = TileShape.Landscape;

    /// <summary>Videos: the Extras type is shown.</summary>
    public bool VideoExtrasOn { get; set; } = true;

    /// <summary>Videos: the Extras videos go into the merged row (only while Display merges).</summary>
    public bool VideoExtrasMerge { get; set; } = true;

    /// <summary>Videos: the title of the Extras row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoExtrasTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Extras row while its title is Own; at most 40 characters.</summary>
    public string VideoExtrasLabel { get; set; } = "";

    /// <summary>Videos: the Clips type is shown.</summary>
    public bool VideoClipsOn { get; set; } = true;

    /// <summary>Videos: the Clips videos go into the merged row (only while Display merges).</summary>
    public bool VideoClipsMerge { get; set; } = true;

    /// <summary>Videos: the title of the Clips row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoClipsTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Clips row while its title is Own; at most 40 characters.</summary>
    public string VideoClipsLabel { get; set; } = "";

    /// <summary>Videos: the Behind the Scenes type is shown.</summary>
    public bool VideoBehindTheScenesOn { get; set; } = true;

    /// <summary>Videos: the Behind the Scenes videos go into the merged row (only while Display merges).</summary>
    public bool VideoBehindTheScenesMerge { get; set; } = true;

    /// <summary>Videos: the title of the Behind the Scenes row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoBehindTheScenesTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Behind the Scenes row while its title is Own; at most 40 characters.</summary>
    public string VideoBehindTheScenesLabel { get; set; } = "";

    /// <summary>Videos: the Deleted Scenes type is shown.</summary>
    public bool VideoDeletedScenesOn { get; set; } = true;

    /// <summary>Videos: the Deleted Scenes videos go into the merged row (only while Display merges).</summary>
    public bool VideoDeletedScenesMerge { get; set; } = true;

    /// <summary>Videos: the title of the Deleted Scenes row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoDeletedScenesTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Deleted Scenes row while its title is Own; at most 40 characters.</summary>
    public string VideoDeletedScenesLabel { get; set; } = "";

    /// <summary>Videos: the Interviews type is shown.</summary>
    public bool VideoInterviewsOn { get; set; } = true;

    /// <summary>Videos: the Interviews videos go into the merged row (only while Display merges).</summary>
    public bool VideoInterviewsMerge { get; set; } = true;

    /// <summary>Videos: the title of the Interviews row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoInterviewsTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Interviews row while its title is Own; at most 40 characters.</summary>
    public string VideoInterviewsLabel { get; set; } = "";

    /// <summary>Videos: the Scenes type is shown.</summary>
    public bool VideoScenesOn { get; set; } = true;

    /// <summary>Videos: the Scenes videos go into the merged row (only while Display merges).</summary>
    public bool VideoScenesMerge { get; set; } = true;

    /// <summary>Videos: the title of the Scenes row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoScenesTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Scenes row while its title is Own; at most 40 characters.</summary>
    public string VideoScenesLabel { get; set; } = "";

    /// <summary>Videos: the Samples type is shown.</summary>
    public bool VideoSamplesOn { get; set; } = true;

    /// <summary>Videos: the Samples videos go into the merged row (only while Display merges).</summary>
    public bool VideoSamplesMerge { get; set; } = true;

    /// <summary>Videos: the title of the Samples row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoSamplesTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Samples row while its title is Own; at most 40 characters.</summary>
    public string VideoSamplesLabel { get; set; } = "";

    /// <summary>Videos: the Shorts type is shown.</summary>
    public bool VideoShortsOn { get; set; } = true;

    /// <summary>Videos: the Shorts videos go into the merged row (only while Display merges).</summary>
    public bool VideoShortsMerge { get; set; } = true;

    /// <summary>Videos: the title of the Shorts row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoShortsTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Shorts row while its title is Own; at most 40 characters.</summary>
    public string VideoShortsLabel { get; set; } = "";

    /// <summary>Videos: the Featurettes type is shown.</summary>
    public bool VideoFeaturettesOn { get; set; } = true;

    /// <summary>Videos: the Featurettes videos go into the merged row (only while Display merges).</summary>
    public bool VideoFeaturettesMerge { get; set; } = true;

    /// <summary>Videos: the title of the Featurettes row as a key (empty is the type word, Own for the typed label).</summary>
    public string VideoFeaturettesTitle { get; set; } = "";

    /// <summary>Videos: the typed title of the Featurettes row while its title is Own; at most 40 characters.</summary>
    public string VideoFeaturettesLabel { get; set; } = "";

    /// <summary>Videos slot 1: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot1Parent { get; set; } = "videos";

    /// <summary>Videos slot 1: sub folder name.</summary>
    public string VideoSlot1Folder { get; set; } = "";

    /// <summary>Videos slot 1: row name (empty: the folder name).</summary>
    public string VideoSlot1Label { get; set; } = "";

    /// <summary>Videos slot 1: in use.</summary>
    public bool VideoSlot1On { get; set; }

    /// <summary>Videos slot 1: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot1Merge { get; set; } = true;

    /// <summary>Videos slot 2: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot2Parent { get; set; } = "videos";

    /// <summary>Videos slot 2: sub folder name.</summary>
    public string VideoSlot2Folder { get; set; } = "";

    /// <summary>Videos slot 2: row name (empty: the folder name).</summary>
    public string VideoSlot2Label { get; set; } = "";

    /// <summary>Videos slot 2: in use.</summary>
    public bool VideoSlot2On { get; set; }

    /// <summary>Videos slot 2: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot2Merge { get; set; } = true;

    /// <summary>Videos slot 3: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot3Parent { get; set; } = "videos";

    /// <summary>Videos slot 3: sub folder name.</summary>
    public string VideoSlot3Folder { get; set; } = "";

    /// <summary>Videos slot 3: row name (empty: the folder name).</summary>
    public string VideoSlot3Label { get; set; } = "";

    /// <summary>Videos slot 3: in use.</summary>
    public bool VideoSlot3On { get; set; }

    /// <summary>Videos slot 3: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot3Merge { get; set; } = true;

    /// <summary>Videos slot 4: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot4Parent { get; set; } = "videos";

    /// <summary>Videos slot 4: sub folder name.</summary>
    public string VideoSlot4Folder { get; set; } = "";

    /// <summary>Videos slot 4: row name (empty: the folder name).</summary>
    public string VideoSlot4Label { get; set; } = "";

    /// <summary>Videos slot 4: in use.</summary>
    public bool VideoSlot4On { get; set; }

    /// <summary>Videos slot 4: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot4Merge { get; set; } = true;

    /// <summary>Videos slot 5: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot5Parent { get; set; } = "videos";

    /// <summary>Videos slot 5: sub folder name.</summary>
    public string VideoSlot5Folder { get; set; } = "";

    /// <summary>Videos slot 5: row name (empty: the folder name).</summary>
    public string VideoSlot5Label { get; set; } = "";

    /// <summary>Videos slot 5: in use.</summary>
    public bool VideoSlot5On { get; set; }

    /// <summary>Videos slot 5: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot5Merge { get; set; } = true;

    /// <summary>Videos slot 6: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot6Parent { get; set; } = "videos";

    /// <summary>Videos slot 6: sub folder name.</summary>
    public string VideoSlot6Folder { get; set; } = "";

    /// <summary>Videos slot 6: row name (empty: the folder name).</summary>
    public string VideoSlot6Label { get; set; } = "";

    /// <summary>Videos slot 6: in use.</summary>
    public bool VideoSlot6On { get; set; }

    /// <summary>Videos slot 6: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot6Merge { get; set; } = true;

    /// <summary>Videos slot 7: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot7Parent { get; set; } = "videos";

    /// <summary>Videos slot 7: sub folder name.</summary>
    public string VideoSlot7Folder { get; set; } = "";

    /// <summary>Videos slot 7: row name (empty: the folder name).</summary>
    public string VideoSlot7Label { get; set; } = "";

    /// <summary>Videos slot 7: in use.</summary>
    public bool VideoSlot7On { get; set; }

    /// <summary>Videos slot 7: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot7Merge { get; set; } = true;

    /// <summary>Videos slot 8: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot8Parent { get; set; } = "videos";

    /// <summary>Videos slot 8: sub folder name.</summary>
    public string VideoSlot8Folder { get; set; } = "";

    /// <summary>Videos slot 8: row name (empty: the folder name).</summary>
    public string VideoSlot8Label { get; set; } = "";

    /// <summary>Videos slot 8: in use.</summary>
    public bool VideoSlot8On { get; set; }

    /// <summary>Videos slot 8: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot8Merge { get; set; } = true;

    /// <summary>Videos slot 9: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot9Parent { get; set; } = "videos";

    /// <summary>Videos slot 9: sub folder name.</summary>
    public string VideoSlot9Folder { get; set; } = "";

    /// <summary>Videos slot 9: row name (empty: the folder name).</summary>
    public string VideoSlot9Label { get; set; } = "";

    /// <summary>Videos slot 9: in use.</summary>
    public bool VideoSlot9On { get; set; }

    /// <summary>Videos slot 9: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot9Merge { get; set; } = true;

    /// <summary>Videos slot 10: the folder the slot sits in (empty: directly in the game folder).</summary>
    public string VideoSlot10Parent { get; set; } = "videos";

    /// <summary>Videos slot 10: sub folder name.</summary>
    public string VideoSlot10Folder { get; set; } = "";

    /// <summary>Videos slot 10: row name (empty: the folder name).</summary>
    public string VideoSlot10Label { get; set; } = "";

    /// <summary>Videos slot 10: in use.</summary>
    public bool VideoSlot10On { get; set; }

    /// <summary>Videos slot 10: its videos go into the merged row (only while Display merges).</summary>
    public bool VideoSlot10Merge { get; set; } = true;

    /// <summary>Where the cover comes from (Sources table of the Artwork tab).</summary>
    public ImageSourceChoice CoverSource { get; set; } = ImageSourceChoice.Playnite;

    /// <summary>Where the list view icon comes from.</summary>
    public ImageSourceChoice IconSource { get; set; } = ImageSourceChoice.Playnite;

    /// <summary>File name of the Cover picture in the game folder (a Jellyfin name, or "Own" with CoverOwnName).</summary>
    public string CoverFileName { get; set; } = "poster";

    /// <summary>The own file name used while CoverFileName is Own.</summary>
    public string CoverOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Backdrop picture in the game folder (a Jellyfin name, or "Own" with BackdropOwnName).</summary>
    public string BackdropFileName { get; set; } = "backdrop";

    /// <summary>The own file name used while BackdropFileName is Own.</summary>
    public string BackdropOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Logo picture in the game folder (a Jellyfin name, or "Own" with LogoOwnName).</summary>
    public string LogoFileName { get; set; } = "clearlogo";

    /// <summary>The own file name used while LogoFileName is Own.</summary>
    public string LogoOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Banner picture in the game folder (a Jellyfin name, or "Own" with BannerOwnName).</summary>
    public string BannerFileName { get; set; } = "banner";

    /// <summary>The own file name used while BannerFileName is Own.</summary>
    public string BannerOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Thumb picture in the game folder (a Jellyfin name, or "Own" with ThumbOwnName).</summary>
    public string ThumbFileName { get; set; } = "landscape";

    /// <summary>The own file name used while ThumbFileName is Own.</summary>
    public string ThumbOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Icon picture in the game folder (a Jellyfin name, or "Own" with IconOwnName).</summary>
    public string IconFileName { get; set; } = "icon";

    /// <summary>The own file name used while IconFileName is Own.</summary>
    public string IconOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Disc picture in the game folder (a Jellyfin name, or "Own" with DiscOwnName).</summary>
    public string DiscFileName { get; set; } = "disc";

    /// <summary>The own file name used while DiscFileName is Own.</summary>
    public string DiscOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Clearart picture in the game folder (a Jellyfin name, or "Own" with ClearartOwnName).</summary>
    public string ClearartFileName { get; set; } = "clearart";

    /// <summary>The own file name used while ClearartFileName is Own.</summary>
    public string ClearartOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Characterart picture in the game folder (a Jellyfin name, or "Own" with CharacterartOwnName).</summary>
    public string CharacterartFileName { get; set; } = "characterart";

    /// <summary>The own file name used while CharacterartFileName is Own.</summary>
    public string CharacterartOwnName { get; set; } = string.Empty;

    /// <summary>What Sync Artwork does with the Cover copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode CoverMode { get; set; } = ArtworkMode.Keep;

    /// <summary>What Sync Artwork does with the Backdrop copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode BackdropMode { get; set; } = ArtworkMode.Keep;

    /// <summary>What Sync Artwork does with the Logo copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode LogoMode { get; set; } = ArtworkMode.Keep;

    /// <summary>What Sync Artwork does with the Banner copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode BannerMode { get; set; } = ArtworkMode.Keep;

    /// <summary>What Sync Artwork does with the Thumb copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode ThumbMode { get; set; } = ArtworkMode.Keep;

    /// <summary>What Sync Artwork does with the Icon copies (Sources table of the Artwork tab).</summary>
    public ArtworkMode IconMode { get; set; } = ArtworkMode.Keep;

    /// <summary>File name of the Square picture in the game folder (its default name, or "Own" with SquareOwnName).</summary>
    public string SquareFileName { get; set; } = "square";

    /// <summary>The own file name used while SquareFileName is Own.</summary>
    public string SquareOwnName { get; set; } = string.Empty;

    /// <summary>File name of the Box3d picture in the game folder (its default name, or "Own" with Box3dOwnName).</summary>
    public string Box3dFileName { get; set; } = "box3d";

    /// <summary>The own file name used while Box3dFileName is Own.</summary>
    public string Box3dOwnName { get; set; } = string.Empty;

    /// <summary>File name of the BoxFront picture in the game folder (its default name, or "Own" with BoxFrontOwnName).</summary>
    public string BoxFrontFileName { get; set; } = "boxfront";

    /// <summary>The own file name used while BoxFrontFileName is Own.</summary>
    public string BoxFrontOwnName { get; set; } = string.Empty;

    /// <summary>File name of the BoxBack picture in the game folder (its default name, or "Own" with BoxBackOwnName).</summary>
    public string BoxBackFileName { get; set; } = "boxback";

    /// <summary>The own file name used while BoxBackFileName is Own.</summary>
    public string BoxBackOwnName { get; set; } = string.Empty;

    /// <summary>File name of the BoxSpine picture in the game folder (its default name, or "Own" with BoxSpineOwnName).</summary>
    public string BoxSpineFileName { get; set; } = "boxspine";

    /// <summary>The own file name used while BoxSpineFileName is Own.</summary>
    public string BoxSpineOwnName { get; set; } = string.Empty;

    /// <summary>Detail page: the version dropdown also lists the same game from other platforms.</summary>
    public bool ShowOtherPlatforms { get; set; } = true;

    /// <summary>Detail page: the version dropdown also lists editions of the game.</summary>
    public bool ShowEditions { get; set; }

    /// <summary>Detail page marker kind (independent of the library's).</summary>
    public VersionMarkerKind DetailMarkerKind { get; set; } = VersionMarkerKind.Icons;

    /// <summary>Detail page marker corner on the cover.</summary>
    public VersionMarkerPosition DetailMarkerPosition { get; set; } = VersionMarkerPosition.TopLeft;

    /// <summary>When the detail page marker shows.</summary>
    public DetailMarkerShowMode DetailMarkerShow { get; set; } = DetailMarkerShowMode.Always;

    /// <summary>Most platform icons on the detail page cover; 0 = unlimited.</summary>
    public int DetailMaxMarkerIcons { get; set; }

    /// <summary>Artwork on version change, Follow Cover.</summary>
    public bool VersionFollowCover { get; set; } = true;

    /// <summary>Artwork on version change, Follow Backdrop.</summary>
    public bool VersionFollowBackdrop { get; set; } = true;

    /// <summary>Artwork on version change, Follow Logo.</summary>
    public bool VersionFollowLogo { get; set; } = true;

    /// <summary>Artwork on version change, Follow Clearart.</summary>
    public bool VersionFollowClearart { get; set; } = true;

    /// <summary>Artwork on version change, Follow Characterart.</summary>
    public bool VersionFollowCharacterart { get; set; } = true;

    /// <summary>Artwork on version change, Keep Cover.</summary>
    public bool VersionKeepCover { get; set; } = true;

    /// <summary>Artwork on version change, Keep Backdrop.</summary>
    public bool VersionKeepBackdrop { get; set; } = true;

    /// <summary>Artwork on version change, Keep Logo.</summary>
    public bool VersionKeepLogo { get; set; } = true;

    /// <summary>Artwork on version change, Keep Clearart.</summary>
    public bool VersionKeepClearart { get; set; } = true;

    /// <summary>Artwork on version change, Keep Characterart.</summary>
    public bool VersionKeepCharacterart { get; set; } = true;

    /// <summary>The row order of a game page as a list: videos, screenshots, wallpapers, documents, collections, related, actors, connections, soundtrack, similar. A row the list does not name stands before similar.</summary>
    public string RowOrder { get; set; } = "videos,screenshots,wallpapers,documents,collections,related,actors,connections,soundtrack,similar";

    /// <summary>Connections: show the series tiles, the related media and the cast from game.nfo on a game page; off hides all three.</summary>
    public bool EnableConnections { get; set; } = true;

    /// <summary>Connections: where the series tiles show (needs Show Series As Tiles or Both and Enable Connections; Off hides the tiles).</summary>
    public ConnectionsShow ConnectionsSeries { get; set; } = ConnectionsShow.OwnRow;

    /// <summary>Connections: where the related media of game.nfo show.</summary>
    public ConnectionsShow ConnectionsMedia { get; set; } = ConnectionsShow.OwnRow;

    /// <summary>Connections: where the cast of game.nfo shows.</summary>
    public ConnectionsShow ConnectionsCast { get; set; } = ConnectionsShow.OwnRow;

    /// <summary>Connections: the cast cards show the pictures of the game's .actors folder; off = only the actors' photos of Jellyfin.</summary>
    public bool ConnectionsCustomPictures { get; set; }

    /// <summary>Connections: the name of the shared row.</summary>
    public ConnectionsRowName ConnectionsName { get; set; } = ConnectionsRowName.Connections;

    /// <summary>Connections: the typed name of the shared row while the name is Own (at most 40 characters).</summary>
    public string ConnectionsOwnName { get; set; } = "";

    /// <summary>Connections: the title of the related media group.</summary>
    public ConnectionsMediaName ConnectionsMediaName { get; set; } = ConnectionsMediaName.RelatedMedia;

    /// <summary>Connections: the typed title of the related media group while its name is Own (at most 40 characters).</summary>
    public string ConnectionsMediaOwnName { get; set; } = "";

    /// <summary>Connections: the title of the cast group.</summary>
    public ConnectionsCastName ConnectionsCastName { get; set; } = ConnectionsCastName.CastAndCrew;

    /// <summary>Connections: the typed title of the cast group while its name is Own (at most 40 characters).</summary>
    public string ConnectionsCastOwnName { get; set; } = "";

    /// <summary>Connections: with only one group in the shared row, the row is named by that group's own name instead of the Connections title.</summary>
    public bool ConnectionsNameSingleGroup { get; set; } = true;

    /// <summary>Connections: a divider with the group's name between the groups of the shared row; off puts all tiles in one row without any.</summary>
    public bool ConnectionsShowDividers { get; set; } = true;

    /// <summary>Connections: the order of the groups in the shared row as a list: series, media, cast.</summary>
    public string ConnectionsGroupOrder { get; set; } = "series,media,cast";

    /// <summary>Connections: the order of the media kinds inside the media group as a list: collections, movies, tvshows.</summary>
    public string ConnectionsMediaOrder { get; set; } = "collections,movies,tvshows";

    /// <summary>Show Jellyfin's More Like This row on game pages.</summary>
    public bool ShowMoreLikeThis { get; set; } = true;

    /// <summary>The custom title of the Steam Special Features (used while SteamSpecialFeaturesTitle is Own); at most 40 characters.</summary>
    public string SteamSpecialFeaturesLabel { get; set; } = "";

    /// <summary>Videos: in the merged row a divider with a name between the Steam and the local videos (and, with the slots together, between the types and slots).</summary>
    public bool VideosShowDividers { get; set; }

    /// <summary>Show the camera on the cards of special features (local and Steam, in Jellyfin's row and in the plugin's own Steam row).</summary>
    public bool ShowVideoIcon { get; set; } = true;

    /// <summary>Show the second text line of the cards of special features (their runtime, or the label of a Steam video).</summary>
    public bool ShowSubtext { get; set; } = true;

    /// <summary>Show the first text line of the cards of special features (the name of the video).</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>Documents: the first text line under a tile (the file name without its extension).</summary>
    public bool DocumentsShowTitle { get; set; } = true;

    /// <summary>Documents: the second text line under a tile (the name of its slot).</summary>
    public bool DocumentsShowSubtext { get; set; } = true;

    /// <summary>Documents: how the picture of a tile is shown, cut to the tile, whole on a blurred fill, or one of both by the Crop Limit.</summary>
    public PictureFit DocumentsPictureFit { get; set; } = PictureFit.Adaptive;

    /// <summary>Documents: with Picture Fit Adaptive, the most of a picture (percent, 5 to 50) a crop may cut off; a picture that would lose more is shown on the blurred fill.</summary>
    public int DocumentsCropLimitPercent { get; set; } = 20;

    /// <summary>Documents: keep the page-1 preview of a PDF or CBR comic (drawn by a browser) in the plugin's cache, made on a visit and marked by Sync Extras; off stores nothing and the browser draws page 1 on each visit.</summary>
    public bool DocumentsGenerateThumbnails { get; set; } = true;

    /// <summary>Screenshots: the first text line under a tile (the file name without its extension).</summary>
    public bool ScreenshotsShowTitle { get; set; }

    /// <summary>Screenshots: the second text line under a tile (the label of its slot).</summary>
    public bool ScreenshotsShowSubtext { get; set; }

    /// <summary>Screenshots: the shape of the tiles of a row, fixed or taken from the pictures of the row.</summary>
    public TileShape ScreenshotsTileShape { get; set; } = TileShape.Landscape;

    /// <summary>Screenshots: how the picture of a tile is shown, cut to the tile, whole on a blurred fill, or one of both by the Crop Limit.</summary>
    public PictureFit ScreenshotsPictureFit { get; set; } = PictureFit.Adaptive;

    /// <summary>Screenshots: with Picture Fit Adaptive, the most of a picture (percent, 5 to 50) a crop may cut off; a picture that would lose more is shown on the blurred fill.</summary>
    public int ScreenshotsCropLimitPercent { get; set; } = 20;

    /// <summary>The title of the local special features as a key (SpecialFeatures, Extras, BonusMaterial, HeaderVideos or Own for the custom text); empty is the default.</summary>
    public string SpecialFeaturesTitle { get; set; } = "";

    /// <summary>The title of the Steam special features as a key (SteamSpecialFeatures, SteamVideos, SteamTrailers or Own for SteamSpecialFeaturesLabel); empty is the default.</summary>
    public string SteamSpecialFeaturesTitle { get; set; } = "";

    /// <summary>The title of the merged or flat screenshots row as a key (Screenshots, Gameplay, Gallery or Own for ScreenshotsLabel); empty is the default.</summary>
    public string ScreenshotsTitle { get; set; } = "";

    /// <summary>The title of the merged or flat documents row as a key (Documents, Library, ManualsAndBooks or Own for DocumentsLabel); empty is the default.</summary>
    public string DocumentsTitle { get; set; } = "";

    /// <summary>The title of the soundtrack row as a key (Soundtrack, Music, OriginalSoundtrack or Own for SoundtrackLabel); empty is the default.</summary>
    public string SoundtrackTitle { get; set; } = "";

    /// <summary>The title of the local special features (the divider next to the Steam ones, the row title); empty keeps the default in the viewer's language.</summary>
    public string SpecialFeaturesLabel { get; set; } = string.Empty;

    /// <summary>The custom title of the merged or flat screenshots row (used while ScreenshotsTitle is Own); at most 40 characters.</summary>
    public string ScreenshotsLabel { get; set; } = string.Empty;

    /// <summary>The title of the merged or flat documents row; empty keeps the default in the viewer's language.</summary>
    public string DocumentsLabel { get; set; } = string.Empty;

    /// <summary>The title of the soundtrack row; empty keeps the default in the viewer's language.</summary>
    public string SoundtrackLabel { get; set; } = string.Empty;

    /// <summary>Documents: show the documents (PDF, EPUB, CBZ, CBR) of a game as rows on its page (read from the documents folder of the version's own folder).</summary>
    public bool EnableDocuments { get; set; }

    /// <summary>Documents: the folder inside a version's folder (the Extras Folder with Separate Extras Folder, else the game folder) that holds the slot folders.</summary>
    public string DocumentsFolder { get; set; } = "documents";

    /// <summary>Documents: ignore all sub folders and the slot list; only the files lying directly in the documents folder, as one row.</summary>
    public bool DocumentsFlat { get; set; }

    /// <summary>Documents: one row per slot, the slots ticked in Merge always in one row, or in one row only while at least two of them have files.</summary>
    public ScreenshotDisplay DocumentsDisplay { get; set; } = ScreenshotDisplay.Adaptive;

    /// <summary>Documents, merged display: slot order first and the sort inside each slot (off: all documents mixed by the sort).</summary>
    public bool DocumentsKeepSlotsTogether { get; set; } = true;

    /// <summary>Documents, merged display with the slots together: a divider with the slot's name before each slot's tiles.</summary>
    public bool DocumentsShowDividers { get; set; }

    /// <summary>Documents: the order inside a row.</summary>
    public ScreenshotSort DocumentsSort { get; set; } = ScreenshotSort.NameAscending;

    /// <summary>Documents slot 1: sub folder name.</summary>
    public string DocumentSlot1Folder { get; set; } = "manuals";

    /// <summary>Documents slot 1: row name.</summary>
    public DocumentLabel DocumentSlot1Label { get; set; } = DocumentLabel.Manuals;

    /// <summary>Documents slot 1: the typed row name while the label is Own.</summary>
    public string DocumentSlot1OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 1: in use.</summary>
    public bool DocumentSlot1On { get; set; } = true;

    /// <summary>Documents slot 1: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot1Merge { get; set; } = true;

    /// <summary>Documents slot 2: sub folder name.</summary>
    public string DocumentSlot2Folder { get; set; } = "guides";

    /// <summary>Documents slot 2: row name.</summary>
    public DocumentLabel DocumentSlot2Label { get; set; } = DocumentLabel.Guides;

    /// <summary>Documents slot 2: the typed row name while the label is Own.</summary>
    public string DocumentSlot2OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 2: in use.</summary>
    public bool DocumentSlot2On { get; set; } = true;

    /// <summary>Documents slot 2: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot2Merge { get; set; } = true;

    /// <summary>Documents slot 3: sub folder name.</summary>
    public string DocumentSlot3Folder { get; set; } = "maps";

    /// <summary>Documents slot 3: row name.</summary>
    public DocumentLabel DocumentSlot3Label { get; set; } = DocumentLabel.Maps;

    /// <summary>Documents slot 3: the typed row name while the label is Own.</summary>
    public string DocumentSlot3OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 3: in use.</summary>
    public bool DocumentSlot3On { get; set; } = true;

    /// <summary>Documents slot 3: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot3Merge { get; set; } = true;

    /// <summary>Documents slot 4: sub folder name.</summary>
    public string DocumentSlot4Folder { get; set; } = "books";

    /// <summary>Documents slot 4: row name.</summary>
    public DocumentLabel DocumentSlot4Label { get; set; } = DocumentLabel.Books;

    /// <summary>Documents slot 4: the typed row name while the label is Own.</summary>
    public string DocumentSlot4OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 4: in use.</summary>
    public bool DocumentSlot4On { get; set; } = true;

    /// <summary>Documents slot 4: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot4Merge { get; set; } = true;

    /// <summary>Documents slot 5: sub folder name.</summary>
    public string DocumentSlot5Folder { get; set; } = "articles";

    /// <summary>Documents slot 5: row name.</summary>
    public DocumentLabel DocumentSlot5Label { get; set; } = DocumentLabel.Articles;

    /// <summary>Documents slot 5: the typed row name while the label is Own.</summary>
    public string DocumentSlot5OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 5: in use.</summary>
    public bool DocumentSlot5On { get; set; } = true;

    /// <summary>Documents slot 5: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot5Merge { get; set; } = true;

    /// <summary>Documents slot 6: sub folder name.</summary>
    public string DocumentSlot6Folder { get; set; } = "";

    /// <summary>Documents slot 6: row name.</summary>
    public DocumentLabel DocumentSlot6Label { get; set; } = DocumentLabel.Own;

    /// <summary>Documents slot 6: the typed row name while the label is Own.</summary>
    public string DocumentSlot6OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 6: in use.</summary>
    public bool DocumentSlot6On { get; set; }

    /// <summary>Documents slot 6: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot6Merge { get; set; } = true;

    /// <summary>Documents slot 7: sub folder name.</summary>
    public string DocumentSlot7Folder { get; set; } = "";

    /// <summary>Documents slot 7: row name.</summary>
    public DocumentLabel DocumentSlot7Label { get; set; } = DocumentLabel.Own;

    /// <summary>Documents slot 7: the typed row name while the label is Own.</summary>
    public string DocumentSlot7OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 7: in use.</summary>
    public bool DocumentSlot7On { get; set; }

    /// <summary>Documents slot 7: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot7Merge { get; set; } = true;

    /// <summary>Documents slot 8: sub folder name.</summary>
    public string DocumentSlot8Folder { get; set; } = "";

    /// <summary>Documents slot 8: row name.</summary>
    public DocumentLabel DocumentSlot8Label { get; set; } = DocumentLabel.Own;

    /// <summary>Documents slot 8: the typed row name while the label is Own.</summary>
    public string DocumentSlot8OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 8: in use.</summary>
    public bool DocumentSlot8On { get; set; }

    /// <summary>Documents slot 8: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot8Merge { get; set; } = true;

    /// <summary>Documents slot 9: sub folder name.</summary>
    public string DocumentSlot9Folder { get; set; } = "";

    /// <summary>Documents slot 9: row name.</summary>
    public DocumentLabel DocumentSlot9Label { get; set; } = DocumentLabel.Own;

    /// <summary>Documents slot 9: the typed row name while the label is Own.</summary>
    public string DocumentSlot9OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 9: in use.</summary>
    public bool DocumentSlot9On { get; set; }

    /// <summary>Documents slot 9: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot9Merge { get; set; } = true;

    /// <summary>Documents slot 10: sub folder name.</summary>
    public string DocumentSlot10Folder { get; set; } = "";

    /// <summary>Documents slot 10: row name.</summary>
    public DocumentLabel DocumentSlot10Label { get; set; } = DocumentLabel.Own;

    /// <summary>Documents slot 10: the typed row name while the label is Own.</summary>
    public string DocumentSlot10OwnLabel { get; set; } = string.Empty;

    /// <summary>Documents slot 10: in use.</summary>
    public bool DocumentSlot10On { get; set; }

    /// <summary>Documents slot 10: its files go into the merged row (only while Display merges).</summary>
    public bool DocumentSlot10Merge { get; set; } = true;

    /// <summary>Screenshots: show the screenshots of a game as rows on its page (read from the screenshots folder of the version's own folder).</summary>
    public bool EnableScreenshots { get; set; } = true;

    /// <summary>Show the soundtrack row on game pages: the audio files in a game's folder "soundtrack", played by Jellyfin's own player.</summary>
    public bool EnableSoundtrack { get; set; } = true;

    /// <summary>Soundtrack: the folder inside a version's folder that holds the audio files (and cover); case is ignored.</summary>
    public string SoundtrackFolder { get; set; } = "soundtrack";

    /// <summary>Soundtrack: with Theme Music on, the soundtrack tracks are the game's theme songs when it has none of its own.</summary>
    public bool SoundtrackAsTheme { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork: show the pictures of a game's wallpapers folder as rows on its page (opened in Jellyfin's viewer).</summary>
    public bool EnableWallpapers { get; set; }

    /// <summary>Wallpapers &amp; Artwork: the folder inside a version's folder (as the Screenshots Folder) that holds the slot folders.</summary>
    public string WallpapersFolder { get; set; } = "wallpapers";

    /// <summary>Wallpapers &amp; Artwork: ignore all sub folders and the slot list; only the images lying directly in the folder, as one row.</summary>
    public bool WallpapersFlat { get; set; }

    /// <summary>Wallpapers &amp; Artwork: one row per slot, the slots ticked in Merge always in one row, or in one row only while at least two of them have files.</summary>
    public ScreenshotDisplay WallpapersDisplay { get; set; } = ScreenshotDisplay.Adaptive;

    /// <summary>Wallpapers &amp; Artwork, merged display: slot order first and the sort inside each slot (off: all pictures of the merged row mixed by the sort).</summary>
    public bool WallpapersKeepSlotsTogether { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork, merged display with the slots together: a divider with the slot's name before each slot's tiles.</summary>
    public bool WallpapersShowDividers { get; set; }

    /// <summary>Wallpapers &amp; Artwork: the order inside a row.</summary>
    public ScreenshotSort WallpapersSort { get; set; } = ScreenshotSort.NameAscending;

    /// <summary>Wallpapers &amp; Artwork: the first text line under a tile (the file name without its extension).</summary>
    public bool WallpapersShowTitle { get; set; }

    /// <summary>Wallpapers &amp; Artwork: the second text line under a tile (the label of its slot).</summary>
    public bool WallpapersShowSubtext { get; set; }

    /// <summary>Wallpapers &amp; Artwork: the shape of the tiles of a row, fixed or taken from the pictures of the row.</summary>
    public TileShape WallpapersTileShape { get; set; } = TileShape.Landscape;

    /// <summary>Wallpapers &amp; Artwork: how the picture of a tile is shown, cut to the tile, whole on a blurred fill, or one of both by the Crop Limit.</summary>
    public PictureFit WallpapersPictureFit { get; set; } = PictureFit.Adaptive;

    /// <summary>Wallpapers &amp; Artwork: with Picture Fit Adaptive, the most of a picture (percent, 5 to 50) a crop may cut off; a picture that would lose more is shown on the blurred fill.</summary>
    public int WallpapersCropLimitPercent { get; set; } = 20;

    /// <summary>The title of the merged or flat wallpapers row as a key (WallpapersAndArtwork, Wallpapers, Artwork, Gallery or Own for WallpapersLabel); empty is the default.</summary>
    public string WallpapersTitle { get; set; } = "";

    /// <summary>The custom title of the merged or flat wallpapers row (used while WallpapersTitle is Own); at most 40 characters.</summary>
    public string WallpapersLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 1: sub folder name.</summary>
    public string WallpaperSlot1Folder { get; set; } = "wallpapers";

    /// <summary>Wallpapers &amp; Artwork slot 1: row name.</summary>
    public WallpaperLabel WallpaperSlot1Label { get; set; } = WallpaperLabel.Wallpapers;

    /// <summary>Wallpapers &amp; Artwork slot 1: the typed row name while the label is Own.</summary>
    public string WallpaperSlot1OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 1: in use.</summary>
    public bool WallpaperSlot1On { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork slot 1: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot1Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 2: sub folder name.</summary>
    public string WallpaperSlot2Folder { get; set; } = "fanart";

    /// <summary>Wallpapers &amp; Artwork slot 2: row name.</summary>
    public WallpaperLabel WallpaperSlot2Label { get; set; } = WallpaperLabel.FanArt;

    /// <summary>Wallpapers &amp; Artwork slot 2: the typed row name while the label is Own.</summary>
    public string WallpaperSlot2OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 2: in use.</summary>
    public bool WallpaperSlot2On { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork slot 2: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot2Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 3: sub folder name.</summary>
    public string WallpaperSlot3Folder { get; set; } = "concept";

    /// <summary>Wallpapers &amp; Artwork slot 3: row name.</summary>
    public WallpaperLabel WallpaperSlot3Label { get; set; } = WallpaperLabel.ConceptArt;

    /// <summary>Wallpapers &amp; Artwork slot 3: the typed row name while the label is Own.</summary>
    public string WallpaperSlot3OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 3: in use.</summary>
    public bool WallpaperSlot3On { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork slot 3: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot3Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 4: sub folder name.</summary>
    public string WallpaperSlot4Folder { get; set; } = "posters";

    /// <summary>Wallpapers &amp; Artwork slot 4: row name.</summary>
    public WallpaperLabel WallpaperSlot4Label { get; set; } = WallpaperLabel.Posters;

    /// <summary>Wallpapers &amp; Artwork slot 4: the typed row name while the label is Own.</summary>
    public string WallpaperSlot4OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 4: in use.</summary>
    public bool WallpaperSlot4On { get; set; } = true;

    /// <summary>Wallpapers &amp; Artwork slot 4: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot4Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 5: sub folder name.</summary>
    public string WallpaperSlot5Folder { get; set; } = "boxart";

    /// <summary>Wallpapers &amp; Artwork slot 5: row name.</summary>
    public WallpaperLabel WallpaperSlot5Label { get; set; } = WallpaperLabel.BoxArt;

    /// <summary>Wallpapers &amp; Artwork slot 5: the typed row name while the label is Own.</summary>
    public string WallpaperSlot5OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 5: in use.</summary>
    public bool WallpaperSlot5On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 5: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot5Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 6: sub folder name.</summary>
    public string WallpaperSlot6Folder { get; set; } = "renders";

    /// <summary>Wallpapers &amp; Artwork slot 6: row name.</summary>
    public WallpaperLabel WallpaperSlot6Label { get; set; } = WallpaperLabel.Renders;

    /// <summary>Wallpapers &amp; Artwork slot 6: the typed row name while the label is Own.</summary>
    public string WallpaperSlot6OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 6: in use.</summary>
    public bool WallpaperSlot6On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 6: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot6Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 7: sub folder name.</summary>
    public string WallpaperSlot7Folder { get; set; } = "logos";

    /// <summary>Wallpapers &amp; Artwork slot 7: row name.</summary>
    public WallpaperLabel WallpaperSlot7Label { get; set; } = WallpaperLabel.Logos;

    /// <summary>Wallpapers &amp; Artwork slot 7: the typed row name while the label is Own.</summary>
    public string WallpaperSlot7OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 7: in use.</summary>
    public bool WallpaperSlot7On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 7: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot7Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 8: sub folder name.</summary>
    public string WallpaperSlot8Folder { get; set; } = "icons";

    /// <summary>Wallpapers &amp; Artwork slot 8: row name.</summary>
    public WallpaperLabel WallpaperSlot8Label { get; set; } = WallpaperLabel.Icons;

    /// <summary>Wallpapers &amp; Artwork slot 8: the typed row name while the label is Own.</summary>
    public string WallpaperSlot8OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 8: in use.</summary>
    public bool WallpaperSlot8On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 8: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot8Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 9: sub folder name.</summary>
    public string WallpaperSlot9Folder { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 9: row name.</summary>
    public WallpaperLabel WallpaperSlot9Label { get; set; } = WallpaperLabel.Own;

    /// <summary>Wallpapers &amp; Artwork slot 9: the typed row name while the label is Own.</summary>
    public string WallpaperSlot9OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 9: in use.</summary>
    public bool WallpaperSlot9On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 9: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot9Merge { get; set; } = true;


    /// <summary>Wallpapers &amp; Artwork slot 10: sub folder name.</summary>
    public string WallpaperSlot10Folder { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 10: row name.</summary>
    public WallpaperLabel WallpaperSlot10Label { get; set; } = WallpaperLabel.Own;

    /// <summary>Wallpapers &amp; Artwork slot 10: the typed row name while the label is Own.</summary>
    public string WallpaperSlot10OwnLabel { get; set; } = string.Empty;

    /// <summary>Wallpapers &amp; Artwork slot 10: in use.</summary>
    public bool WallpaperSlot10On { get; set; }

    /// <summary>Wallpapers &amp; Artwork slot 10: its files go into the merged row (only while Display merges).</summary>
    public bool WallpaperSlot10Merge { get; set; } = true;


    /// <summary>Screenshots: the folder inside a version's folder (the Extras Folder with Separate Extras Folder, else the game folder) that holds the slot folders.</summary>
    public string ScreenshotsFolder { get; set; } = "screenshots";

    /// <summary>Screenshots: ignore all sub folders and the slot list; only the images lying directly in the screenshots folder, as one row.</summary>
    public bool ScreenshotsFlat { get; set; }

    /// <summary>Screenshots: one row per slot, the slots ticked in Merge always in one row, or in one row only while at least two of them have files.</summary>
    public ScreenshotDisplay ScreenshotsDisplay { get; set; } = ScreenshotDisplay.Adaptive;

    /// <summary>Screenshots, merged display: slot order first and the sort inside each slot (off: all screenshots mixed by the sort).</summary>
    public bool ScreenshotsKeepSlotsTogether { get; set; } = true;

    /// <summary>Screenshots, merged display with the slots together: a divider with the slot's name before each slot's tiles.</summary>
    public bool ScreenshotsShowDividers { get; set; }

    /// <summary>Screenshots: the order inside a row.</summary>
    public ScreenshotSort ScreenshotsSort { get; set; } = ScreenshotSort.NameAscending;

    /// <summary>Screenshots slot 1: sub folder name.</summary>
    public string ScreenshotSlot1Folder { get; set; } = "title";

    /// <summary>Screenshots slot 1: row name.</summary>
    public ScreenshotLabel ScreenshotSlot1Label { get; set; } = ScreenshotLabel.TitleScreenshots;

    /// <summary>Screenshots slot 1: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot1OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 1: in use.</summary>
    public bool ScreenshotSlot1On { get; set; } = true;

    /// <summary>Screenshots slot 1: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot1Merge { get; set; } = true;

    /// <summary>Screenshots slot 2: sub folder name.</summary>
    public string ScreenshotSlot2Folder { get; set; } = "select";

    /// <summary>Screenshots slot 2: row name.</summary>
    public ScreenshotLabel ScreenshotSlot2Label { get; set; } = ScreenshotLabel.SelectScreenshots;

    /// <summary>Screenshots slot 2: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot2OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 2: in use.</summary>
    public bool ScreenshotSlot2On { get; set; } = true;

    /// <summary>Screenshots slot 2: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot2Merge { get; set; } = true;

    /// <summary>Screenshots slot 3: sub folder name.</summary>
    public string ScreenshotSlot3Folder { get; set; } = "gameplay";

    /// <summary>Screenshots slot 3: row name.</summary>
    public ScreenshotLabel ScreenshotSlot3Label { get; set; } = ScreenshotLabel.GameplayScreenshots;

    /// <summary>Screenshots slot 3: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot3OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 3: in use.</summary>
    public bool ScreenshotSlot3On { get; set; } = true;

    /// <summary>Screenshots slot 3: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot3Merge { get; set; } = true;

    /// <summary>Screenshots slot 4: sub folder name.</summary>
    public string ScreenshotSlot4Folder { get; set; } = "gameover";

    /// <summary>Screenshots slot 4: row name.</summary>
    public ScreenshotLabel ScreenshotSlot4Label { get; set; } = ScreenshotLabel.GameOverScreenshots;

    /// <summary>Screenshots slot 4: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot4OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 4: in use.</summary>
    public bool ScreenshotSlot4On { get; set; } = true;

    /// <summary>Screenshots slot 4: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot4Merge { get; set; } = true;

    /// <summary>Screenshots slot 5: sub folder name.</summary>
    public string ScreenshotSlot5Folder { get; set; } = "highscores";

    /// <summary>Screenshots slot 5: row name.</summary>
    public ScreenshotLabel ScreenshotSlot5Label { get; set; } = ScreenshotLabel.HighScoresScreenshots;

    /// <summary>Screenshots slot 5: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot5OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 5: in use.</summary>
    public bool ScreenshotSlot5On { get; set; } = true;

    /// <summary>Screenshots slot 5: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot5Merge { get; set; } = true;

    /// <summary>Screenshots slot 6: sub folder name.</summary>
    public string ScreenshotSlot6Folder { get; set; } = string.Empty;

    /// <summary>Screenshots slot 6: row name.</summary>
    public ScreenshotLabel ScreenshotSlot6Label { get; set; } = ScreenshotLabel.Own;

    /// <summary>Screenshots slot 6: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot6OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 6: in use.</summary>
    public bool ScreenshotSlot6On { get; set; }

    /// <summary>Screenshots slot 6: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot6Merge { get; set; } = true;

    /// <summary>Screenshots slot 7: sub folder name.</summary>
    public string ScreenshotSlot7Folder { get; set; } = string.Empty;

    /// <summary>Screenshots slot 7: row name.</summary>
    public ScreenshotLabel ScreenshotSlot7Label { get; set; } = ScreenshotLabel.Own;

    /// <summary>Screenshots slot 7: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot7OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 7: in use.</summary>
    public bool ScreenshotSlot7On { get; set; }

    /// <summary>Screenshots slot 7: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot7Merge { get; set; } = true;

    /// <summary>Screenshots slot 8: sub folder name.</summary>
    public string ScreenshotSlot8Folder { get; set; } = string.Empty;

    /// <summary>Screenshots slot 8: row name.</summary>
    public ScreenshotLabel ScreenshotSlot8Label { get; set; } = ScreenshotLabel.Own;

    /// <summary>Screenshots slot 8: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot8OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 8: in use.</summary>
    public bool ScreenshotSlot8On { get; set; }

    /// <summary>Screenshots slot 8: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot8Merge { get; set; } = true;

    /// <summary>Screenshots slot 9: sub folder name.</summary>
    public string ScreenshotSlot9Folder { get; set; } = string.Empty;

    /// <summary>Screenshots slot 9: row name.</summary>
    public ScreenshotLabel ScreenshotSlot9Label { get; set; } = ScreenshotLabel.Own;

    /// <summary>Screenshots slot 9: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot9OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 9: in use.</summary>
    public bool ScreenshotSlot9On { get; set; }

    /// <summary>Screenshots slot 9: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot9Merge { get; set; } = true;

    /// <summary>Screenshots slot 10: sub folder name.</summary>
    public string ScreenshotSlot10Folder { get; set; } = string.Empty;

    /// <summary>Screenshots slot 10: row name.</summary>
    public ScreenshotLabel ScreenshotSlot10Label { get; set; } = ScreenshotLabel.Own;

    /// <summary>Screenshots slot 10: the typed row name while the label is Own.</summary>
    public string ScreenshotSlot10OwnLabel { get; set; } = string.Empty;

    /// <summary>Screenshots slot 10: in use.</summary>
    public bool ScreenshotSlot10On { get; set; }

    /// <summary>Screenshots slot 10: its files go into the merged row (only while Display merges).</summary>
    public bool ScreenshotSlot10Merge { get; set; } = true;

    /// <summary>Edition words removed from the end of a title when editions are grouped, comma separated, case ignored.</summary>
    public string EditionWords { get; set; } = "remastered,remaster,definitive edition,enhanced edition,enhanced,hd,game of the year edition,game of the year,goty edition,goty,complete edition,deluxe edition,redux,directors cut,director s cut,german edition,deutsche version,history edition,premium,ultimate edition";

    /// <summary>The edition words are applied (library and detail page alike); off = no title is shortened by them.</summary>
    public bool RecognizeEditions { get; set; } = true;

    /// <summary>A roman numeral as the last word of a title counts as its arabic number when grouping.</summary>
    public bool RomanNumerals { get; set; } = true;

    /// <summary>Group demos and test builds with their game (never as the main version).</summary>
    public bool GroupDemosAndTestBuilds { get; set; }

    /// <summary>Words that mark a demo or test build at the end of a title, comma separated.</summary>
    public string DemoWords { get; set; } = "demo,beta,playtest,public test,test server,test branch";

    /// <summary>Titles that are never grouped and never shortened by edition words, comma separated.</summary>
    public string NeverGroupTitles { get; set; } = "DOOM,Resident Evil,Resident Evil 2,Resident Evil 4";

    /// <summary>NeverGroupTitles is applied; off = the list is ignored and kept.</summary>
    public bool NeverGroupTitlesEnabled { get; set; } = true;

    /// <summary>Hide Titles: HideDemos.</summary>
    public bool HideDemos { get; set; }

    /// <summary>Words of HideDemos, comma separated.</summary>
    public string HideDemosWords { get; set; } = "demo";

    /// <summary>Hide Titles: HideTestBuilds.</summary>
    public bool HideTestBuilds { get; set; }

    /// <summary>Words of HideTestBuilds, comma separated.</summary>
    public string HideTestBuildsWords { get; set; } = "beta,playtest,public test,test server,test branch";

    /// <summary>Hide Titles: HideSoundtracks.</summary>
    public bool HideSoundtracks { get; set; }

    /// <summary>Words of HideSoundtracks, comma separated.</summary>
    public string HideSoundtracksWords { get; set; } = "soundtrack,ost";

    /// <summary>Hide Titles: HideDlcAndPacks.</summary>
    public bool HideDlcAndPacks { get; set; }

    /// <summary>Words of HideDlcAndPacks, comma separated.</summary>
    public string HideDlcAndPacksWords { get; set; } = "dlc,expansion,season pass,pack";

    /// <summary>Hide Titles: HideProloguesAndTrials.</summary>
    public bool HideProloguesAndTrials { get; set; }

    /// <summary>Words of HideProloguesAndTrials, comma separated.</summary>
    public string HideProloguesAndTrialsWords { get; set; } = "prologue,trial";

    /// <summary>Hide Titles: HideTitles (the list of whole titles below) hides.</summary>
    public bool HideTitlesEnabled { get; set; }

    /// <summary>Titles that are always hidden (whole titles, compared as NeverHideTitles), one per line or comma separated.</summary>
    public string HideTitles { get; set; } = string.Empty;

    /// <summary>Hide Titles: NeverHideTitles (the list below) keeps its titles visible.</summary>
    public bool NeverHideTitlesEnabled { get; set; } = true;

    /// <summary>Titles that are never hidden, comma separated.</summary>
    public string NeverHideTitles { get; set; } = string.Empty;

    /// <summary>CharacterArt in the corner is hidden while the logo slot is empty.</summary>
    public bool ArtCharacterArtHideWithoutLogo { get; set; }

    /// <summary>Show only the detail page links whose host entry is listed in LinkHosts.</summary>
    public bool LinkHostLimiter { get; set; }

    /// <summary>Host entries shown when LinkHostLimiter is on, comma-separated; "official" = the links named "Official"/"Official Website".</summary>
    public string LinkHosts { get; set; } = "official,steampowered.com,epicgames.com,gog.com,ea.com,ubisoft.com,xbox.com,itch.io,rockstargames.com,playstation.com,nintendo.com,wikipedia.org,pcgamingwiki.com";

    /// <summary>Order of the host entries of the Links list on the admin page, comma-separated (all known hosts); the links of a game page follow it, links of other hosts come after them unsorted.</summary>
    public string LinkHostOrder { get; set; } = "official,steampowered.com,epicgames.com,gog.com,ea.com,ubisoft.com,xbox.com,itch.io,rockstargames.com,playstation.com,nintendo.com,wikipedia.org,pcgamingwiki.com,fandom.com,youtube.com,twitch.tv,reddit.com,discord.com,x.com,facebook.com,instagram.com,apps.apple.com,play.google.com";

    /// <summary>How the game page shows the links while LinkHostLimiter is on: as text or as one icon per link.</summary>
    public LinkDisplayMode LinkDisplay { get; set; } = LinkDisplayMode.Text;

    /// <summary>Show the same game from several stores as one tile (main version plus version list).</summary>
    public bool GroupGames { get; set; } = true;

    /// <summary>Also group editions (Remastered, Definitive Edition, HD and similar) with the base game.</summary>
    public bool GroupEditions { get; set; }

    /// <summary>Kind of the version marker: store icons or Jellyfin's number.</summary>
    public VersionMarkerKind MarkerKind { get; set; } = VersionMarkerKind.Icons;

    /// <summary>Corner of the version marker; Off = no marker.</summary>
    public VersionMarkerPosition MarkerPosition { get; set; } = VersionMarkerPosition.TopLeft;

    /// <summary>
    /// Most store icons on a tile (2-5; more stores = one fewer and "…"). In the top row beside Jellyfin's played check mark (MarkerPosition
    /// top, PlayedMark not Off) at most 4 fit (GameGroups.MaxIcons); a narrower tile shortens the row in the web client.
    /// </summary>
    public int MaxMarkerIcons { get; set; } = 3;

    /// <summary>Which tiles show the store icons: only games with several versions, or every game (its own icon when it has one version).</summary>
    public MarkerScope MarkerScope { get; set; } = MarkerScope.AllGames;

    /// <summary>Order that picks the main version of a group.</summary>
    public MainVersionOrder MainVersionOrder { get; set; } = MainVersionOrder.InstalledLastPlayedPlatform;

    /// <summary>Platform list for the main version, store keys comma-separated; stores not listed follow alphabetically before "custom".</summary>
    public string PlatformOrder { get; set; } = "steam,epic,gog,ea,ubisoft,xbox,battlenet,amazon,humble,itch,indiegala,rockstar,riot,playstation,bethesda,twitch,custom";

    /// <summary>Which game's cover is the poster of a collection (a Playnite series with games in at least two groups).</summary>
    public CollectionPosterOrder CollectionPosterOrder { get; set; } = CollectionPosterOrder.EarliestRelease;

    /// <summary>The folder with one sub folder per series holding poster and backdrop pictures, read only. Empty = Jellyfin's internal metadata path + Game Collections.</summary>
    public string CollectionPicturesFolder { get; set; } = string.Empty;

    /// <summary>
    /// The ticked detail page actions per store, entries "&lt;storeKey&gt;:&lt;Kind&gt;|&lt;Kind&gt;" comma-separated (GameStart.StartActionKind,
    /// "&lt;storeKey&gt;:" = none); a store not listed gets OpenInPlaynite and StartViaPlaynite.
    /// </summary>
    public string StartActions { get; set; } = "steam:OpenInPlaynite|StartViaPlaynite,epic:OpenInPlaynite|StartViaPlaynite,gog:OpenInPlaynite|StartViaPlaynite,ea:OpenInPlaynite|StartViaPlaynite,ubisoft:OpenInPlaynite|StartViaPlaynite,xbox:OpenInPlaynite|StartViaPlaynite,battlenet:OpenInPlaynite|StartViaPlaynite,amazon:OpenInPlaynite|StartViaPlaynite,humble:OpenInPlaynite|StartViaPlaynite,itch:OpenInPlaynite|StartViaPlaynite,indiegala:OpenInPlaynite|StartViaPlaynite,rockstar:OpenInPlaynite|StartViaPlaynite,riot:OpenInPlaynite|StartViaPlaynite,playstation:OpenInPlaynite|StartViaPlaynite,bethesda:OpenInPlaynite|StartViaPlaynite,twitch:OpenInPlaynite|StartViaPlaynite,custom:OpenInPlaynite|StartViaPlaynite";

    /// <summary>Who may have the server start a game on the PC (requests that do not come from the PC itself).</summary>
    public RemoteStartPermission RemoteStart { get; set; } = RemoteStartPermission.AdminsOnly;

    // OSD & Artwork tab. The defaults
    // change nothing on a game page; while ArtworkPlus is active ClientConfiguration sends no Artwork values at all.

    /// <summary>
    /// Settings From (Display): whose values draw the game pages' artwork. ArtworkPlus applies only while it is installed and active
    /// (GamesController.ArtworkPlusDraws); otherwise Playnite Bridge's own. Default ArtworkPlus, so a fresh install with ArtworkPlus uses it.
    /// </summary>
    public ArtworkSettingsSource ArtworkSettingsFrom { get; set; } = ArtworkSettingsSource.ArtworkPlus;

    /// <summary>Size of the game page's poster in percent of Jellyfin's own size, 10-200; 100 = Jellyfin.</summary>
    public int ArtPosterSizePercent { get; set; } = 100;

    /// <summary>Place of a poster below 100 % inside the space of the full-size poster.</summary>
    public ArtPosterAnchorMode ArtPosterAnchor { get; set; } = ArtPosterAnchorMode.Default;

    /// <summary>First stage of the logo slot of a game page.</summary>
    public ArtLogoArtSourceKind ArtLogoArtSource { get; set; } = ArtLogoArtSourceKind.VanillaLogo;

    /// <summary>Stage used when the source has no image.</summary>
    public ArtLogoArtFallbackKind ArtLogoArtFallback { get; set; } = ArtLogoArtFallbackKind.None;

    /// <summary>Stage used when the source and the fallback have no image.</summary>
    public ArtLogoArtFallbackKind ArtLogoArtSecondFallback { get; set; } = ArtLogoArtFallbackKind.None;

    /// <summary>Jellyfin's logo in percent of its slot (25vw x 16vh), 10-400 in steps of 5.</summary>
    public double ArtLogoArtLogoSizePercent { get; set; } = 100;

    /// <summary>Sideways shift of the logo in vw, -40..40.</summary>
    public double ArtLogoArtLogoOffsetVw { get; set; }

    /// <summary>Vertical shift of the logo in vh, -40..40.</summary>
    public double ArtLogoArtLogoVerticalOffsetVh { get; set; }

    /// <summary>Clearart in percent of the 25vw wide 16:9 box on the ribbon line, 10-400 in steps of 5.</summary>
    public double ArtLogoArtClearartSizePercent { get; set; } = 100;

    /// <summary>Sideways shift of the clearart in vw, -40..40.</summary>
    public double ArtLogoArtClearartOffsetVw { get; set; }

    /// <summary>Vertical shift of the clearart in vh, -40..40.</summary>
    public double ArtLogoArtClearartVerticalOffsetVh { get; set; }

    /// <summary>Side the characterart box of the logo slot is sized by.</summary>
    public ArtScaleMode ArtLogoArtCharacterartScaleMode { get; set; } = ArtScaleMode.Height;

    /// <summary>Height of the characterart box of the logo slot in vh, at least 1.</summary>
    public double ArtLogoArtCharacterartHeightVh { get; set; } = 20;

    /// <summary>Greatest width of that box in vw; 0 = the image's width.</summary>
    public double ArtLogoArtCharacterartMaxWidthVw { get; set; } = 12;

    /// <summary>Width of that box in vw, at least 1.</summary>
    public double ArtLogoArtCharacterartWidthVw { get; set; } = 11.5;

    /// <summary>Greatest height of that box in vh; 0 = the image's height.</summary>
    public double ArtLogoArtCharacterartMaxHeightVh { get; set; }

    /// <summary>Side the image keeps inside that box.</summary>
    public ArtAlignMode ArtLogoArtCharacterartHorizontalAlign { get; set; } = ArtAlignMode.Center;

    /// <summary>Sideways shift of that box in vw, -40..40.</summary>
    public double ArtLogoArtCharacterartOffsetVw { get; set; }

    /// <summary>Show every characterart image of the game in turn instead of the first one.</summary>
    public bool ArtLogoArtMultiImage { get; set; } = true;

    /// <summary>Order of the images in the logo slot.</summary>
    public ArtOrderMode ArtLogoArtOrderMode { get; set; } = ArtOrderMode.Shuffle;

    /// <summary>End after one pass instead of looping.</summary>
    public bool ArtLogoArtSinglePass { get; set; }

    /// <summary>Start at a random image (Sequential and Loop only).</summary>
    public bool ArtLogoArtRandomStart { get; set; }

    /// <summary>Keep a single image on screen with Play once.</summary>
    public bool ArtLogoArtStaySingleImageStatic { get; set; }

    /// <summary>How long each image stays in ms, 0-600000.</summary>
    public int ArtLogoArtCycleTimeMs { get; set; } = 5000;

    /// <summary>Fade between two images in ms, 0-60000, never above the display duration.</summary>
    public int ArtLogoArtFadeTimeMs { get; set; } = 1000;

    /// <summary>Show the game's characterart in a corner of its game page.</summary>
    public bool ArtCharacterArtShow { get; set; }

    /// <summary>Corner of the characterart.</summary>
    public ArtCharacterArtPositionKind ArtCharacterArtPosition { get; set; } = ArtCharacterArtPositionKind.TopRight;

    /// <summary>Side the top left box is sized by.</summary>
    public ArtScaleMode ArtCharacterArtTopLeftScaleMode { get; set; } = ArtScaleMode.Height;

    /// <summary>Height of the top left box in vh, at least 1.</summary>
    public double ArtCharacterArtTopLeftHeightVh { get; set; } = 20;

    /// <summary>Greatest width of the top left box in vw; 0 = the image's width.</summary>
    public double ArtCharacterArtTopLeftMaxWidthVw { get; set; } = 12;

    /// <summary>Width of the top left box in vw, at least 1.</summary>
    public double ArtCharacterArtTopLeftWidthVw { get; set; } = 11.5;

    /// <summary>Greatest height of the top left box in vh; 0 = the image's height.</summary>
    public double ArtCharacterArtTopLeftMaxHeightVh { get; set; }

    /// <summary>Side the image keeps inside the top left box.</summary>
    public ArtAlignMode ArtCharacterArtTopLeftHorizontalAlign { get; set; } = ArtAlignMode.Center;

    /// <summary>Sideways shift of the top left box in vw, -40..40.</summary>
    public double ArtCharacterArtTopLeftOffsetVw { get; set; } = 4;

    /// <summary>Extra sideways shift of the top left box in vw while the browser is in fullscreen, -40..40.</summary>
    public double ArtCharacterArtTopLeftFullscreenOffsetVw { get; set; } = -2.5;

    /// <summary>Side the top right box is sized by.</summary>
    public ArtScaleMode ArtCharacterArtTopRightScaleMode { get; set; } = ArtScaleMode.Height;

    /// <summary>Height of the top right box in vh, at least 1.</summary>
    public double ArtCharacterArtTopRightHeightVh { get; set; } = 20;

    /// <summary>Greatest width of the top right box in vw; 0 = the image's width.</summary>
    public double ArtCharacterArtTopRightMaxWidthVw { get; set; } = 12;

    /// <summary>Width of the top right box in vw, at least 1.</summary>
    public double ArtCharacterArtTopRightWidthVw { get; set; } = 11.5;

    /// <summary>Greatest height of the top right box in vh; 0 = the image's height.</summary>
    public double ArtCharacterArtTopRightMaxHeightVh { get; set; }

    /// <summary>Side the image keeps inside the top right box.</summary>
    public ArtAlignMode ArtCharacterArtTopRightHorizontalAlign { get; set; } = ArtAlignMode.Center;

    /// <summary>Sideways shift of the top right box in vw, -40..40.</summary>
    public double ArtCharacterArtTopRightOffsetVw { get; set; } = -5;

    /// <summary>Extra sideways shift of the top right box in vw while the browser is in fullscreen, -40..40.</summary>
    public double ArtCharacterArtTopRightFullscreenOffsetVw { get; set; } = 2.5;

    /// <summary>Side the bottom left box is sized by.</summary>
    public ArtScaleMode ArtCharacterArtBottomLeftScaleMode { get; set; } = ArtScaleMode.Height;

    /// <summary>Height of the bottom left box in vh, at least 1.</summary>
    public double ArtCharacterArtBottomLeftHeightVh { get; set; } = 30;

    /// <summary>Greatest width of the bottom left box in vw; 0 = the image's width.</summary>
    public double ArtCharacterArtBottomLeftMaxWidthVw { get; set; } = 30;

    /// <summary>Width of the bottom left box in vw, at least 1.</summary>
    public double ArtCharacterArtBottomLeftWidthVw { get; set; } = 11.5;

    /// <summary>Greatest height of the bottom left box in vh; 0 = the image's height.</summary>
    public double ArtCharacterArtBottomLeftMaxHeightVh { get; set; }

    /// <summary>Side the image keeps inside the bottom left box.</summary>
    public ArtAlignMode ArtCharacterArtBottomLeftHorizontalAlign { get; set; } = ArtAlignMode.Center;

    /// <summary>Sideways shift of the bottom left box in vw, -40..40.</summary>
    public double ArtCharacterArtBottomLeftOffsetVw { get; set; } = -1.5;

    /// <summary>Extra sideways shift of the bottom left box in vw while the browser is in fullscreen, -40..40.</summary>
    public double ArtCharacterArtBottomLeftFullscreenOffsetVw { get; set; }

    /// <summary>Side the bottom right box is sized by.</summary>
    public ArtScaleMode ArtCharacterArtBottomRightScaleMode { get; set; } = ArtScaleMode.Height;

    /// <summary>Height of the bottom right box in vh, at least 1.</summary>
    public double ArtCharacterArtBottomRightHeightVh { get; set; } = 30;

    /// <summary>Greatest width of the bottom right box in vw; 0 = the image's width.</summary>
    public double ArtCharacterArtBottomRightMaxWidthVw { get; set; } = 30;

    /// <summary>Width of the bottom right box in vw, at least 1.</summary>
    public double ArtCharacterArtBottomRightWidthVw { get; set; } = 11.5;

    /// <summary>Greatest height of the bottom right box in vh; 0 = the image's height.</summary>
    public double ArtCharacterArtBottomRightMaxHeightVh { get; set; }

    /// <summary>Side the image keeps inside the bottom right box.</summary>
    public ArtAlignMode ArtCharacterArtBottomRightHorizontalAlign { get; set; } = ArtAlignMode.Center;

    /// <summary>Sideways shift of the bottom right box in vw, -40..40.</summary>
    public double ArtCharacterArtBottomRightOffsetVw { get; set; } = 1.5;

    /// <summary>Extra sideways shift of the bottom right box in vw while the browser is in fullscreen, -40..40.</summary>
    public double ArtCharacterArtBottomRightFullscreenOffsetVw { get; set; }

    /// <summary>Show every characterart image of the game in turn instead of the first one.</summary>
    public bool ArtCharacterArtMultiImage { get; set; } = true;

    /// <summary>Order of the images.</summary>
    public ArtOrderMode ArtCharacterArtOrderMode { get; set; } = ArtOrderMode.Shuffle;

    /// <summary>End after one pass instead of looping.</summary>
    public bool ArtCharacterArtSinglePass { get; set; }

    /// <summary>Start at a random image (Sequential and Loop only).</summary>
    public bool ArtCharacterArtRandomStart { get; set; }

    /// <summary>Keep a single image on screen with Play once.</summary>
    public bool ArtCharacterArtStaySingleImageStatic { get; set; }

    /// <summary>How long each image stays in ms, at least 0.</summary>
    public int ArtCharacterArtCycleTimeMs { get; set; } = 5000;

    /// <summary>Fade between two images in ms, at least 0, never above the display duration.</summary>
    public int ArtCharacterArtFadeTimeMs { get; set; } = 1000;

    /// <summary>Wait ArtCharacterArtDelayMs before the first image (Multi image only).</summary>
    public bool ArtCharacterArtDelayEnabled { get; set; }

    /// <summary>The wait before the first image in ms, at least 0.</summary>
    public int ArtCharacterArtDelayMs { get; set; } = 5000;

    /// <summary>Own rotating backdrops on game pages (Backdrops: Detail View).</summary>
    public bool ArtBackdropsDetailShow { get; set; }

    /// <summary>How long each backdrop stays in ms, at least 1200, no upper limit.</summary>
    public int ArtBackdropsDetailCycleTimeMs { get; set; } = 10000;

    /// <summary>Slow zoom and pan of the backdrops.</summary>
    public bool ArtBackdropsDetailKenBurnsEnabled { get; set; } = true;

    /// <summary>Duration of one Ken Burns zoom in ms, at least 500.</summary>
    public int ArtBackdropsDetailKenBurnsZoomMs { get; set; } = 20000;

    /// <summary>Duration of one Ken Burns pan in ms, at least 500.</summary>
    public int ArtBackdropsDetailKenBurnsPanMs { get; set; } = 10000;

    /// <summary>Order of the backdrops.</summary>
    public ArtOrderMode ArtBackdropsDetailOrderMode { get; set; } = ArtOrderMode.Shuffle;

    /// <summary>Start at a random backdrop (Sequential only).</summary>
    public bool ArtBackdropsDetailRandomStart { get; set; }

    /// <summary>Own rotating backdrops on the games library page (Backdrops: Library View).</summary>
    public bool ArtBackdropsLibraryShow { get; set; }

    /// <summary>How long each backdrop stays in ms, at least 1200, no upper limit.</summary>
    public int ArtBackdropsLibraryCycleTimeMs { get; set; } = 10000;

    /// <summary>Slow zoom and pan of the backdrops.</summary>
    public bool ArtBackdropsLibraryKenBurnsEnabled { get; set; } = true;

    /// <summary>Duration of one Ken Burns zoom in ms, at least 500.</summary>
    public int ArtBackdropsLibraryKenBurnsZoomMs { get; set; } = 20000;

    /// <summary>Duration of one Ken Burns pan in ms, at least 500.</summary>
    public int ArtBackdropsLibraryKenBurnsPanMs { get; set; } = 10000;

    /// <summary>Order of the backdrops.</summary>
    public ArtOrderMode ArtBackdropsLibraryOrderMode { get; set; } = ArtOrderMode.Shuffle;

    /// <summary>Start at a random backdrop (Sequential only).</summary>
    public bool ArtBackdropsLibraryRandomStart { get; set; }

    /// <summary>Own rotating backdrops on collection pages (Backdrops: Collection View).</summary>
    public bool ArtBackdropsCollectionShow { get; set; }

    /// <summary>How long each backdrop stays in ms, at least 1200, no upper limit.</summary>
    public int ArtBackdropsCollectionCycleTimeMs { get; set; } = 10000;

    /// <summary>Slow zoom and pan of the backdrops.</summary>
    public bool ArtBackdropsCollectionKenBurnsEnabled { get; set; } = true;

    /// <summary>Duration of one Ken Burns zoom in ms, at least 500.</summary>
    public int ArtBackdropsCollectionKenBurnsZoomMs { get; set; } = 20000;

    /// <summary>Duration of one Ken Burns pan in ms, at least 500.</summary>
    public int ArtBackdropsCollectionKenBurnsPanMs { get; set; } = 10000;

    /// <summary>Where the backdrops come from: the own pictures, the games' backdrops, own then games, or all mixed (reshuffled every round).</summary>
    public CollectionBackdropSource ArtBackdropsCollectionSource { get; set; } = CollectionBackdropSource.Games;

    /// <summary>Own pictures: only the first or all of them.</summary>
    public CollectionImageCount ArtBackdropsCollectionOwnImages { get; set; } = CollectionImageCount.Multiple;

    /// <summary>Order of the own pictures (Multiple only).</summary>
    public ArtOrderMode ArtBackdropsCollectionOwnImageOrder { get; set; } = ArtOrderMode.Sequential;

    /// <summary>Games: each game's main backdrop or all of its backdrops.</summary>
    public CollectionImageCount ArtBackdropsCollectionGameImages { get; set; } = CollectionImageCount.Single;

    /// <summary>Order of one game's backdrops (Multiple only).</summary>
    public ArtOrderMode ArtBackdropsCollectionGameImageOrder { get; set; } = ArtOrderMode.Sequential;

    /// <summary>Order of the games.</summary>
    public CollectionGameOrder ArtBackdropsCollectionGameOrder { get; set; } = CollectionGameOrder.ReleaseAscending;

    /// <summary>Start at a random game and go on in order (a sorted game order only).</summary>
    public bool ArtBackdropsCollectionRandomStart { get; set; } = true;

    /// <summary>
    /// Settings From (Player Artwork): whose values draw the game artwork in the video player. VideoOSD applies only while its Artwork addon is
    /// on (GamesController.VideoOsdDraws; in the browser also html[data-jvosd-artwork]); otherwise Playnite Bridge's own. Default VideoOSD.
    /// Not named PlayerArt*: every PlayerArt* property is a value of the player artwork itself (ClientPlayerArtwork, the admin page's mirror).
    /// </summary>
    public PlayerArtworkSettingsSource OsdArtworkSettingsFrom { get; set; } = PlayerArtworkSettingsSource.VideoOSD;

    /// <summary>Own artwork in the video player while a game's trailer or extra plays and VideoOSD Tweaks &amp; Candy's Artwork addon does not draw it.</summary>
    public bool PlayerArtEnabled { get; set; }

    /// <summary>Show the clearlogo layer of the player artwork.</summary>
    public bool PlayerArtLogoEnabled { get; set; } = true;

    /// <summary>Item the clearlogo comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtLogoSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the clearlogo; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtLogoHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the clearlogo; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtLogoVertical { get; set; } = PlayerArtVertical.Top;

    /// <summary>Distance of the clearlogo in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtLogoOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the clearlogo in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtLogoOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the clearlogo in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtLogoOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the clearlogo in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtLogoOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the clearlogo in rem, 0..100.</summary>
    public double PlayerArtLogoMaxHeightRem { get; set; } = 6.75;

    /// <summary>Greatest width of the clearlogo in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtLogoMaxWidthRem { get; set; } = 17.42;

    /// <summary>Stacking order of the clearlogo among the layers, 1..9.</summary>
    public int PlayerArtLogoZIndex { get; set; } = 8;

    /// <summary>Show the clearart layer of the player artwork.</summary>
    public bool PlayerArtClearartEnabled { get; set; }

    /// <summary>Item the clearart comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtClearartSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the clearart; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtClearartHorizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the clearart; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtClearartVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the clearart in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtClearartOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the clearart in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtClearartOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the clearart in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtClearartOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the clearart in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtClearartOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the clearart in rem, 0..100.</summary>
    public double PlayerArtClearartMaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the clearart in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtClearartMaxWidthRem { get; set; }

    /// <summary>Stacking order of the clearart among the layers, 1..9.</summary>
    public int PlayerArtClearartZIndex { get; set; } = 7;

    /// <summary>Show the disc layer of the player artwork.</summary>
    public bool PlayerArtDiscEnabled { get; set; }

    /// <summary>Item the disc comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtDiscSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the disc; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtDiscHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the disc; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtDiscVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the disc in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtDiscOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the disc in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtDiscOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the disc in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtDiscOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the disc in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtDiscOffsetBottomRem { get; set; } = -1.43;

    /// <summary>Greatest height of the disc in rem, 0..100.</summary>
    public double PlayerArtDiscMaxHeightRem { get; set; } = 18.87;

    /// <summary>Greatest width of the disc in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtDiscMaxWidthRem { get; set; }

    /// <summary>Stacking order of the disc among the layers, 1..9.</summary>
    public int PlayerArtDiscZIndex { get; set; } = 3;

    /// <summary>Show several discs of a game in the player artwork: every disc file of the game (disc, cdart, discart and their numbered forms, identical pictures once) up to Max Discs, the first in front, the others behind it shifted by a fixed distance; off shows only the first disc.</summary>
    public bool PlayerArtDiscShowMultiple { get; set; }

    /// <summary>Most discs shown with Show Multiple Discs, 1..20 (the web script cuts a value outside to the range).</summary>
    public int PlayerArtDiscMaxCount { get; set; } = 6;

    /// <summary>Show the characterart layer of the player artwork.</summary>
    public bool PlayerArtCharacterArtEnabled { get; set; }

    /// <summary>Item the characterart comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtCharacterArtSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the characterart; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtCharacterArtHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the characterart; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtCharacterArtVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the characterart in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtCharacterArtOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the characterart in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtCharacterArtOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the characterart in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtCharacterArtOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the characterart in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtCharacterArtOffsetBottomRem { get; set; } = -1.43;

    /// <summary>Greatest height of the characterart in rem, 0..100.</summary>
    public double PlayerArtCharacterArtMaxHeightRem { get; set; } = 18.87;

    /// <summary>Greatest width of the characterart in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtCharacterArtMaxWidthRem { get; set; }

    /// <summary>Stacking order of the characterart among the layers, 1..9.</summary>
    public int PlayerArtCharacterArtZIndex { get; set; } = 3;

    /// <summary>Show several characterart images of a game in turn; off shows only the first.</summary>
    public bool PlayerArtCharacterArtMultiImage { get; set; }

    /// <summary>Milliseconds each characterart image stays, at least 1000 (the web script cuts a smaller value to 1000).</summary>
    public int PlayerArtCharacterArtCycleTimeMs { get; set; } = 5000;

    /// <summary>Milliseconds the change between two characterart images takes (never longer than the time an image stays).</summary>
    public int PlayerArtCharacterArtFadeTimeMs { get; set; } = 1000;

    /// <summary>Order of the characterart images: in turn (Sequential) or shuffled without repeats in a round (Shuffle).</summary>
    public ArtOrderMode PlayerArtCharacterArtOrderMode { get; set; } = ArtOrderMode.Shuffle;

    /// <summary>With Sequential: start at a random image and go on in order.</summary>
    public bool PlayerArtCharacterArtRandomStart { get; set; }

    /// <summary>Show the box front layer of the player artwork.</summary>
    public bool PlayerArtBoxFrontEnabled { get; set; }

    /// <summary>Item the box front comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtBoxFrontSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the box front; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBoxFrontHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the box front; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBoxFrontVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the box front in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBoxFrontOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the box front in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBoxFrontOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the box front in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBoxFrontOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the box front in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBoxFrontOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the box front in rem, 0..100.</summary>
    public double PlayerArtBoxFrontMaxHeightRem { get; set; } = 19.6;

    /// <summary>Greatest width of the box front in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBoxFrontMaxWidthRem { get; set; } = 14.1;

    /// <summary>Stacking order of the box front among the layers, 1..9.</summary>
    public int PlayerArtBoxFrontZIndex { get; set; } = 4;

    /// <summary>Show the box back layer of the player artwork.</summary>
    public bool PlayerArtBoxBackEnabled { get; set; }

    /// <summary>Item the box back comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtBoxBackSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the box back; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBoxBackHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the box back; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBoxBackVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the box back in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBoxBackOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the box back in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBoxBackOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the box back in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBoxBackOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the box back in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBoxBackOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the box back in rem, 0..100.</summary>
    public double PlayerArtBoxBackMaxHeightRem { get; set; } = 19.6;

    /// <summary>Greatest width of the box back in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBoxBackMaxWidthRem { get; set; } = 14.1;

    /// <summary>Stacking order of the box back among the layers, 1..9.</summary>
    public int PlayerArtBoxBackZIndex { get; set; } = 4;

    /// <summary>Show the box spine layer of the player artwork.</summary>
    public bool PlayerArtBoxSpineEnabled { get; set; }

    /// <summary>Item the box spine comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtBoxSpineSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the box spine; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBoxSpineHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the box spine; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBoxSpineVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the box spine in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBoxSpineOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the box spine in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBoxSpineOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the box spine in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBoxSpineOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the box spine in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBoxSpineOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the box spine in rem, 0..100.</summary>
    public double PlayerArtBoxSpineMaxHeightRem { get; set; } = 19.6;

    /// <summary>Greatest width of the box spine in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBoxSpineMaxWidthRem { get; set; }

    /// <summary>Stacking order of the box spine among the layers, 1..9.</summary>
    public int PlayerArtBoxSpineZIndex { get; set; } = 4;

    /// <summary>Show the 3D box layer of the player artwork.</summary>
    public bool PlayerArtBox3DEnabled { get; set; }

    /// <summary>Item the 3D box comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtBox3DSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the 3D box; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBox3DHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the 3D box; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBox3DVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the 3D box in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBox3DOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the 3D box in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBox3DOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the 3D box in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBox3DOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the 3D box in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBox3DOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the 3D box in rem, 0..100.</summary>
    public double PlayerArtBox3DMaxHeightRem { get; set; } = 19.6;

    /// <summary>Greatest width of the 3D box in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBox3DMaxWidthRem { get; set; } = 17.42;

    /// <summary>Stacking order of the 3D box among the layers, 1..9.</summary>
    public int PlayerArtBox3DZIndex { get; set; } = 4;

    /// <summary>Show the square picture layer of the player artwork.</summary>
    public bool PlayerArtSquareEnabled { get; set; }

    /// <summary>Item the square picture comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtSquareSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the square picture; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtSquareHorizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the square picture; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtSquareVertical { get; set; } = PlayerArtVertical.Top;

    /// <summary>Distance of the square picture in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtSquareOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the square picture in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtSquareOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the square picture in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtSquareOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the square picture in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtSquareOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the square picture in rem, 0..100.</summary>
    public double PlayerArtSquareMaxHeightRem { get; set; } = 6.75;

    /// <summary>Greatest width of the square picture in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtSquareMaxWidthRem { get; set; } = 6.75;

    /// <summary>Stacking order of the square picture among the layers, 1..9.</summary>
    public int PlayerArtSquareZIndex { get; set; } = 5;

    /// <summary>Show the icon layer of the player artwork.</summary>
    public bool PlayerArtIconEnabled { get; set; }

    /// <summary>Item the icon comes from: only a game has one, so the extra alone gives none (Default and Game are the game).</summary>
    public PlayerArtSource PlayerArtIconSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the icon; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtIconHorizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the icon; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtIconVertical { get; set; } = PlayerArtVertical.Top;

    /// <summary>Distance of the icon in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtIconOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the icon in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtIconOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the icon in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtIconOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the icon in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtIconOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the icon in rem, 0..100.</summary>
    public double PlayerArtIconMaxHeightRem { get; set; } = 6.75;

    /// <summary>Greatest width of the icon in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtIconMaxWidthRem { get; set; } = 6.75;

    /// <summary>Stacking order of the icon among the layers, 1..9.</summary>
    public int PlayerArtIconZIndex { get; set; } = 5;

    /// <summary>Show the poster layer of the player artwork.</summary>
    public bool PlayerArtPosterEnabled { get; set; }

    /// <summary>Item the poster comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtPosterSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the poster; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtPosterHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the poster; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtPosterVertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the poster in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtPosterOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the poster in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtPosterOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the poster in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtPosterOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the poster in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtPosterOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the poster in rem, 0..100.</summary>
    public double PlayerArtPosterMaxHeightRem { get; set; } = 19.6;

    /// <summary>Greatest width of the poster in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtPosterMaxWidthRem { get; set; } = 17.42;

    /// <summary>Stacking order of the poster among the layers, 1..9.</summary>
    public int PlayerArtPosterZIndex { get; set; } = 4;

    /// <summary>Show the thumb layer of the player artwork.</summary>
    public bool PlayerArtThumbEnabled { get; set; }

    /// <summary>Item the thumb comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtThumbSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the thumb; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtThumbHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the thumb; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtThumbVertical { get; set; } = PlayerArtVertical.Center;

    /// <summary>Distance of the thumb in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtThumbOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the thumb in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtThumbOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the thumb in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtThumbOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the thumb in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtThumbOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the thumb in rem, 0..100.</summary>
    public double PlayerArtThumbMaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the thumb in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtThumbMaxWidthRem { get; set; }

    /// <summary>Stacking order of the thumb among the layers, 1..9.</summary>
    public int PlayerArtThumbZIndex { get; set; } = 3;

    /// <summary>Show the banner layer of the player artwork.</summary>
    public bool PlayerArtBannerEnabled { get; set; }

    /// <summary>Item the banner comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBannerSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the banner; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBannerHorizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the banner; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBannerVertical { get; set; } = PlayerArtVertical.Top;

    /// <summary>Distance of the banner in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBannerOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the banner in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBannerOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the banner in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBannerOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the banner in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBannerOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the banner in rem, 0..100.</summary>
    public double PlayerArtBannerMaxHeightRem { get; set; } = 5.44;

    /// <summary>Greatest width of the banner in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBannerMaxWidthRem { get; set; }

    /// <summary>Stacking order of the banner among the layers, 1..9.</summary>
    public int PlayerArtBannerZIndex { get; set; } = 5;

    /// <summary>Show the first backdrop layer of the player artwork.</summary>
    public bool PlayerArtBackdropEnabled { get; set; }

    /// <summary>Item the first backdrop comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdropSource { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the first backdrop; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdropHorizontal { get; set; } = PlayerArtHorizontal.Left;

    /// <summary>Vertical place of the first backdrop; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdropVertical { get; set; } = PlayerArtVertical.Center;

    /// <summary>Distance of the first backdrop in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdropOffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the first backdrop in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdropOffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the first backdrop in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdropOffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the first backdrop in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdropOffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the first backdrop in rem, 0..100.</summary>
    public double PlayerArtBackdropMaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the first backdrop in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdropMaxWidthRem { get; set; }

    /// <summary>Stacking order of the first backdrop among the layers, 1..9.</summary>
    public int PlayerArtBackdropZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 1 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop1Enabled { get; set; }

    /// <summary>Item the backdrop 1 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop1Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 1; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop1Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 1; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop1Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 1 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop1OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 1 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop1OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 1 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop1OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 1 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop1OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 1 in rem, 0..100.</summary>
    public double PlayerArtBackdrop1MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 1 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop1MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 1 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop1ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 2 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop2Enabled { get; set; }

    /// <summary>Item the backdrop 2 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop2Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 2; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop2Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 2; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop2Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 2 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop2OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 2 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop2OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 2 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop2OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 2 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop2OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 2 in rem, 0..100.</summary>
    public double PlayerArtBackdrop2MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 2 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop2MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 2 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop2ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 3 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop3Enabled { get; set; }

    /// <summary>Item the backdrop 3 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop3Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 3; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop3Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 3; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop3Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 3 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop3OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 3 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop3OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 3 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop3OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 3 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop3OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 3 in rem, 0..100.</summary>
    public double PlayerArtBackdrop3MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 3 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop3MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 3 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop3ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 4 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop4Enabled { get; set; }

    /// <summary>Item the backdrop 4 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop4Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 4; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop4Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 4; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop4Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 4 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop4OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 4 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop4OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 4 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop4OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 4 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop4OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 4 in rem, 0..100.</summary>
    public double PlayerArtBackdrop4MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 4 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop4MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 4 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop4ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 5 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop5Enabled { get; set; }

    /// <summary>Item the backdrop 5 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop5Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 5; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop5Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 5; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop5Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 5 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop5OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 5 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop5OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 5 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop5OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 5 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop5OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 5 in rem, 0..100.</summary>
    public double PlayerArtBackdrop5MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 5 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop5MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 5 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop5ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 6 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop6Enabled { get; set; }

    /// <summary>Item the backdrop 6 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop6Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 6; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop6Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 6; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop6Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 6 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop6OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 6 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop6OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 6 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop6OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 6 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop6OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 6 in rem, 0..100.</summary>
    public double PlayerArtBackdrop6MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 6 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop6MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 6 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop6ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 7 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop7Enabled { get; set; }

    /// <summary>Item the backdrop 7 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop7Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 7; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop7Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 7; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop7Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 7 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop7OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 7 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop7OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 7 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop7OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 7 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop7OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 7 in rem, 0..100.</summary>
    public double PlayerArtBackdrop7MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 7 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop7MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 7 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop7ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 8 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop8Enabled { get; set; }

    /// <summary>Item the backdrop 8 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop8Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 8; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop8Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 8; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop8Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 8 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop8OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 8 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop8OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 8 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop8OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 8 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop8OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 8 in rem, 0..100.</summary>
    public double PlayerArtBackdrop8MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 8 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop8MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 8 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop8ZIndex { get; set; } = 2;

    /// <summary>Show the backdrop 9 layer of the player artwork.</summary>
    public bool PlayerArtBackdrop9Enabled { get; set; }

    /// <summary>Item the backdrop 9 comes from: the extra, then its game (Default), only the extra or only the game.</summary>
    public PlayerArtSource PlayerArtBackdrop9Source { get; set; } = PlayerArtSource.Default;

    /// <summary>Horizontal place of the backdrop 9; Center uses no offset.</summary>
    public PlayerArtHorizontal PlayerArtBackdrop9Horizontal { get; set; } = PlayerArtHorizontal.Right;

    /// <summary>Vertical place of the backdrop 9; Center uses no offset.</summary>
    public PlayerArtVertical PlayerArtBackdrop9Vertical { get; set; } = PlayerArtVertical.Bottom;

    /// <summary>Distance of the backdrop 9 in rem from the left end of the control bar, -20..70, with Left only.</summary>
    public double PlayerArtBackdrop9OffsetLeftRem { get; set; } = 4.1;

    /// <summary>Distance of the backdrop 9 in rem from the right end of the control bar, -20..70, with Right only.</summary>
    public double PlayerArtBackdrop9OffsetRightRem { get; set; } = 5.01;

    /// <summary>Distance of the backdrop 9 in rem below the OSD header, -20..70, with Top only.</summary>
    public double PlayerArtBackdrop9OffsetTopRem { get; set; } = -1.47;

    /// <summary>Distance of the backdrop 9 in rem above the control bar, -20..70, with Bottom only.</summary>
    public double PlayerArtBackdrop9OffsetBottomRem { get; set; } = -1.72;

    /// <summary>Greatest height of the backdrop 9 in rem, 0..100.</summary>
    public double PlayerArtBackdrop9MaxHeightRem { get; set; } = 13.06;

    /// <summary>Greatest width of the backdrop 9 in rem, 0..100; 0 = Auto, only the free space.</summary>
    public double PlayerArtBackdrop9MaxWidthRem { get; set; }

    /// <summary>Stacking order of the backdrop 9 among the layers, 1..9.</summary>
    public int PlayerArtBackdrop9ZIndex { get; set; } = 2;

    /// <summary>The clearart layer shows the clearlogo when the items have no clearart, at the clearlogo's greatest size.</summary>
    public bool PlayerArtClearartFallbackToLogo { get; set; }

    /// <summary>The disc turns while the video plays.</summary>
    public bool PlayerArtDiscSpinning { get; set; } = true;

    /// <summary>Seconds of one turn of the disc, at least 1.</summary>
    public int PlayerArtDiscRotationSeconds { get; set; } = 15;

    /// <summary>Hide the clearlogo while the clearart layer shows the clearlogo as its fallback.</summary>
    public bool PlayerArtHideLogoWhenClearartFallback { get; set; }

    /// <summary>Hide the clearlogo while the clearart layer shows a real clearart.</summary>
    public bool PlayerArtHideLogoWhenClearartAvailable { get; set; }
}
