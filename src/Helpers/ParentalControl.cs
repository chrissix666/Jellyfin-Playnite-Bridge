using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Data.Enums;
#if NET10_0_OR_GREATER
using Jellyfin.Extensions;
using MediaBrowser.Model.Entities;
#endif

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

#if NET10_0_OR_GREATER
/// <summary>
/// Jellyfin 12.x: a Jellyfin user's parental control settings as they apply to a movie: the highest allowed rating score and sub-score, whether
/// unrated movies are blocked, the blocked and the allowed tags.
/// </summary>
public sealed record ParentalPolicy(int? MaxScore, int? MaxSubScore, bool BlockUnrated, string[] BlockedTags, string[] AllowedTags)
{
    /// <summary>
    /// The settings of <paramref name="user"/> as 12.1 BaseItem.IsParentalAllowed reads them for a movie (and IsVisibleViaTags;
    /// Movie.GetBlockUnratedType is Movie).
    /// </summary>
    public static ParentalPolicy From(User user) => new(
        user.MaxParentalRatingScore,
        user.MaxParentalRatingSubScore,
        user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems).Contains(UnratedItem.Movie),
        user.GetPreference(PreferenceKind.BlockedTags),
        user.GetPreference(PreferenceKind.AllowedTags));

    /// <summary>
    /// The key of this policy with <paramref name="libraryTags"/>: equal keys allow the same games. Tags as 12.1 IsVisibleViaTags compares them
    /// (GetCleanValue, sorted; blank blocked and allowed entries left out); allowed tags that are all blank still differ from none, because 12.1
    /// then blocks every game.
    /// </summary>
    public string Key(string[] libraryTags) => string.Join(
        "\u001e",
        MaxScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        MaxSubScore?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        BlockUnrated ? "1" : "0",
        Tags(BlockedTags.Where(t => !string.IsNullOrWhiteSpace(t))),
        AllowedTags.Length == 0 ? "-" : "+" + Tags(AllowedTags.Where(t => !string.IsNullOrWhiteSpace(t))),
        Tags(libraryTags));

    private static string Tags(IEnumerable<string> tags) =>
        string.Join("\u001f", tags.Select(t => t.GetCleanValue()).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal));
}
#else
/// <summary>
/// A Jellyfin user's parental control settings as they apply to a movie: the highest allowed rating level, whether unrated movies are
/// blocked, the blocked and the allowed tags.
/// </summary>
public sealed record ParentalPolicy(int? MaxRating, bool BlockUnrated, string[] BlockedTags, string[] AllowedTags)
{
    /// <summary>The settings of <paramref name="user"/> as BaseItem.IsParentalAllowed reads them for a movie (Movie.GetBlockUnratedType = Movie).</summary>
    public static ParentalPolicy From(User user) => new(
        user.MaxParentalAgeRating,
        user.GetPreferenceValues<UnratedItem>(PreferenceKind.BlockUnratedItems).Contains(UnratedItem.Movie),
        user.GetPreference(PreferenceKind.BlockedTags),
        user.GetPreference(PreferenceKind.AllowedTags));

    /// <summary>The key of this policy with <paramref name="libraryTags"/>: equal keys allow the same games (tags compared case-insensitively, sorted).</summary>
    public string Key(string[] libraryTags) => string.Join(
        "\u001e",
        MaxRating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        BlockUnrated ? "1" : "0",
        Tags(BlockedTags),
        Tags(AllowedTags),
        Tags(libraryTags));

    private static string Tags(IEnumerable<string> tags) =>
        string.Join("\u001f", tags.Select(t => t.ToLowerInvariant()).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal));
}
#endif

