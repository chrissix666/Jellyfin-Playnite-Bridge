using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One row of the Suggestions tab, as Jellyfin's RecommendationDto before the games become item DTOs.</summary>
public sealed record GameRecommendation(RecommendationType Type, string BaselineItemName, string BaselineId, IReadOnlyList<GameEntry> Items);

/// <summary>
/// A Playnite series whose visible games belong to at least two groups, shown as a virtual folder in the Collections tab;
/// Games = the series' Playnite entries, GroupCount = the number of their groups.
/// </summary>
public sealed record GameCollection(Guid Id, string Name, IReadOnlyList<GameEntry> Games, int GroupCount, GameEntry? Poster, DateTime? Added, DateTime? Released);

/// <summary>Similar games, Suggestions rows, latest games and series collections; pure, no request or file access.</summary>
public static class GameSuggestions
{
    // Community score descending, games without a score last, then sort name.
    private static readonly IComparer<GameEntry> ByScore = Comparer<GameEntry>.Create((a, b) =>
        a.CommunityScore == b.CommunityScore ? CompareSortName(a, b)
        : a.CommunityScore is null ? 1
        : b.CommunityScore is null ? -1
        : b.CommunityScore.Value.CompareTo(a.CommunityScore.Value));

    // Release date ascending, games without a date last, then sort name.
    private static readonly IComparer<GameEntry> ByEarliestRelease = Comparer<GameEntry>.Create((a, b) =>
        a.ReleaseDate == b.ReleaseDate ? CompareSortName(a, b)
        : a.ReleaseDate is null ? 1
        : b.ReleaseDate is null ? -1
        : a.ReleaseDate.Value.CompareTo(b.ReleaseDate.Value));

    // Release date descending, games without a date last, then sort name.
    private static readonly IComparer<GameEntry> ByLatestRelease = Comparer<GameEntry>.Create((a, b) =>
        a.ReleaseDate == b.ReleaseDate ? CompareSortName(a, b)
        : a.ReleaseDate is null ? 1
        : b.ReleaseDate is null ? -1
        : b.ReleaseDate.Value.CompareTo(a.ReleaseDate.Value));

    // Added date descending, games without a date last, then sort name.
    private static readonly IComparer<GameEntry> ByNewestAdded = Comparer<GameEntry>.Create((a, b) =>
        a.Added == b.Added ? CompareSortName(a, b)
        : a.Added is null ? 1
        : b.Added is null ? -1
        : b.Added.Value.CompareTo(a.Added.Value));

    /// <summary>The order that puts the game whose cover is a collection's poster first (CollectionPosterOrder).</summary>
    public static IComparer<GameEntry> PosterComparer(CollectionPosterOrder order) => order switch
    {
        CollectionPosterOrder.LatestRelease => ByLatestRelease,
        CollectionPosterOrder.FirstByName => Comparer<GameEntry>.Create(CompareSortName),
        CollectionPosterOrder.NewestAdded => ByNewestAdded,
        CollectionPosterOrder.HighestScore => ByScore,
        _ => ByEarliestRelease
    };

    /// <summary>Similar = same Playnite series, a shared developer or at least two shared genres.</summary>
    public static bool IsSimilar(GameEntry a, GameEntry b) =>
        a.SeriesRefs.Any(s => b.SeriesRefs.Any(t => t.Id == s.Id))
        || a.DeveloperRefs.Any(d => b.DeveloperRefs.Any(e => e.Id == d.Id))
        || a.GenreRefs.Select(r => r.Id).Intersect(b.GenreRefs.Select(r => r.Id)).Count() >= 2;

    /// <summary>The candidates similar to <paramref name="baseline"/> (never itself), best community score first, at most <paramref name="limit"/>.</summary>
    public static List<GameEntry> Similar(IEnumerable<GameEntry> candidates, GameEntry baseline, int limit) =>
        candidates.Where(c => c.Id != baseline.Id && IsSimilar(baseline, c)).OrderBy(c => c, ByScore).Take(limit).ToList();

