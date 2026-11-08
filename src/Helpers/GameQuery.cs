using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>The parameters of Jellyfin's Items query that the games list answers; names as in ItemsController.GetItems.</summary>
public sealed record GameQueryRequest
{
    public IReadOnlyList<ItemSortBy> SortBy { get; init; } = Array.Empty<ItemSortBy>();

    public IReadOnlyList<SortOrder> SortOrder { get; init; } = Array.Empty<SortOrder>();

    public int? StartIndex { get; init; }

    public int? Limit { get; init; }

    public string? NameStartsWith { get; init; }

    public string? NameLessThan { get; init; }

    public IReadOnlyList<ItemFilter> Filters { get; init; } = Array.Empty<ItemFilter>();

    public bool? IsFavorite { get; init; }

    public bool? IsPlayed { get; init; }

    // The rule of the played and unplayed filters and of isPlayed (PluginConfiguration.PlayedMark).
    public PlayedMarkMode PlayedMark { get; init; } = PlayedMarkMode.Started;

    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public IReadOnlyList<Guid> GenreIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();

    public IReadOnlyList<string> OfficialRatings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    // Developers (Playnite company GUID) and publishers (VirtualIds.Publisher): the list.html grids of the detail page links.
    public IReadOnlyList<Guid> PersonIds { get; init; } = Array.Empty<Guid>();

    public IReadOnlyList<Guid> StudioIds { get; init; } = Array.Empty<Guid>();

    // parentId = a collection folder: only the Playnite entries of that series (ids); null = no such restriction.
    public IReadOnlySet<string>? VersionIds { get; init; }

    // The local extras of a group (Jellyfin's meaning: true = has one of that kind, false = has none); answered from ExtraKinds.
    public bool? HasTrailer { get; init; }

    public bool? HasThemeSong { get; init; }

    public bool? HasThemeVideo { get; init; }

    public bool? HasSpecialFeature { get; init; }

    // The kinds of local extras of a group; set only while one of the four filters above is (WithExtras), null = no group has any.
    public Func<GameGroup, IReadOnlyCollection<ExtraKind>>? ExtraKinds { get; init; }

    // Conditions the games list does not check (a game has no media of its own): asking for them gives an empty result.
    public bool? HasSubtitles { get; init; }

    public IReadOnlyList<VideoType> VideoTypes { get; init; } = Array.Empty<VideoType>();

    public bool? Is3D { get; init; }

    public bool? IsHd { get; init; }

    public bool? Is4K { get; init; }
}

/// <summary>Filtering, sorting and paging of the games list; pure, no request or file access.</summary>
public static class GameQuery
{
    /// <summary>
    /// Prefix of the filter entries the plugin adds to Jellyfin's own filter lists: the Movies and Favorites tabs send them back
    /// as tags, list.html as genre ids (VirtualIds.Filter). Reserved: a real tag starting with it counts as a filter entry.
    /// </summary>
    public const string PseudoPrefix = "pb:";

    public const string Installed = "pb:installed";

    public const string NotInstalled = "pb:notinstalled";