#if NET10_0_OR_GREATER
/// <summary>
/// Jellyfin 12.x: which games a user may see with the setting ApplyParentalControls, by the rule Jellyfin 12.1 applies to a movie's item
/// (BaseItem.IsParentalAllowed, IsVisibleViaTags, GetBlockUnratedValue). Pure: a game's age ratings
/// map to a rating score by <see cref="Score"/>, a form it does not know by the caller's fallback (ILocalizationManager.GetRatingScore with the
/// metadata country); score and sub-score are compared with the user's MaxParentalRatingScore and MaxParentalRatingSubScore.
/// </summary>
public static class ParentalControl
{
    // Normalized form (case ignored, no white space, '-' or '_') -> (score, sub-score). Jellyfin 12.1's value where its table of the same system
    // has the same name (Emby.Server.Implementations\Localization\Ratings jp.json = CERO, au.json = ACB, nz.json = OFLC, br.json = ClassInd), else
    // the minimum age the system names with no sub-score (PEGI, USK: de.json is FSK, ESRB: us.json is MPAA/TV, GRAC: kr.json is a film system).
    // ESRB RP has no age. The scores are those of the net8.0 table; only the sub-scores are new.
    private static readonly Dictionary<string, (int Score, int? SubScore)> Scores = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PEGI3"] = (3, null), ["PEGI7"] = (7, null), ["PEGI12"] = (12, null), ["PEGI16"] = (16, null), ["PEGI18"] = (18, null),
        ["USK0"] = (0, null), ["USK6"] = (6, null), ["USK12"] = (12, null), ["USK16"] = (16, null), ["USK18"] = (18, null),
        ["ESRBEC"] = (3, null), ["ESRBE"] = (6, null), ["ESRBE10+"] = (10, null), ["ESRBT"] = (13, null), ["ESRBM"] = (17, null), ["ESRBAO"] = (18, null),
        ["CEROA"] = (0, 0), ["CEROB"] = (12, 1), ["CEROC"] = (15, 1), ["CEROD"] = (17, null), ["CEROZ"] = (18, null),
        ["ACBG"] = (0, 0), ["ACBPG"] = (15, 1), ["ACBM"] = (15, 2), ["ACBMA15+"] = (15, 3), ["ACBR18+"] = (18, 1),
        ["CLASSINDL"] = (0, null), ["CLASSIND10"] = (10, null), ["CLASSIND12"] = (12, null), ["CLASSIND14"] = (14, null), ["CLASSIND16"] = (16, null),
        ["CLASSIND18"] = (18, null),
        ["GRACALL"] = (0, null), ["GRAC12"] = (12, null), ["GRAC15"] = (15, null), ["GRAC18"] = (18, null),
        ["OFLCG"] = (0, 0), ["OFLCPG"] = (13, 0), ["OFLCM"] = (16, 0), ["OFLCR13"] = (13, 1), ["OFLCR15"] = (15, 0), ["OFLCR16"] = (16, 1), ["OFLCR18"] = (18, 1),
    };

    /// <summary>
    /// The rating score of the Playnite age rating <paramref name="rating"/> (e.g. "PEGI 12", "ACB M", "16+"), case, white space, '-' and '_'
    /// ignored; a plain number "n" or "n+" (0 to 99) is (n, no sub-score), as 12.1 TryParseRatingAsScore; null for a form the table does not know.
    /// </summary>
    public static ParentalRatingScore? Score(string rating)
    {
        var key = string.Concat(rating.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_'));
        if (Scores.TryGetValue(key, out var score))
        {
            return new ParentalRatingScore(score.Score, score.SubScore);
        }

        var number = key.EndsWith('+') ? key[..^1] : key;
        return number.Length is 1 or 2 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var age) ? new ParentalRatingScore(age, null) : null;
    }

    /// <summary>The policy that applies to a request: that of <paramref name="user"/> while ApplyParentalControls (<paramref name="apply"/>) is on, else null; null without a user (an API key).</summary>
    public static ParentalPolicy? PolicyFor(bool apply, User? user) => apply && user is not null ? ParentalPolicy.From(user) : null;

    /// <summary>
    /// True when <paramref name="policy"/> allows <paramref name="game"/>; null policy (an API key without a user) allows every game. Tags as 12.1
    /// IsVisibleViaTags: without blocked and allowed tags no tag check; else the tags of the game and of the games library
    /// (<paramref name="libraryTags"/>) and the non-blank blocked and allowed tags are compared after GetCleanValue, ordinally; a shared blocked tag
    /// blocks; with allowed tags one shared tag is needed (the games library is a CollectionFolder, so the check applies). Every non-empty age
    /// rating counts as <see cref="Score"/> gives it, else as <paramref name="ratingScore"/> does; the strictest applies (highest score, then the
    /// highest sub-score, a missing one as 0). Without one the game is blocked exactly when unrated movies are; else allowed without a maximum,
    /// below the maximum score, and at it when there is no maximum sub-score or the sub-score (missing = 0) is not above it.
    /// </summary>
    public static bool IsAllowed(GameEntry game, ParentalPolicy? policy, string[] libraryTags, Func<string, ParentalRatingScore?> ratingScore)
    {
        if (policy is null)
        {
            return true;
        }

        if (!IsVisibleViaTags(game, policy, libraryTags))
        {
            return false;
        }

        // The strictest rating is the one GlobeLink hands out too (GetRatingScore throws on an empty text, so an empty rating never reaches it).
        if (Strictest(game, ratingScore) is not { } strictest)
        {
            return !policy.BlockUnrated;
        }

        if (policy.MaxScore is null)
        {
            return true;
        }

        if (strictest.Score != policy.MaxScore.Value)
        {
            return strictest.Score < policy.MaxScore.Value;
        }

        return policy.MaxSubScore is null || (strictest.SubScore ?? 0) <= policy.MaxSubScore.Value;
    }

    /// <summary>
    /// The strictest rating of <paramref name="game"/> as <see cref="IsAllowed"/> compares it (highest score, then highest sub-score, a missing one as 0): every non-empty
    /// age rating counts as <see cref="Score"/> gives it, else as <paramref name="ratingScore"/>; null without one. Read only, no policy.
    /// </summary>
    public static (int Score, int? SubScore)? Strictest(GameEntry game, Func<string, ParentalRatingScore?> ratingScore)
    {
        (int Score, int? SubScore)? strictest = null;
        foreach (var rating in game.AgeRatings.Where(r => !string.IsNullOrEmpty(r)))
        {
            var score = Score(rating) ?? ratingScore(rating);
            if (score is not null && (strictest is null || score.Score > strictest.Value.Score
                || (score.Score == strictest.Value.Score && (score.SubScore ?? 0) > (strictest.Value.SubScore ?? 0))))
            {
                strictest = (score.Score, score.SubScore);
            }
        }

        return strictest;
    }

    /// <summary>The games of <paramref name="games"/> that <see cref="IsAllowed"/> allows; the list itself when <paramref name="policy"/> is null.</summary>
    public static IReadOnlyList<GameEntry> Filter(IReadOnlyList<GameEntry> games, ParentalPolicy? policy, string[] libraryTags, Func<string, ParentalRatingScore?> ratingScore) =>
        policy is null ? games : games.Where(g => IsAllowed(g, policy, libraryTags, ratingScore)).ToList();

    // 12.1 BaseItem.IsVisibleViaTags for an item whose parent is the games library (a CollectionFolder).
    private static bool IsVisibleViaTags(GameEntry game, ParentalPolicy policy, string[] libraryTags)
    {
        if (policy.BlockedTags.Length == 0 && policy.AllowedTags.Length == 0)
        {
            return true;
        }

        var tags = game.Tags.Concat(libraryTags).Select(t => t.GetCleanValue()).ToHashSet(StringComparer.Ordinal);
        if (Clean(policy.BlockedTags).Overlaps(tags))
        {
            return false;
        }

        return policy.AllowedTags.Length == 0 || Clean(policy.AllowedTags).Overlaps(tags);
    }

    private static HashSet<string> Clean(IEnumerable<string> tags) =>
        tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.GetCleanValue()).ToHashSet(StringComparer.Ordinal);
}
#else
/// <summary>
/// Which games a user may see with the setting ApplyParentalControls, by the rule Jellyfin applies to a movie's item (BaseItem.IsParentalAllowed,
/// IsVisibleViaTags, GetBlockUnratedValue; Jellyfin 10.10.7). Pure: a game's age ratings map to an age in years by <see cref="Age"/>, a form it does
/// not know by the caller's fallback (ILocalizationManager.GetRatingLevel). In 10.10.7 a rating level is an age in years, so the age is compared
/// with the user's MaxParentalAgeRating directly.
/// </summary>
public static class ParentalControl
{
    // Normalized form (case ignored, no white space, '-' or '_') -> age in years. Jellyfin's value where its 10.10.7 table of the same system has the
    // same name (Localization\Ratings de, jp, au, br, nz), else the minimum age the system names (PEGI, ESRB, GRAC). ESRB RP has no age.
    private static readonly Dictionary<string, int> Ages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PEGI3"] = 3, ["PEGI7"] = 7, ["PEGI12"] = 12, ["PEGI16"] = 16, ["PEGI18"] = 18,
        ["USK0"] = 0, ["USK6"] = 6, ["USK12"] = 12, ["USK16"] = 16, ["USK18"] = 18,
        ["ESRBEC"] = 3, ["ESRBE"] = 6, ["ESRBE10+"] = 10, ["ESRBT"] = 13, ["ESRBM"] = 17, ["ESRBAO"] = 18,
        ["CEROA"] = 0, ["CEROB"] = 12, ["CEROC"] = 15, ["CEROD"] = 17, ["CEROZ"] = 18,
        ["ACBG"] = 0, ["ACBPG"] = 15, ["ACBM"] = 15, ["ACBMA15+"] = 15, ["ACBR18+"] = 18,
        ["CLASSINDL"] = 0, ["CLASSIND10"] = 10, ["CLASSIND12"] = 12, ["CLASSIND14"] = 14, ["CLASSIND16"] = 16, ["CLASSIND18"] = 18,
        ["GRACALL"] = 0, ["GRAC12"] = 12, ["GRAC15"] = 15, ["GRAC18"] = 18,
        ["OFLCG"] = 0, ["OFLCPG"] = 13, ["OFLCM"] = 16, ["OFLCR13"] = 13, ["OFLCR15"] = 15, ["OFLCR16"] = 16, ["OFLCR18"] = 18,
    };

    /// <summary>
    /// The age in years of the Playnite age rating <paramref name="rating"/> (e.g. "PEGI 12", "ESRB M", "16+"), case, white space, '-' and '_'
    /// ignored; a plain number "n" or "n+" (0 to 99) is n; null for a form the table does not know.
    /// </summary>
    public static int? Age(string rating)
    {
        var key = string.Concat(rating.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_'));
        if (Ages.TryGetValue(key, out var age))
        {
            return age;
        }

        var number = key.EndsWith('+') ? key[..^1] : key;
        return number.Length is 1 or 2 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out age) ? age : null;
    }

    /// <summary>The policy that applies to a request: that of <paramref name="user"/> while ApplyParentalControls (<paramref name="apply"/>) is on, else null; null without a user (an API key).</summary>
    public static ParentalPolicy? PolicyFor(bool apply, User? user) => apply && user is not null ? ParentalPolicy.From(user) : null;

    /// <summary>
    /// True when <paramref name="policy"/> allows <paramref name="game"/>; null policy (an API key without a user) allows every game. Blocked when a
    /// blocked tag is a tag of the game or of the games library (<paramref name="libraryTags"/>), case ignored; with allowed tags, one of them is
    /// needed. Every non-empty age rating of the game counts as <see cref="Age"/> gives it, else as <paramref name="ratingLevel"/> does; the strictest
    /// (highest) age applies. Without one (no rating, or only forms neither knows) the game is blocked exactly when unrated movies are; else it is
    /// allowed up to the user's highest allowed age.
    /// </summary>
    public static bool IsAllowed(GameEntry game, ParentalPolicy? policy, string[] libraryTags, Func<string, int?> ratingLevel)
    {
        if (policy is null)
        {
            return true;
        }

        var tags = game.Tags.Concat(libraryTags).ToList();
        if (policy.BlockedTags.Any(b => tags.Contains(b, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (policy.AllowedTags.Length != 0 && !policy.AllowedTags.Any(a => tags.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        // The strictest age is the one GlobeLink hands out too (GetRatingLevel throws on an empty text, so an empty rating never reaches it).
        var level = Strictest(game, ratingLevel);
        if (level is null)
        {
            return !policy.BlockUnrated;
        }

        return policy.MaxRating is null || level.Value <= policy.MaxRating.Value;
    }

    /// <summary>
    /// The strictest (highest) age in years of <paramref name="game"/> as <see cref="IsAllowed"/> compares it: every non-empty age rating counts as <see cref="Age"/> gives it,
    /// else as <paramref name="ratingLevel"/>; null without one. Read only, no policy.
    /// </summary>
    public static int? Strictest(GameEntry game, Func<string, int?> ratingLevel) =>
        game.AgeRatings.Where(r => !string.IsNullOrEmpty(r)).Select(r => Age(r) ?? ratingLevel(r)).Max();

    /// <summary>The games of <paramref name="games"/> that <see cref="IsAllowed"/> allows; the list itself when <paramref name="policy"/> is null.</summary>
    public static IReadOnlyList<GameEntry> Filter(IReadOnlyList<GameEntry> games, ParentalPolicy? policy, string[] libraryTags, Func<string, int?> ratingLevel) =>
        policy is null ? games : games.Where(g => IsAllowed(g, policy, libraryTags, ratingLevel)).ToList();
}
#endif
