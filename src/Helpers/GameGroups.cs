using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// One game: its versions (Playnite entries) in main version order; index 0 is the main version. Twins = the Playnite ids of entries
/// left out as twins of a version (same store, title and platforms), each with the id of the version kept in its place.
/// </summary>
public sealed record GameGroup(IReadOnlyList<GameEntry> Versions, IReadOnlyDictionary<string, string>? Twins = null)
{
    public GameEntry Main => Versions[0];
}

/// <summary>Grouping of the same game across stores, main version, version markers and version labels; pure, no request or file access.</summary>
public static class GameGroups
{
    /// <summary>Edition words removed from the end of a normalized title when editions are grouped (whole words, repeatedly).</summary>
    public static readonly string[] EditionWords =
    {
        "remastered", "remaster", "definitive edition", "enhanced edition", "enhanced", "hd", "game of the year edition",
        "game of the year", "goty edition", "goty", "complete edition", "deluxe edition", "redux", "directors cut", "director s cut",
        "german edition", "deutsche version"
    };

    /// <summary>The store keys that have an icon file (Web\icons\&lt;key&gt;.png, custom.svg); the icons endpoint serves only these.</summary>
    public static readonly string[] IconKeys =
    {
        "amazon", "battlenet", "ea", "epic", "gog", "humble", "indiegala", "itch", "playstation", "riot", "rockstar", "steam", "ubisoft", "xbox", "custom"
    };

    /// <summary>The text marker that stands for the icons left out after the first two.</summary>
    public const string More = "…";

    // GOG Galaxy's order of the icons; every other store follows alphabetically, custom games last.
    private static readonly string[] IconOrder = { "gog", "steam", "ubisoft", "ea" };

    private static readonly Regex SteamApp = new(@"^/app/(\d+)(/|$)", RegexOptions.CultureInvariant);

    // Short platform names of the version labels, from Playnite's Emulation\Platforms.yaml; every other name stays as Playnite has it.
    private static readonly Dictionary<string, string> ShortPlatforms = new(StringComparer.Ordinal)
    {
        ["Sony PlayStation"] = "PS1", ["Sony PlayStation 2"] = "PS2", ["Sony PlayStation 3"] = "PS3", ["Sony PlayStation 4"] = "PS4",
        ["Sony PlayStation 5"] = "PS5", ["Sony PlayStation Portable"] = "PSP", ["Sony PlayStation Vita"] = "PS Vita", ["Microsoft Xbox"] = "Xbox",
        ["Microsoft Xbox 360"] = "Xbox 360", ["Microsoft Xbox One"] = "Xbox One", ["Microsoft Xbox Series"] = "Xbox Series", ["Nintendo Switch"] = "Switch",
        ["Nintendo Switch 2"] = "Switch 2", ["PC (Windows)"] = "PC", ["PC (Linux)"] = "Linux", ["PC (DOS)"] = "DOS", ["Macintosh"] = "Mac"
    };

    // Characters trimmed from both ends of a title part (" - ", ":", "–"); brackets only in pairs (TrimTerm).
    private static readonly char[] TitlePartTrim = { ' ', '-', '–', '—', ':', '™', '®', '©' };

    private static readonly char[] Brackets = { '(', ')', '[', ']' };

    private static readonly PluginConfiguration DefaultConfig = new();

    // Twin detection and the title part of a label compare the plain titles: no roman numerals, no demo words.
    private static readonly PluginConfiguration PlainConfig = new() { RomanNumerals = false, GroupDemosAndTestBuilds = false };