    /// <summary>
    /// The Suggestions rows as Jellyfin's MoviesController.GetMovieRecommendations builds them: the 7 most recently played games
    /// give "similar" rows (unplayed games only), the developers of the 6 most recent give "developer" rows (the developer's other
    /// visible games, never one of those 6, and only when at least 2 are left); "unplayed" by the activity rule (PlayedMarkMode.Started)
    /// whatever PlayedMark is set to; empty rows are dropped,
    /// rows are mixed with the weights [similar, similar, developer] up to <paramref name="categoryLimit"/>, then ordered by type.
    /// </summary>
    public static List<GameRecommendation> Recommendations(IReadOnlyList<GameEntry> games, int categoryLimit, int itemLimit)
    {
        var recent = games.Where(g => g.LastActivity is not null).OrderByDescending(g => g.LastActivity).Take(7).ToList();
        var unplayed = games.Where(g => !GameQuery.IsPlayed(g, PlayedMarkMode.Started)).ToList();
        using var similar = recent
            .Select(b => new GameRecommendation(RecommendationType.SimilarToRecentlyPlayed, b.Name, b.Id, Similar(unplayed, b, itemLimit)))
            .Where(r => r.Items.Count > 0)
            .GetEnumerator();
        var baselines = recent.Take(6).ToList();
        using var developers = baselines.SelectMany(g => g.DeveloperRefs).DistinctBy(d => d.Id)
            .Select(d => (Developer: d, Others: games
                .Where(g => g.DeveloperRefs.Any(r => r.Id == d.Id) && !baselines.Any(b => b.Id == g.Id))
                .OrderBy(g => g, ByScore).ToList()))
            .Where(row => row.Others.Count >= 2)
            .Select(row => new GameRecommendation(RecommendationType.HasDirectorFromRecentlyPlayed, row.Developer.Name, row.Developer.Id.ToString(),
                row.Others.Take(itemLimit).ToList()))
            .Where(r => r.Items.Count > 0)
            .GetEnumerator();

        var weighted = new[] { similar, similar, developers };
        var categories = new List<GameRecommendation>();
        while (categories.Count < categoryLimit)
        {
            var allEmpty = true;
            foreach (var category in weighted)
            {
                if (category.MoveNext())
                {
                    categories.Add(category.Current);
                    allEmpty = false;
                    if (categories.Count >= categoryLimit)
                    {
                        break;
                    }
                }
            }

            if (allEmpty)
            {
                break;
            }
        }

        return categories.OrderBy(c => c.Type).ToList();
    }

    /// <summary>The most recently added games (games without an added date left out); played state by the rule <paramref name="mode"/>, only when asked for.</summary>
    public static List<GameEntry> Latest(IEnumerable<GameEntry> games, int limit, bool? isPlayed, PlayedMarkMode mode) =>
        games.Where(g => g.Added is not null && (isPlayed is not bool played || GameQuery.IsPlayed(g, mode) == played))
            .OrderByDescending(g => g.Added).Take(limit).ToList();

    /// <summary>
    /// The Playnite series whose games belong to at least two of the given groups; poster = the main version of the group of the
    /// series game with a cover that comes first in <paramref name="posterOrder"/> (earliest release by default; games without a date or score
    /// last, a tie by sort name).
    /// </summary>
    public static List<GameCollection> Collections(IReadOnlyList<GameGroup> groups, CollectionPosterOrder posterOrder = CollectionPosterOrder.EarliestRelease)
    {
        var groupOf = groups.SelectMany(group => group.Versions.Select(v => (v.Id, Group: group))).ToDictionary(p => p.Id, p => p.Group);
        return groups.SelectMany(group => group.Versions).SelectMany(g => g.SeriesRefs.Select(s => (Series: s, Game: g)))
            .GroupBy(p => p.Series.Id)
            .Select(series => (Series: series, Games: series.Select(p => p.Game).ToList()))
            .Select(series => (series.Series, series.Games, GroupCount: series.Games.Select(g => groupOf[g.Id]).Distinct().Count()))
            .Where(series => series.GroupCount >= 2)
            .Select(series =>
            {
                var poster = series.Games.Where(g => g.HasCover).OrderBy(g => g, PosterComparer(posterOrder)).FirstOrDefault();
                return new GameCollection(
                    VirtualIds.Series(series.Series.Key),
                    series.Series.First().Series.Name,
                    series.Games,
                    series.GroupCount,
                    poster is null ? null : groupOf[poster.Id].Main,
                    series.Games.Max(g => g.Added),
                    series.Games.Max(g => g.ReleaseDate));
            })
            .ToList();
    }