    // Store key and name per Playnite library plugin id (the built-in libraries and a few common extensions); a new store needs one line here.
    internal static readonly Dictionary<Guid, (string Key, string Name)> StoreNames = new()
    {
        [Guid.Parse("E3C26A3D-D695-4CB7-A769-5FF7612C7EDD")] = ("battlenet", "Battle.net"),
        [Guid.Parse("0E2E793E-E0DD-4447-835C-C44A1FD506EC")] = ("bethesda", "Bethesda"),
        [Guid.Parse("00000002-DBD1-46C6-B5D0-B1BA559D10E4")] = ("epic", "Epic"),
        [Guid.Parse("AEBE8B7C-6DC3-4A66-AF31-E7375C6B5E9E")] = ("gog", "GOG"),
        [Guid.Parse("00000001-EBB2-4EEC-ABCB-7C89937A42BB")] = ("itch", "itch.io"),
        // The EA app library uses the id of the former Origin library.
        [Guid.Parse("85DD7072-2F20-4E76-A007-41035E390724")] = ("ea", "EA"),
        [Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB")] = ("steam", "Steam"),
        [Guid.Parse("E2A7D494-C138-489D-BB3F-1D786BEEB675")] = ("twitch", "Twitch"),
        [Guid.Parse("C2F038E5-8B92-4877-91F1-DA9094155FC5")] = ("ubisoft", "Ubisoft"),
        [Guid.Parse("96e8c4bc-ec5c-4c8b-87e7-18ee5a690626")] = ("humble", "Humble"),
        [Guid.Parse("7e4fbb5e-2ae3-48d4-8ba0-6b30e7a4e287")] = ("xbox", "Xbox"),
        [Guid.Parse("402674cd-4af6-4886-b6ec-0e695bfa0688")] = ("amazon", "Amazon"),
        [Guid.Parse("e4ac81cb-1b1a-4ec9-8639-9a9633989a71")] = ("playstation", "PlayStation"),
        [Guid.Parse("88409022-088a-4de8-805a-fdbac291f00a")] = ("rockstar", "Rockstar"),
        [Guid.Parse("91d13c6f-63d3-42ed-a100-6f811a8387ea")] = ("riot", "Riot"),
        [Guid.Parse("f7da6eb0-17d7-497c-92fd-347050914954")] = ("indiegala", "Indiegala")
    };

    /// <summary>
    /// A game's store by its Playnite library plugin: the table entry; no plugin (Guid.Empty) = custom game; any other plugin =
    /// "plugin-" + its id with the name of the game's Playnite source (<paramref name="sourceName"/>), else "Unknown".
    /// </summary>
    public static (string Key, string Name) Store(Guid pluginId, string? sourceName)
    {
        if (StoreNames.TryGetValue(pluginId, out var store))
        {
            return store;
        }

        return pluginId == Guid.Empty
            ? ("custom", "Custom")
            : ("plugin-" + pluginId.ToString("N"), string.IsNullOrEmpty(sourceName) ? "Unknown" : sourceName);
    }

    /// <summary>The Steam AppID: Playnite's GameId of a Steam game; null for every other store (GOG ids are numeric too).</summary>
    public static string? SteamAppId(string storeKey, string storeGameId) => storeKey == "steam" ? storeGameId : null;

    /// <summary>The filter entry of a Playnite feature.</summary>
    public static string FeatureFilter(string feature) => PseudoPrefix + "feature:" + feature;

    /// <summary>The filter entry of a completion status.</summary>
    public static string StatusFilter(string status) => PseudoPrefix + "status:" + status;

    /// <summary>The filter entry of a source: the store name as the user sees it (GameGroups.StoreLabel).</summary>
    public static string SourceFilter(string name) => PseudoPrefix + "source:" + name;

    /// <summary>
    /// The filter entry of a kind of local extra ("pb:extra:trailer", "special", "themesong", "themevideo"): never a checkbox of its own, the
    /// web script shows Jellyfin's own checkbox of that kind for it.
    /// </summary>
    public static string ExtraFilter(ExtraKind kind) => PseudoPrefix + "extra:" + kind.ToString().ToLowerInvariant();

    /// <summary>The kinds of local extras that at least one of <paramref name="groups"/> has in <paramref name="index"/> (ExtrasIndex.ForGroup).</summary>
    public static IReadOnlySet<ExtraKind> ExtraKindsOf(ExtrasIndex index, IEnumerable<GameGroup> groups) =>
        groups.SelectMany(index.ForGroup).Select(e => e.Kind).Where(k => k != ExtraKind.Soundtrack).ToHashSet();

    /// <summary>
    /// <paramref name="q"/> with the kinds of local extras per group (ExtrasIndex.ForGroup of <paramref name="index"/>) when it asks for one of
    /// hasTrailer, hasSpecialFeature, hasThemeSong, hasThemeVideo; else <paramref name="q"/> itself, and <paramref name="index"/> is not called.
    /// </summary>
    public static GameQueryRequest WithExtras(GameQueryRequest q, Func<ExtrasIndex> index)
    {
        if (q.HasTrailer is null && q.HasSpecialFeature is null && q.HasThemeSong is null && q.HasThemeVideo is null)
        {
            return q;
        }

        var extras = index();
        return q with { ExtraKinds = g => extras.ForGroup(g).Select(e => e.Kind).ToHashSet() };
    }

    /// <summary>
    /// The name of a feature, completion status or source of the given games whose filter entry has the id <paramref name="id"/>
    /// (VirtualIds.Filter, the links of a game page), without prefix; null for any other id.
    /// </summary>
    public static string? FilterName(IEnumerable<GameEntry> games, Guid id) =>
        games.SelectMany(g => g.Features.Select(FeatureFilter).Concat(g.CompletionStatus is string status ? new[] { StatusFilter(status) } : Array.Empty<string>())
                .Append(SourceFilter(GameGroups.StoreLabel(g))))
            .Distinct(StringComparer.Ordinal)
            .Where(value => VirtualIds.Filter(value) == id)
            .Select(value => value.Substring(value.IndexOf(':', PseudoPrefix.Length) + 1))
            .FirstOrDefault();

    /// <summary>The filter entries for the Movies and Favorites tabs' filter dialog (Items/Filters, as tags): installed state, source, features, completion status.</summary>
    public static string[] PseudoTags(IEnumerable<GameEntry> games) => Distinct(games.SelectMany(g => PseudoValues(g, false)));

    /// <summary>
    /// The filter entries for list.html's filter menu (Items/Filters2, as genres): as PseudoTags plus tags, age rating and year,
    /// each with the derived id VirtualIds.Filter, so the menu sends them back as genre ids.
    /// </summary>
    public static NameGuidPair[] PseudoGenres(IEnumerable<GameEntry> games) =>
        Distinct(games.SelectMany(g => PseudoValues(g, true))).Select(v => new NameGuidPair { Id = VirtualIds.Filter(v), Name = v }).ToArray();

    /// <summary>
    /// A name of an Items/Filters answer without the characters &lt; &gt; ": Jellyfin's filter dialog writes the names into its HTML
    /// unescaped (components/filterdialog/filterdialog.js renderOptions, data-filter). "&amp;" stays, so "Card &amp; Board Game" shows as it is.
    /// The filters compare both sides through it, so the cleaned name of the dialog and the raw name both find the game. Runs of spaces
    /// left behind become one, and the ends are trimmed.
    /// </summary>
    public static string FilterSafe(string value) =>
        string.Join(' ', string.Concat(value.Where(c => c is not ('<' or '>' or '"'))).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The filter entries go only to clients that run our script: it marks its own filter requests with pbFilters=1.</summary>
    public static bool ShowsFilterEntries(string? marker) => marker == "1";

    private static string[] Distinct(IEnumerable<string> values) =>
        values.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IEnumerable<string> PseudoValues(GameEntry game, bool listMenu)
    {
        yield return game.Installed ? Installed : NotInstalled;
        yield return SourceFilter(GameGroups.StoreLabel(game));

        foreach (var feature in game.Features)
        {
            yield return FeatureFilter(feature);
        }

        if (game.CompletionStatus is string status)
        {
            yield return StatusFilter(status);
        }

        if (!listMenu)
        {
            yield break;
        }

        foreach (var tag in game.Tags)
        {
            yield return PseudoPrefix + "tag:" + tag;
        }

        if (OfficialRating(game) is string rating)
        {
            yield return PseudoPrefix + "rating:" + rating;
        }

        if (game.Year is int year)
        {
            yield return PseudoPrefix + "year:" + year.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool IsPseudo(string value) => value.StartsWith(PseudoPrefix, StringComparison.Ordinal);

    // The group of a filter entry: installed and not installed form one group, else the kind ("pb:source:", "pb:feature:", ...);
    // an unknown entry is a group of its own that no game matches.
    private static string Group(string value)
    {
        if (string.Equals(value, Installed, StringComparison.OrdinalIgnoreCase) || string.Equals(value, NotInstalled, StringComparison.OrdinalIgnoreCase))
        {
            return Installed;
        }

        var end = value.IndexOf(':', PseudoPrefix.Length);
        return end < 0 ? value : value.Substring(0, end + 1).ToLowerInvariant();
    }

    /// <summary>Jellyfin's SortName: Playnite's sorting name when set, else the name.</summary>
    public static string SortName(GameEntry game) => string.IsNullOrEmpty(game.SortingName) ? game.Name : game.SortingName;

    /// <summary>
    /// Played by the rule <paramref name="mode"/>: Off = never; Started = some playtime or a last activity; Completed = Playnite's
    /// completion status is named "Beaten" or "Completed" (trimmed, case ignored).
    /// </summary>
    public static bool IsPlayed(GameEntry game, PlayedMarkMode mode) => mode switch
    {
        PlayedMarkMode.Started => game.Playtime > 0 || game.LastActivity is not null,
        PlayedMarkMode.Completed => game.CompletionStatus?.Trim() is string status
            && (string.Equals(status, "Beaten", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)),
        _ => false
    };

    public static string? OfficialRating(GameEntry game) => game.AgeRatings.FirstOrDefault();

    /// <summary>
    /// The requested page of groups and the number of groups after filtering, before paging. A group matches when one of its
    /// versions meets every filter; name and state filters (played, unplayed, favourite) look at the main version only, because
    /// the tile shows the main version's state; sorting uses the main versions.
    /// </summary>
    public static (List<GameGroup> Page, int TotalRecordCount) Run(IReadOnlyList<GameGroup> games, GameQueryRequest q)
    {
        if (q.HasSubtitles == true || q.VideoTypes.Count > 0 || q.Is3D == true || q.IsHd == true || q.Is4K == true)
        {
            return (new List<GameGroup>(), 0);
        }

        // Filter entries come as tags (filter dialog) or as genre ids (filter menu, ids derived from the same games);
        // within a group any entry matches, between groups all must.
        var all = games.SelectMany(g => g.Versions).ToList();
        var pseudoIds = q.GenreIds.Count == 0 ? new Dictionary<Guid, string>() : PseudoGenres(all).ToDictionary(p => p.Id, p => p.Name!);
        var groups = q.Tags.Where(IsPseudo).Concat(q.GenreIds.Where(pseudoIds.ContainsKey).Select(id => pseudoIds[id]))
            .GroupBy(Group, StringComparer.OrdinalIgnoreCase).ToList();
        var tags = q.Tags.Where(t => !IsPseudo(t)).ToList();
        var genreIds = q.GenreIds.Where(id => !pseudoIds.ContainsKey(id)).ToList();
        var filtered = games.Where(game => MatchesName(game.Main, q) && MatchesState(game.Main, q) && MatchesExtras(game, q) && game.Versions.Any(g => Matches(g, q, tags, genreIds)
            && groups.All(group => PseudoValues(g, true).Any(v => AnyFilterName(group, v))))).ToList();
        var comparer = Comparer(q);
        var sorted = filtered.OrderBy(g => g.Main, comparer).ToList();
        IEnumerable<GameGroup> page = sorted.Skip(q.StartIndex ?? 0);
        if (q.Limit is int limit)
        {
            page = page.Take(limit);
        }

        return (page.ToList(), filtered.Count);
    }

    // The alphabet bar: the tile shows the main version's name, so only the main version counts.
    private static bool MatchesName(GameEntry g, GameQueryRequest q)
    {
        var sortName = SortName(g);
        return (string.IsNullOrEmpty(q.NameStartsWith) || sortName.StartsWith(q.NameStartsWith, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrEmpty(q.NameLessThan)
                || string.CompareOrdinal(sortName.ToLowerInvariant(), q.NameLessThan.ToLowerInvariant()) < 0);
    }

    private static bool MatchesState(GameEntry g, GameQueryRequest q)
    {
        var played = IsPlayed(g, q.PlayedMark);
        return (!q.Filters.Contains(ItemFilter.IsFavorite) || g.Favorite)
            && (q.IsFavorite is not bool favorite || g.Favorite == favorite)
            && (!q.Filters.Contains(ItemFilter.IsPlayed) || played)
            && (!q.Filters.Contains(ItemFilter.IsUnplayed) || !played)
            && (q.IsPlayed is not bool isPlayed || played == isPlayed);
    }

    // The extras filters look at the whole group (its game folders, ExtrasIndex.ForGroup); several asked kinds must all hold.
    private static bool MatchesExtras(GameGroup game, GameQueryRequest q)
    {
        if (q.HasTrailer is null && q.HasSpecialFeature is null && q.HasThemeSong is null && q.HasThemeVideo is null)
        {
            return true;
        }

        var kinds = q.ExtraKinds?.Invoke(game) ?? Array.Empty<ExtraKind>();
        return Has(q.HasTrailer, ExtraKind.Trailer) && Has(q.HasSpecialFeature, ExtraKind.Special)
            && Has(q.HasThemeSong, ExtraKind.ThemeSong) && Has(q.HasThemeVideo, ExtraKind.ThemeVideo);

        bool Has(bool? asked, ExtraKind kind) => asked is not bool want || kinds.Contains(kind) == want;
    }

    private static bool Matches(GameEntry g, GameQueryRequest q, IReadOnlyList<string> tags, IReadOnlyList<Guid> genreIds)
    {
        return (q.VersionIds is null || q.VersionIds.Contains(g.Id))
            // Resumable for a game = played recently: a last activity is set (Suggestions "Continue watching").
            && (!q.Filters.Contains(ItemFilter.IsResumable) || g.LastActivity is not null)
            && (q.Genres.Count == 0 || g.Genres.Any(n => AnyFilterName(q.Genres, n)))
            && (genreIds.Count == 0 || g.GenreRefs.Any(r => genreIds.Contains(r.Id)))
            && (q.PersonIds.Count == 0 || g.DeveloperRefs.Any(r => q.PersonIds.Contains(r.Id)))
            && (q.StudioIds.Count == 0 || g.PublisherRefs.Any(r => q.StudioIds.Contains(r.Id)))
            && (q.Years.Count == 0 || (g.Year is int year && q.Years.Contains(year)))
            && (q.OfficialRatings.Count == 0
                || (OfficialRating(g) is string rating && AnyFilterName(q.OfficialRatings, rating)))
            && (tags.Count == 0 || g.Tags.Any(t => AnyFilterName(tags, t)));
    }

    // Whether one of the asked names equals the game's value, both through FilterSafe, case ignored.
    private static bool AnyFilterName(IEnumerable<string> asked, string value)
    {
        var safe = FilterSafe(value);
        return asked.Any(a => string.Equals(FilterSafe(a), safe, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Keys in order; sort order per key as Jellyfin's RequestHelpers.GetOrderBy; no key = SortName ascending.</summary>
    private static IComparer<GameEntry> Comparer(GameQueryRequest q)
    {
        var keys = q.SortBy.Count == 0 ? new[] { ItemSortBy.SortName } : q.SortBy.ToArray();
        var random = new Dictionary<string, int>();
        var comparisons = keys.Select((key, i) =>
        {
            var order = i < q.SortOrder.Count ? q.SortOrder[i] : q.SortOrder.Count > 0 ? q.SortOrder[0] : SortOrder.Ascending;
            var compare = Key(key, random);
            return order == SortOrder.Descending ? (a, b) => compare(b, a) : compare;
        }).ToList();

        return Comparer<GameEntry>.Create((a, b) =>
        {
            foreach (var compare in comparisons)
            {
                var result = compare(a, b);
                if (result != 0)
                {
                    return result;
                }
            }

            return 0;
        });
    }

    // Null values sort as the smallest (Nullable.Compare and string.Compare both do so).
    private static Comparison<GameEntry> Key(ItemSortBy key, Dictionary<string, int> random) => key switch
    {
        ItemSortBy.SortName => (a, b) => string.Compare(SortName(a), SortName(b), StringComparison.OrdinalIgnoreCase),
        ItemSortBy.Random => (a, b) => RandomValue(a, random).CompareTo(RandomValue(b, random)),
        ItemSortBy.CommunityRating => (a, b) => Nullable.Compare(a.CommunityScore, b.CommunityScore),
        ItemSortBy.CriticRating => (a, b) => Nullable.Compare(a.CriticScore, b.CriticScore),
        ItemSortBy.DateCreated => (a, b) => Nullable.Compare(a.Added, b.Added),
        ItemSortBy.DatePlayed => (a, b) => Nullable.Compare(a.LastActivity, b.LastActivity),
        ItemSortBy.OfficialRating => (a, b) => string.Compare(OfficialRating(a), OfficialRating(b), StringComparison.OrdinalIgnoreCase),
        ItemSortBy.PlayCount => (a, b) => a.PlayCount.CompareTo(b.PlayCount),
        ItemSortBy.PremiereDate => (a, b) => Nullable.Compare(a.ReleaseDate, b.ReleaseDate),
        ItemSortBy.Runtime => (a, b) => a.Playtime.CompareTo(b.Playtime),
        ItemSortBy.ProductionYear => (a, b) => Nullable.Compare(a.Year, b.Year),
        _ => (a, b) => 0
    };

    // A fresh random value per game and request, so Random shuffles anew each time.
    private static int RandomValue(GameEntry game, Dictionary<string, int> random)
    {
        if (!random.TryGetValue(game.Id, out var value))
        {
            value = Random.Shared.Next();
            random[game.Id] = value;
        }

        return value;
    }
}