    /// <summary>The title as compared: ™ ® © removed, lower case (invariant), every character that is not a Unicode letter or digit becomes a space, white space collapsed and trimmed.</summary>
    private static string Plain(string name)
    {
        var lower = name.Replace("™", string.Empty, StringComparison.Ordinal).Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("©", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        var builder = new StringBuilder(lower.Length);
        foreach (var c in lower)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// A word list of the settings as plain words or phrases (see <see cref="Plain"/>), in list order, each once, empty entries left out: one entry per line
    /// when the text holds a line break (the admin page stores its lists so, a comma is then part of a title), else separated by commas (the defaults).
    /// </summary>
    public static string[] Words(string? list) => ListOf(list).Words;

    // A word list split once per text (the settings' string, by reference) with its lookups: the set, each word's first position and the words with roman
    // numerals as numbers; titles look up their whole-word parts instead of testing every word.
    private sealed class WordList
    {
        public WordList(string list)
        {
            var entries = (list.Contains('\n', StringComparison.Ordinal) ? list.Split('\n') : list.Split(','))
                .Select(w => (Entry: w.Trim(), Plain: Plain(w.Trim()))).Where(e => e.Plain.Length > 0).DistinctBy(e => e.Plain).ToArray();
            Words = entries.Select(e => e.Plain).ToArray();
            Entries = entries.Select(e => e.Entry).ToArray();
            Set = new HashSet<string>(Words, StringComparer.Ordinal);
            Position = Words.Select((w, i) => (w, i)).ToDictionary(x => x.w, x => x.i, StringComparer.Ordinal);
            Numbered = new HashSet<string>(Words.Select(RomanToArabic), StringComparer.Ordinal);
        }

        public string[] Words { get; }

        // The entries as written (trimmed), parallel to Words.
        public string[] Entries { get; }

        public HashSet<string> Set { get; }

        public Dictionary<string, int> Position { get; }

        public HashSet<string> Numbered { get; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, WordList> Lists = new();

    private static WordList ListOf(string? list) => Lists.GetValue(list ?? string.Empty, l => new WordList(l));

    // The starts of the words of a plain title (single spaces between words).
    private static IEnumerable<int> WordStarts(string title)
    {
        yield return 0;
        for (var i = title.IndexOf(' ', StringComparison.Ordinal); i >= 0; i = title.IndexOf(' ', i + 1))
        {
            yield return i + 1;
        }
    }

    // Words removed from the end of a plain title, whole words, repeatedly, each time the one first in the list; a title that would become empty stays whole.
    private static string StripWords(string title, WordList words)
    {
        var shortened = title;
        while (words.Words.Length > 0 && shortened.Length > 0)
        {
            var best = -1;
            var length = 0;
            foreach (var start in WordStarts(shortened))
            {
                if (words.Position.TryGetValue(shortened[start..], out var position) && (best < 0 || position < best))
                {
                    best = position;
                    length = shortened.Length - start;
                }
            }

            if (best < 0)
            {
                break;
            }

            shortened = shortened[..^length].TrimEnd();
        }

        return shortened.Length == 0 ? title : shortened;
    }

    // True when one of the words occurs in a plain title as whole words (a run of its words).
    private static bool ContainsWords(string title, WordList words)
    {
        if (words.Words.Length == 0)
        {
            return false;
        }

        var starts = WordStarts(title).ToList();
        for (var i = 0; i < starts.Count; i++)
        {
            for (var j = i; j < starts.Count; j++)
            {
                var end = j + 1 < starts.Count ? starts[j + 1] - 1 : title.Length;
                if (words.Set.Contains(title[starts[i]..end]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // I to XX as the last word of a plain title becomes its number ("ultima underworld ii" = "ultima underworld 2").
    private static readonly Dictionary<string, string> Romans = new(StringComparer.Ordinal)
    {
        ["i"] = "1", ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8", ["ix"] = "9", ["x"] = "10",
        ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xiv"] = "14", ["xv"] = "15", ["xvi"] = "16", ["xvii"] = "17", ["xviii"] = "18", ["xix"] = "19", ["xx"] = "20"
    };

    private static string RomanToArabic(string title)
    {
        var cut = title.LastIndexOf(' ');
        return cut > 0 && Romans.TryGetValue(title[(cut + 1)..], out var number) ? title[..(cut + 1)] + number : title;
    }

    /// <summary>The number of the roman numeral (I to XX) that RomanNumerals reads as the last word of a title ("Ultima Underworld II" = "2"); null when there is none.</summary>
    public static string? RomanNumber(string name)
    {
        var title = Plain(name);
        var cut = title.LastIndexOf(' ');
        return cut > 0 && Romans.TryGetValue(title[(cut + 1)..], out var number) ? number : null;
    }

    /// <summary>
    /// The title as compared for grouping (<see cref="Plain"/>); with <paramref name="editions"/> and RecognizeEditions the edition words of the settings are
    /// removed from the end, with GroupDemosAndTestBuilds the demo words too, and with RomanNumerals a roman numeral as the last word becomes its number.
    /// Without a <paramref name="config"/> the defaults count.
    /// </summary>
    public static string NormalizeTitle(string name, bool editions, PluginConfiguration? config = null)
    {
        var cfg = config ?? DefaultConfig;
        var title = Plain(name);
        if (editions && cfg.RecognizeEditions)
        {
            title = StripWords(title, ListOf(cfg.EditionWords));
        }

        if (cfg.GroupDemosAndTestBuilds)
        {
            title = StripWords(title, ListOf(cfg.DemoWords));
        }

        return cfg.RomanNumerals ? RomanToArabic(title) : title;
    }

    /// <summary>True when a title ends with one of the demo and test build words of the settings.</summary>
    public static bool IsDemo(string name, PluginConfiguration config) =>
        ListOf(config.DemoWords) is { Words.Length: > 0 } words && Plain(name) is var title && WordStarts(title).Skip(1).Any(start => words.Set.Contains(title[start..]));

    /// <summary>The names <see cref="HiddenBy"/> gives its rules, in its order.</summary>
    public static readonly string[] HideRules = { "Demos", "Test Builds", "Soundtracks", "DLC and Packs", "Prologues and Trials", "Hide These Titles" };

    /// <summary>
    /// The rule of Hide Titles that hides a title, the first of <see cref="HideRules"/> (the order of the admin page); null when it is visible. A title in
    /// "Never Hide These Titles" (NeverHideTitlesEnabled on, compared plain, whole) is visible whatever else matches. A switched-on category hides a title
    /// containing its words as whole words or phrases (anywhere in the title); "Hide These Titles" (HideTitlesEnabled on) hides a title equal to an entry
    /// (compared plain, whole, as Never Hide does).
    /// </summary>
    public static string? HiddenBy(string name, PluginConfiguration config)
    {
        var title = Plain(name);
        if (config.NeverHideTitlesEnabled && ListOf(config.NeverHideTitles).Set.Contains(title))
        {
            return null;
        }

        bool Matches(bool on, string words) => on && ContainsWords(title, ListOf(words));
        if (Matches(config.HideDemos, config.HideDemosWords))
        {
            return HideRules[0];
        }

        if (Matches(config.HideTestBuilds, config.HideTestBuildsWords))
        {
            return HideRules[1];
        }

        if (Matches(config.HideSoundtracks, config.HideSoundtracksWords))
        {
            return HideRules[2];
        }

        if (Matches(config.HideDlcAndPacks, config.HideDlcAndPacksWords))
        {
            return HideRules[3];
        }

        if (Matches(config.HideProloguesAndTrials, config.HideProloguesAndTrialsWords))
        {
            return HideRules[4];
        }

        return config.HideTitlesEnabled && ListOf(config.HideTitles).Set.Contains(title) ? HideRules[5] : null;
    }

    /// <summary>True when <see cref="HiddenBy"/> names a rule for the title.</summary>
    public static bool HiddenByTitle(string name, PluginConfiguration config) => HiddenBy(name, config) is not null;

    /// <summary>True when the plain title of <paramref name="name"/> is an entry of the whole-title list <paramref name="list"/> (the comparison of Hide These Titles and Never Hide These Titles).</summary>
    public static bool InTitleList(string name, string? list) => ListOf(list).Set.Contains(Plain(name));

    /// <summary>The entries (as written) of the whole-title list <paramref name="list"/> that equal the plain title of none of <paramref name="names"/>.</summary>
    public static string[] UnmatchedTitles(string? list, IEnumerable<string> names)
    {
        var words = ListOf(list);
        var found = names.Select(Plain).ToHashSet(StringComparer.Ordinal);
        return words.Entries.Where((_, i) => !found.Contains(words.Words[i])).ToArray();
    }

    /// <summary>
    /// The entries (as written) of "Never Group These Titles" that match none of <paramref name="names"/>, compared as <see cref="NeverGrouped"/> does (title
    /// without edition words, roman numerals as numbers, the other settings of <paramref name="config"/>; the list's own switch is not looked at).
    /// </summary>
    public static string[] UnmatchedNeverGroup(PluginConfiguration config, IEnumerable<string> names)
    {
        var words = ListOf(config.NeverGroupTitles);
        var found = names.Select(n => NormalizeTitle(n, false, config)).ToHashSet(StringComparer.Ordinal);
        return words.Entries.Where((_, i) => !found.Contains(RomanToArabic(words.Words[i]))).ToArray();
    }

    /// <summary>
    /// True when NeverGroupTitlesEnabled is on and the title is one of "Never Group These Titles" (compared as grouping compares: plain, without edition
    /// words, roman numerals as numbers).
    /// </summary>
    public static bool NeverGrouped(string name, PluginConfiguration config) =>
        config.NeverGroupTitlesEnabled && ListOf(config.NeverGroupTitles).Numbered.Contains(NormalizeTitle(name, false, config));

    /// <summary>The Steam AppIDs of Steam store links (host store.steampowered.com, path /app/&lt;digits&gt;, http or https).</summary>
    public static string[] SteamAppIdsFromLinks(IEnumerable<string?> urls) =>
        urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.Equals(uri.Host, "store.steampowered.com", StringComparison.OrdinalIgnoreCase)
                ? SteamApp.Match(uri.AbsolutePath) : null)
            .Where(m => m is not null && m.Success)
            .Select(m => m!.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>The Steam AppIDs a game stands for: a Steam game only its own, any other store those of its links.</summary>
    private static IEnumerable<string> SteamIds(GameEntry game) =>
        game.StoreKey == "steam" ? (game.SteamAppId is null ? Array.Empty<string>() : new[] { game.SteamAppId }) : game.LinkedSteamAppIds;

    /// <summary>
    /// True when two developer lists share a studio: an equal name, or names starting with the same word ("Rockstar North" and
    /// "Rockstar Games"; a leading "The" is skipped, words shorter than 3 letters do not count).
    /// </summary>
    public static bool SameStudio(IEnumerable<string> a, IEnumerable<string> b)
    {
        static string FirstWord(string name)
        {
            var words = name.Split(new[] { ' ', '-', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
            var word = words.Length > 1 && words[0].Equals("The", StringComparison.OrdinalIgnoreCase) ? words[1] : words.FirstOrDefault() ?? string.Empty;
            return word.Length >= 3 ? word : string.Empty;
        }

        var left = a.ToList();
        var right = b.ToList();
        return left.Intersect(right, StringComparer.OrdinalIgnoreCase).Any()
            || left.Select(FirstWord).Where(w => w.Length > 0).Intersect(right.Select(FirstWord), StringComparer.OrdinalIgnoreCase).Any();
    }

    /// <summary>
    /// The groups of the given games. With GroupGames two games belong together when their normalized titles are equal and their
    /// release years are at most 2 apart, one of them has no year or no developer, or they share a studio (<see cref="SameStudio"/>);
    /// or when they share a Steam AppID (transitive); without it every game is a group of its own. Groups keep the order of their first
    /// game; the versions of a group are in main version order, twins left out (see WithoutTwins).
    /// </summary>
    public static List<GameGroup> Group(IEnumerable<GameEntry> games, PluginConfiguration config)
    {
        var list = games.ToList();
        if (!config.GroupGames)
        {
            return list.Select(g => new GameGroup(new[] { g })).ToList();
        }

        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        var firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        void Join(string key, int i)
        {
            if (firstByKey.TryGetValue(key, out var other))
            {
                parent[Root(i)] = Root(other);
            }
            else
            {
                firstByKey[key] = i;
            }
        }

        // Same title: joined pairwise with every earlier game of that title, unless their release years are more than 2 apart and
        // they share no developer; an unknown year or developer counts as a match (stores often carry a re-release date; a remake by
        // another studio stays apart).
        var byTitle = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        // "Never Group These Titles": such a game is a group of its own, joined by no title and no Steam AppID.
        var excluded = list.Select(g => NeverGrouped(g.Name, config)).ToArray();
        for (var i = 0; i < list.Count; i++)
        {
            if (excluded[i])
            {
                continue;
            }

            var title = NormalizeTitle(list[i].Name, config.GroupEditions, config);
            if (!byTitle.TryGetValue(title, out var earlier))
            {
                byTitle[title] = earlier = new List<int>();
            }

            foreach (var other in earlier)
            {
                if (list[i].Year is not int a || list[other].Year is not int b || Math.Abs(a - b) <= 2
                    || list[i].Developers.Length == 0 || list[other].Developers.Length == 0
                    || SameStudio(list[i].Developers, list[other].Developers))
                {
                    parent[Root(i)] = Root(other);
                }
            }

            earlier.Add(i);
            foreach (var appId in SteamIds(list[i]))
            {
                Join("steam:" + appId, i);
            }
        }

        var order = MainVersionComparer(config);
        return Enumerable.Range(0, list.Count)
            .GroupBy(Root)
            .Select(group => WithoutTwins(group.Select(i => list[i]), order, config))
            .ToList();
    }

    // Twins keep one entry: installed first, then the latest last activity, then the earliest added (none last), then the smaller Playnite id.
    private static readonly IComparer<GameEntry> TwinOrder = Comparer<GameEntry>.Create((a, b) =>
    {
        var result = b.Installed.CompareTo(a.Installed);
        if (result == 0)
        {
            result = a.LastActivity == b.LastActivity ? 0 : a.LastActivity is null ? 1 : b.LastActivity is null ? -1 : b.LastActivity.Value.CompareTo(a.LastActivity.Value);
        }

        if (result == 0)
        {
            result = a.Added == b.Added ? 0 : a.Added is null ? 1 : b.Added is null ? -1 : a.Added.Value.CompareTo(b.Added.Value);
        }

        return result != 0 ? result : string.CompareOrdinal(a.Id, b.Id);
    });

    /// <summary>
    /// A group without twins (entries with the same store key, the same title as NormalizeTitle without editions and the same platforms,
    /// e.g. two Steam entries of one game): one of them stays (TwinOrder), before the main version is chosen.
    /// </summary>
    private static GameGroup WithoutTwins(IEnumerable<GameEntry> versions, IComparer<GameEntry> order, PluginConfiguration config)
    {
        var kept = new List<GameEntry>();
        var twins = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var same in versions.GroupBy(v => v.StoreKey + "\n" + NormalizeTitle(v.Name, false, PlainConfig) + "\n"
            + string.Join("\n", v.Platforms.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal)), StringComparer.Ordinal))
        {
            var keep = same.OrderBy(v => v, TwinOrder).First();
            kept.Add(keep);
            foreach (var twin in same.Where(v => v != keep))
            {
                twins[twin.Id] = keep.Id;
            }
        }

        return new GameGroup(DemosBelowTheirPlatform(kept.OrderBy(v => v, order).ToList(), config), twins);
    }

    /// <summary>
    /// The groups of the detail page's version dropdown, at least as wide as the library's groups (so every version stays reachable): Show Other Platforms
    /// or the library's Group Games put the same game of other stores into one group, Show Editions or the library's Group Editions (with Group Games)
    /// add the editions (the edition words); with only Show Editions a group is split by store, with none of them every game is alone.
    /// </summary>
    public static List<GameGroup> DetailGroups(IEnumerable<GameEntry> games, PluginConfiguration config)
    {
        var platforms = config.ShowOtherPlatforms || config.GroupGames;
        var detail = config.Clone();
        detail.GroupGames = platforms || config.ShowEditions;
        detail.GroupEditions = config.ShowEditions || (config.GroupGames && config.GroupEditions);
        var groups = Group(games, detail);
        if (platforms)
        {
            return groups;
        }

        var order = MainVersionComparer(detail);
        return groups.SelectMany(g => g.Versions.GroupBy(v => v.StoreKey).Select(store =>
            new GameGroup(DemosBelowTheirPlatform(store.OrderBy(v => v, order).ToList(), detail),
                g.Twins?.Where(t => store.Any(v => v.Id == t.Value)).ToDictionary(t => t.Key, t => t.Value)))).ToList();
    }

    /// <summary>
    /// With GroupDemosAndTestBuilds a demo or test build is never the main version and stands right below the main or alternative version of its own platform
    /// (Steam/Main, Steam/Demo, EA/Main, EA/Demo), so the platform order is not torn apart; a demo of a platform without another version comes last.
    /// </summary>
    private static List<GameEntry> DemosBelowTheirPlatform(List<GameEntry> ordered, PluginConfiguration config)
    {
        if (!config.GroupDemosAndTestBuilds || ordered.Count < 2)
        {
            return ordered;
        }

        var demos = ordered.Where(v => IsDemo(v.Name, config)).ToList();
        var normal = ordered.Where(v => !demos.Contains(v)).ToList();
        if (demos.Count == 0 || normal.Count == 0)
        {
            return ordered;
        }

        var result = new List<GameEntry>();
        foreach (var version in normal)
        {
            result.Add(version);
            foreach (var demo in demos.Where(d => d.StoreKey == version.StoreKey && !result.Contains(d)))
            {
                result.Add(demo);
            }
        }

        result.AddRange(demos.Where(d => !result.Contains(d)));
        return result;
    }

    /// <summary>The group that holds a Playnite id and the version that stands for it: the entry itself, or for a twin left out the version kept in its place.</summary>
    public static (GameGroup Group, GameEntry Version)? Find(IEnumerable<GameGroup> groups, string id)
    {
        foreach (var group in groups)
        {
            var shown = group.Twins is not null && group.Twins.TryGetValue(id, out var kept) ? kept : id;
            var version = group.Versions.FirstOrDefault(v => v.Id == shown);
            if (version is not null)
            {
                return (group, version);
            }
        }

        return null;
    }

    /// <summary>
    /// The rank of a store in the platform list <paramref name="platformOrder"/> (comma-separated store keys, trimmed, case ignored;
    /// unknown entries ignored, the first of duplicates counts). Stores not listed come alphabetically by name right before "custom";
    /// without "custom" in the list it comes last. Compare the tuples in order.
    /// </summary>
    public static (int Position, int Tier, string Name) PlatformRank(string storeKey, string storeName, string platformOrder)
    {
        var known = new HashSet<string>(GameQuery.StoreNames.Values.Select(s => s.Key).Append("custom"), StringComparer.OrdinalIgnoreCase);
        var listed = platformOrder.Split(',').Select(k => k.Trim().ToLowerInvariant()).Where(known.Contains).Distinct().ToList();
        var custom = listed.IndexOf("custom");
        var customPosition = custom < 0 ? listed.Count : custom;
        var position = listed.IndexOf(storeKey.ToLowerInvariant());
        if (storeKey == "custom")
        {
            return (customPosition, 1, string.Empty);
        }

        return position >= 0 ? (position, 0, string.Empty) : (customPosition, 0, storeName.ToLowerInvariant());
    }

    /// <summary>Main version order: the steps of MainVersionOrder (installed first, last activity descending with none last), then the platform list, then the Playnite id.</summary>
    public static IComparer<GameEntry> MainVersionComparer(PluginConfiguration config)
    {
        var installed = config.MainVersionOrder is MainVersionOrder.InstalledLastPlayedPlatform or MainVersionOrder.InstalledPlatform or MainVersionOrder.LastPlayedInstalledPlatform;
        var lastPlayed = config.MainVersionOrder is MainVersionOrder.InstalledLastPlayedPlatform or MainVersionOrder.LastPlayedPlatform or MainVersionOrder.LastPlayedInstalledPlatform;
        var lastPlayedFirst = config.MainVersionOrder == MainVersionOrder.LastPlayedInstalledPlatform;
        var ranks = new Dictionary<string, (int, int, string)>();
        (int, int, string) Rank(GameEntry g)
        {
            if (!ranks.TryGetValue(g.StoreKey, out var rank))
            {
                rank = PlatformRank(g.StoreKey, g.StoreName, config.PlatformOrder);
                ranks[g.StoreKey] = rank;
            }

            return rank;
        }

        return Comparer<GameEntry>.Create((a, b) =>
        {
            var result = 0;
            if (installed && !lastPlayedFirst)
            {
                result = b.Installed.CompareTo(a.Installed);
            }

            if (result == 0 && lastPlayed)
            {
                result = a.LastActivity == b.LastActivity ? 0
                    : a.LastActivity is null ? 1
                    : b.LastActivity is null ? -1
                    : b.LastActivity.Value.CompareTo(a.LastActivity.Value);
            }

            if (result == 0 && installed && lastPlayedFirst)
            {
                result = b.Installed.CompareTo(a.Installed);
            }

            if (result == 0)
            {
                var (ra, rb) = (Rank(a), Rank(b));
                result = ra.Item1 != rb.Item1 ? ra.Item1.CompareTo(rb.Item1)
                    : ra.Item2 != rb.Item2 ? ra.Item2.CompareTo(rb.Item2)
                    : string.CompareOrdinal(ra.Item3, rb.Item3);
            }

            return result != 0 ? result : string.CompareOrdinal(a.Id, b.Id);
        });
    }

    /// <summary>The store a custom game's source names (store name or key, case ignored); null for any other game or an unknown source.</summary>
    private static (string Key, string Name)? CustomSource(GameEntry game) =>
        game.StoreKey != "custom" || string.IsNullOrEmpty(game.SourceName)
            ? null
            : GameQuery.StoreNames.Values.Cast<(string Key, string Name)?>().FirstOrDefault(s =>
                string.Equals(s!.Value.Name, game.SourceName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Value.Key, game.SourceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>The icon of a version: its store key when there is an icon file, a custom game the icon of its source's store; else "custom".</summary>
    public static string IconKey(GameEntry game)
    {
        var key = CustomSource(game)?.Key ?? game.StoreKey;
        return key != "custom" && IconKeys.Contains(key) ? key : "custom";
    }

    // Name of a version's store as the user sees it: a custom game with a known source carries that store's name.
    internal static string StoreLabel(GameEntry game) => CustomSource(game)?.Name ?? game.StoreName;

    /// <summary>
    /// The store names (StoreLabel) of the given games, each once (case ignored), in the order of the platform list
    /// <paramref name="platformOrder"/> (PlatformRank; a custom game with a known source ranks as its source's store): the game
    /// page's row "Source" for a group's versions, the source group of the filter dialog for all visible games.
    /// </summary>
    public static string[] Sources(IEnumerable<GameEntry> games, string platformOrder) =>
        games.Select(g => (Name: StoreLabel(g), Rank: CustomSource(g) is { } source
                ? PlatformRank(source.Key, source.Name, platformOrder)
                : PlatformRank(g.StoreKey, g.StoreName, platformOrder)))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.First())
            .OrderBy(s => s.Rank.Position).ThenBy(s => s.Rank.Tier).ThenBy(s => s.Rank.Name, StringComparer.Ordinal)
            .Select(s => s.Name)
            .ToArray();

    /// <summary>
    /// The icons of a tile: one per version, each store once; with MarkerScope MultipleVersions none for a group with one version
    /// (so none at all without grouping). See <see cref="StoreIcons"/>.
    /// </summary>
    public static string[] MarkerIcons(GameGroup group, PluginConfiguration config) =>
        config.MarkerScope == MarkerScope.MultipleVersions && group.Versions.Count < 2 ? Array.Empty<string>() : StoreIcons(group, config);

    /// <summary>
    /// The icons on the cover of a game's detail page, from the detail page's own marker settings (kind Icons only; Show: only while the group
    /// offers several versions, or always; Most Icons 2 to 5 or unlimited). See <see cref="Icons"/>.
    /// </summary>
    public static string[] DetailMarkerIcons(GameGroup group, PluginConfiguration config) =>
        config.DetailMarkerKind != VersionMarkerKind.Icons || (config.DetailMarkerShow == DetailMarkerShowMode.OffersVersions && group.Versions.Count < 2)
            ? Array.Empty<string>()
            : Icons(group, config.DetailMaxMarkerIcons <= 0 ? int.MaxValue : Math.Clamp(config.DetailMaxMarkerIcons, 2, 5));

    /// <summary>The number on the cover of the detail page (kind Number): the number of versions of the group, only for more than one; else null.</summary>
    public static int? DetailMarkerNumber(GameGroup group, PluginConfiguration config) =>
        config.DetailMarkerKind == VersionMarkerKind.Number && group.Versions.Count > 1 ? group.Versions.Count : null;

    /// <summary>
    /// The most icons of a marker: MaxMarkerIcons within 2-5; in the top row beside Jellyfin's played check mark (MarkerPosition top,
    /// PlayedMark not Off) at most 4 (a desktop tile is about 165 px wide and holds four 26 px badges beside the 24 px mark).
    /// </summary>
    public static int MaxIcons(PluginConfiguration config)
    {
        var max = Math.Clamp(config.MaxMarkerIcons, 2, 5);
        var besidePlayed = config.MarkerPosition is VersionMarkerPosition.TopLeft or VersionMarkerPosition.TopRight && config.PlayedMark != PlayedMarkMode.Off;
        return besidePlayed ? Math.Min(max, 4) : max;
    }

    // One icon per version, each store once; none for the number marker or the position Off. Order GOG, Steam, Ubisoft, EA, then
    // alphabetical by store name, custom games last; more than MaxIcons = one fewer and "…".
    private static string[] StoreIcons(GameGroup group, PluginConfiguration config)
    {
        if (config.MarkerKind != VersionMarkerKind.Icons || config.MarkerPosition == VersionMarkerPosition.Off)
        {
            return Array.Empty<string>();
        }

        return Icons(group, MaxIcons(config));
    }

    // One icon per store: two versions from the same store (e.g. EA and a custom game with source EA) show it once.
    private static string[] Icons(GameGroup group, int max)
    {
        var icons = MarkerOrder(group).Select(IconKey).Distinct().ToList();
        return icons.Count > max ? icons.Take(max - 1).Append(More).ToArray() : icons.ToArray();
    }

    /// <summary>The option labels of a group's versions in the icon order (the tooltip of a tile's icons).</summary>
    public static string[] MarkerLabels(GameGroup group)
    {
        var labels = group.Versions.Zip(OptionLabels(group)).ToDictionary(x => x.First, x => x.Second);
        return MarkerOrder(group).Select(v => labels[v]).ToArray();
    }

    private static IEnumerable<GameEntry> MarkerOrder(GameGroup group) =>
        group.Versions
            .OrderBy(v => v.StoreKey == "custom" ? 1 : 0)
            .ThenBy(v => Array.IndexOf(IconOrder, v.StoreKey) is var i && i >= 0 ? i : IconOrder.Length)
            .ThenBy(v => StoreLabel(v), StringComparer.OrdinalIgnoreCase);

    /// <summary>The label of a version in the version list (see <see cref="OptionLabels"/>).</summary>
    public static string OptionLabel(GameGroup group, GameEntry version) =>
        OptionLabels(group)[group.Versions.ToList().IndexOf(version)];

    /// <summary>
    /// The labels of a group's versions in version order, after Jellyfin's GetMediaSourceName (only what tells the
    /// versions apart, terms joined by "/"): the store's name; the short platform names ("/"-joined) when another version with the
    /// same store and title part has other platforms; the title part (see <see cref="TitlePart"/>) when the title differs from the
    /// group's base title, the longest common word prefix of all titles (NormalizeTitle without editions), and another version has the
    /// same store (EA + Steam = "EA", "Steam"; Steam + Steam "Digital Deluxe" = "Steam", "Steam/Digital Deluxe"), and then also on
    /// every other version with that title part (EA, EA N7, Steam N7 = "EA", "EA/N7 …", "Steam/N7 …"). Equal labels left: the
    /// first in version order stays, the others get "/2", "/3" and so on.
    /// </summary>
    public static string[] OptionLabels(GameGroup group)
    {
        var versions = group.Versions;
        var common = CommonWords(group);
        var parts = versions.Select(v => TitlePart(v.Name, common)).ToArray();
        var platforms = versions.Select(v => v.Platforms.Distinct(StringComparer.Ordinal).ToArray()).ToArray();
        bool SamePlatforms(int i, int j) => platforms[i].Length == platforms[j].Length && !platforms[i].Except(platforms[j], StringComparer.Ordinal).Any();
        var labels = versions.Select((v, i) =>
        {
            var terms = new List<string> { StoreLabel(v) };
            if (platforms[i].Length > 0
                && versions.Where((o, j) => j != i && StoreLabel(o) == StoreLabel(v) && parts[j] == parts[i] && !SamePlatforms(i, j)).Any())
            {
                terms.Add(string.Join('/', platforms[i].Select(p => ShortPlatforms.GetValueOrDefault(p, p))));
            }

            if (parts[i].Length > 0 && versions.Where((o, j) => j != i && StoreLabel(o) == StoreLabel(v)).Any())
            {
                terms.Add(parts[i]);
            }

            return terms;
        }).ToArray();

        // A title part shown on one version is shown on every version with that title part (EA, "EA/N7 Digital Deluxe Edition",
        // "Steam/N7 Digital Deluxe Edition").
        var shown = labels.Select((t, i) => t.Contains(parts[i]) ? parts[i] : null).Where(p => p is not null).ToHashSet(StringComparer.Ordinal);
        var joined = labels.Select((t, i) => string.Join('/', parts[i].Length > 0 && shown.Contains(parts[i]) && !t.Contains(parts[i]) ? t.Append(parts[i]) : t)).ToArray();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return joined.Select(l =>
        {
            seen[l] = seen.GetValueOrDefault(l) + 1;
            return seen[l] == 1 ? l : l + "/" + seen[l];
        }).ToArray();
    }

    /// <summary>
    /// The title of a game (tile and detail page, every version alike): the name of the first version in version order whose title is
    /// the group's base title (no title part, see <see cref="OptionLabels"/>), else the main version's name ("Mass Effect 3" for EA,
    /// EA N7 and Steam N7, whichever is the main version).
    /// </summary>
    public static string Title(GameGroup group)
    {
        var common = CommonWords(group);
        return group.Versions.FirstOrDefault(v => TitlePart(v.Name, common).Length == 0)?.Name ?? group.Main.Name;
    }

    // The number of leading words all titles of a group share (NormalizeTitle without editions).
    private static int CommonWords(GameGroup group)
    {
        var words = group.Versions.Select(v => NormalizeTitle(v.Name, false, PlainConfig).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        return Enumerable.Range(0, words.Min(w => w.Length)).TakeWhile(i => words.All(w => w[i] == words[0][i])).Count();
    }

    /// <summary>
    /// The title after its first <paramref name="words"/> words (words as in NormalizeTitle: runs of letters and digits, ™ ® © left
    /// out) as Playnite writes it, without separators at either end and with "/" for a dash inside; empty when nothing follows or
    /// <paramref name="words"/> is 0, so never the whole title.
    /// </summary>
    private static string TitlePart(string name, int words)
    {
        if (words == 0)
        {
            return string.Empty;
        }

        var count = 0;
        var inWord = false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c is '™' or '®' or '©')
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                inWord = true;
            }
            else if (inWord)
            {
                inWord = false;
                if (++count == words)
                {
                    // A dash inside the part separates terms as at its ends: they are joined by "/" like the label's other terms.
                    return string.Join('/', name.Substring(i).Split(new[] { " - ", " – ", " — " }, StringSplitOptions.None)
                        .Select(TrimTerm).Where(t => t.Length > 0));
                }
            }
        }

        return string.Empty;
    }

    // A term without separators at either end; brackets are removed only when they enclose the whole term ("(Beta)" = "Beta",
    // "(2010) Edition" stays).
    private static string TrimTerm(string term)
    {
        var t = term.Trim(TitlePartTrim);
        while (t.Length > 1 && ((t[0] == '(' && t[^1] == ')') || (t[0] == '[' && t[^1] == ']')) && t.IndexOfAny(Brackets, 1) == t.Length - 1)
        {
            t = t[1..^1].Trim(TitlePartTrim);
        }

        return t;
    }

    /// <summary>Jellyfin's MediaSourceCount of a tile: the number of versions for the number marker (not Off) and more than one version; else null.</summary>
    public static int? MediaSourceCount(GameGroup group, PluginConfiguration config) =>
        config.MarkerKind == VersionMarkerKind.Number && config.MarkerPosition != VersionMarkerPosition.Off && group.Versions.Count > 1
            ? group.Versions.Count
            : null;
}