    /// <summary>
    /// A game's Playnite series in Playnite's order, each with the id of its collection folder when the series is one of
    /// <paramref name="collections"/> (at least two groups), else null (the series stays plain text on the game page).
    /// </summary>
    public static List<(string Name, Guid? CollectionId)> SeriesLinks(GameEntry game, IReadOnlyList<GameCollection> collections) =>
        game.SeriesRefs.Select(s => (s.Name!, collections.Any(c => c.Id == VirtualIds.Series(s.Id)) ? VirtualIds.Series(s.Id) : (Guid?)null)).ToList();

#if NET10_0_OR_GREATER
    /// <summary>
    /// Jellyfin 12.x GET Items/{itemId}/Collections for a game: the collections among <paramref name="collections"/> that
    /// <see cref="SeriesLinks"/> gives the game a CollectionId for, in Playnite's order of the game's series (a game in
    /// several collections lists them all, as the row on 10.10.7 does), then
    /// Skip(<paramref name="startIndex"/>) and Take(<paramref name="limit"/>) when given; Total = the count before paging.
    /// </summary>
    public static (List<GameCollection> Page, int Total) ItemCollections(GameEntry game, IReadOnlyList<GameCollection> collections, int? startIndex, int? limit)
    {
        var byId = collections.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        var sorted = SeriesLinks(game, collections).Select(link => link.CollectionId).OfType<Guid>().Distinct()
            .Where(byId.ContainsKey).Select(id => byId[id])
            .ToList();
        IEnumerable<GameCollection> page = sorted;
        if (startIndex.HasValue)
        {
            page = page.Skip(startIndex.Value);
        }

        if (limit.HasValue)
        {
            page = page.Take(limit.Value);
        }

        return (page.ToList(), sorted.Count);
    }
#endif

    /// <summary>
    /// Collections sorted by the keys of Jellyfin's Collections tab: SortName, DateCreated (latest added), PremiereDate (latest release);
    /// other keys (CommunityRating, OfficialRating) are null for every collection and keep the order. Sort order per key as GameQuery.
    /// </summary>
    public static List<GameCollection> Sort(IEnumerable<GameCollection> collections, IReadOnlyList<ItemSortBy> sortBy, IReadOnlyList<SortOrder> sortOrder)
    {
        var keys = sortBy.Count == 0 ? new[] { ItemSortBy.SortName } : sortBy.ToArray();
        var comparisons = keys.Select((key, i) =>
        {
            var order = i < sortOrder.Count ? sortOrder[i] : sortOrder.Count > 0 ? sortOrder[0] : SortOrder.Ascending;
            Comparison<GameCollection> compare = key switch
            {
                ItemSortBy.SortName => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                ItemSortBy.DateCreated => (a, b) => Nullable.Compare(a.Added, b.Added),
                ItemSortBy.PremiereDate => (a, b) => Nullable.Compare(a.Released, b.Released),
                _ => (a, b) => 0
            };
            return order == SortOrder.Descending ? (a, b) => compare(b, a) : compare;
        }).ToList();

        return collections.OrderBy(c => c, Comparer<GameCollection>.Create((a, b) =>
            comparisons.Select(compare => compare(a, b)).FirstOrDefault(result => result != 0))).ToList();
    }

    private static int CompareSortName(GameEntry a, GameEntry b) =>
        string.Compare(GameQuery.SortName(a), GameQuery.SortName(b), StringComparison.OrdinalIgnoreCase);
}
