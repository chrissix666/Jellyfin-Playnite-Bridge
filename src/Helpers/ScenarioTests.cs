using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The admin page's Scenario Test buttons: which games a word list of the form hits, computed with the grouping and hiding rules of
/// <see cref="GameGroups"/> on a copy of the settings; pure, nothing is saved or written.
/// </summary>
public static class ScenarioTests
{
    // Hide Titles: each category's switch on a copy where only that category is on.
    private static readonly Dictionary<string, Action<PluginConfiguration>> HideOnly = new(StringComparer.Ordinal)
    {
        ["hide-demos"] = c => c.HideDemos = true,
        ["hide-testbuilds"] = c => c.HideTestBuilds = true,
        ["hide-soundtracks"] = c => c.HideSoundtracks = true,
        ["hide-dlc"] = c => c.HideDlcAndPacks = true,
        ["hide-prologues"] = c => c.HideProloguesAndTrials = true
    };

    /// <summary>The scenarios the endpoint knows.</summary>
    public static readonly string[] Names = HideOnly.Keys.Concat(new[] { "hide-titles", "hide-never", "editions", "demos", "nevergroup", "roman" }).ToArray();

    /// <summary>The settings the scenarios read; the only ones <see cref="Overlay"/> takes from the form.</summary>
    public static readonly string[] Settings =
    {
        "HideDemos", "HideDemosWords", "HideTestBuilds", "HideTestBuildsWords", "HideSoundtracks", "HideSoundtracksWords", "HideDlcAndPacks", "HideDlcAndPacksWords",
        "HideProloguesAndTrials", "HideProloguesAndTrialsWords", "HideTitlesEnabled", "HideTitles", "NeverHideTitlesEnabled", "NeverHideTitles", "EditionWords", "DemoWords", "NeverGroupTitles", "RecognizeEditions",
        "GroupDemosAndTestBuilds", "NeverGroupTitlesEnabled", "RomanNumerals", "GroupGames", "GroupEditions", "ShowOtherPlatforms", "ShowEditions", "ShowHidden", "ApplyParentalControls"
    };

    /// <summary>
    /// A copy of <paramref name="saved"/> with the form's values laid over it: every name of <see cref="Settings"/> with a value of its type (true or false for
    /// a switch, a string for a list); other names and values of another type are ignored. Null when <paramref name="settings"/> is given but not an object.
    /// </summary>
    public static PluginConfiguration? Overlay(PluginConfiguration saved, JsonElement settings)
    {
        var config = saved.Clone();
        if (settings.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return config;
        }

        if (settings.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var entry in settings.EnumerateObject())
        {
            var property = Settings.Contains(entry.Name, StringComparer.Ordinal) ? typeof(PluginConfiguration).GetProperty(entry.Name) : null;
            var value = entry.Value;
            object? typed = property?.PropertyType == typeof(bool) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean()
                : property?.PropertyType == typeof(string) && value.ValueKind == JsonValueKind.String ? value.GetString()
                : null;
            if (typed is not null)
            {
                property!.SetValue(config, typed);
            }
        }

        return config;
    }

    /// <summary>A Jellyfin user for the parental state of the scenario lines: the name and whether that user's parental controls allow a game.</summary>
    public sealed record ScenarioUser(string Name, Func<GameEntry, bool> Allowed);

    /// <summary>
    /// One line of a scenario: <see cref="Text"/> (the title, its store when the title occurs twice, and the result after an arrow), <see cref="State"/> (one of
    /// <see cref="States"/>) and the users whose parental controls block the game (<see cref="ScenarioUser"/>; empty while ApplyParentalControls is off).
    /// </summary>
    public sealed record ScenarioLine(string Text, string State, string[] BlockedFor);

    /// <summary>The answer of a scenario: its lines in order and the entries of the whole-title lists that match no game (only Hide These Titles, Never Hide These Titles, Never Group These Titles).</summary>
    public sealed record ScenarioResult(ScenarioLine[] Lines, string[] Unmatched);

    /// <summary>The states a line carries; the page colours by state, never by text.</summary>
    public static readonly string[] States = { "joins", "already", "held", "none", "hidden", "conflict", "normal" };

    /// <summary>The lines of a scenario as plain texts (<see cref="Evaluate"/> without users); null for an unknown scenario.</summary>
    public static string[]? Run(string scenario, IReadOnlyList<GameEntry> all, PluginConfiguration config) =>
        Evaluate(scenario, all, config)?.Lines.Select(l => l.Text).ToArray();

    /// <summary>
    /// The lines of a scenario, sorted by Jellyfin's sort name (<see cref="GameQuery.SortName"/>, case ignored); null for an unknown scenario.
    /// <paramref name="all"/> = every game of the copy, also those hidden in Playnite (<see cref="PlayniteReader.ReadShownGames"/> with true); the Hide Titles
    /// scenarios look at the games ShowHidden leaves, the grouping scenarios at all of them. A title that occurs more than once in the answer carries its store.
    /// hide-*: the titles that category's words hit with its switch on and no Never Hide list (state normal); hide-titles: the titles of Hide These Titles with its
    /// switch counted as on (normal; conflict when Never Hide These Titles is on and holds the title too); hide-never: the titles the words of the five
    /// categories and Hide These Titles hit (every category switch counted as on) but Never Hide These Titles (its switch counted as on) keeps visible (normal;
    /// conflict when Hide These Titles is on and holds the title too). The grouping scenarios list a title whatever hides it: a title that Playnite hides
    /// (ShowHidden off) or that Hide Titles hides (<see cref="GameGroups.HiddenBy"/>, the form's switches) has the state hidden and the rule in its text, and
    /// does not take part in the groups. editions: the titles an edition word shortens (Recognize Editions counted as on): joins (the group gets more
    /// versions with the word, text = the group's title), already (the title shares a group without the word), none; demos: the same for the titles a demo
    /// or test build word shortens (Group Demos and Test Builds counted as on against off, Group Editions as in the form); nevergroup: the titles of Never Group
    /// These Titles (its switch counted as on): held (the versions they would join without the entry), none (single copy); roman: the titles that end in a roman
    /// numeral, with its number (normal). With ApplyParentalControls on and <paramref name="users"/> every line names the users whose parental controls block the
    /// game. Unmatched: for hide-titles, hide-never and nevergroup the entries of that list that match no game of <paramref name="all"/>.
    /// </summary>
    public static ScenarioResult? Evaluate(string scenario, IReadOnlyList<GameEntry> all, PluginConfiguration config, IReadOnlyList<ScenarioUser>? users = null)
    {
        var shown = all.Where(g => config.ShowHidden || !g.Hidden).ToList();
        // why a game takes no part in the grouping: Playnite's flag first, then the first Hide Titles rule
        string? Why(GameEntry g) => !config.ShowHidden && g.Hidden ? "Playnite" : GameGroups.HiddenBy(g.Name, config);
        var visible = all.Where(g => Why(g) is null).ToList();
        (string Detail, string State) Hidden(GameEntry g) => (" -> hidden by " + Why(g), "hidden");
        List<(GameEntry Game, string Detail, string State)> hits;
        var unmatched = Array.Empty<string>();
        if (HideOnly.TryGetValue(scenario, out var switchOn))
        {
            var only = config.Clone();
            only.HideDemos = only.HideTestBuilds = only.HideSoundtracks = only.HideDlcAndPacks = only.HideProloguesAndTrials = false;
            only.HideTitlesEnabled = false;
            only.NeverHideTitles = string.Empty;
            switchOn(only);
            hits = shown.Where(g => GameGroups.HiddenByTitle(g.Name, only)).Select(g => (g, string.Empty, "normal")).ToList();
        }
        else if (scenario == "hide-titles")
        {
            var on = config.Clone();
            on.HideTitlesEnabled = true;
            hits = shown.Where(g => GameGroups.InTitleList(g.Name, on.HideTitles)).Select(g =>
                config.NeverHideTitlesEnabled && GameGroups.InTitleList(g.Name, config.NeverHideTitles)
                    ? (g, " -> conflict: also in Never Hide These Titles, stays visible", "conflict") : (g, string.Empty, "normal")).ToList();
            unmatched = GameGroups.UnmatchedTitles(config.HideTitles, all.Select(g => g.Name));
        }
        else if (scenario == "hide-never")
        {
            var allOn = config.Clone();
            allOn.HideDemos = allOn.HideTestBuilds = allOn.HideSoundtracks = allOn.HideDlcAndPacks = allOn.HideProloguesAndTrials = true;
            allOn.NeverHideTitlesEnabled = true;
            var noList = allOn.Clone();
            noList.NeverHideTitles = string.Empty;
            hits = shown.Where(g => GameGroups.HiddenByTitle(g.Name, noList) && !GameGroups.HiddenByTitle(g.Name, allOn)).Select(g =>
                config.HideTitlesEnabled && GameGroups.InTitleList(g.Name, config.HideTitles)
                    ? (g, " -> conflict: also in Hide These Titles", "conflict") : (g, string.Empty, "normal")).ToList();
            unmatched = GameGroups.UnmatchedTitles(config.NeverHideTitles, all.Select(g => g.Name));
        }
        else if (scenario == "editions")
        {
            var on = config.Clone();
            on.RecognizeEditions = true;
            var with = Grouped(visible, on, true);
            var without = Grouped(visible, on, false);
            hits = all.Where(g => GameGroups.NormalizeTitle(g.Name, true, on) != GameGroups.NormalizeTitle(g.Name, false, on))
                .Select(g => Why(g) is not null ? (g, Hidden(g).Detail, "hidden") : Joined(g, with, without)).ToList();
        }
        else if (scenario == "demos")
        {
            var on = config.Clone();
            on.GroupDemosAndTestBuilds = true;
            var off = on.Clone();
            off.GroupDemosAndTestBuilds = false;
            var with = Grouped(visible, on, config.GroupEditions);
            var without = Grouped(visible, off, config.GroupEditions);
            hits = all.Where(g => GameGroups.NormalizeTitle(g.Name, config.GroupEditions, on) != GameGroups.NormalizeTitle(g.Name, config.GroupEditions, off))
                .Select(g => Why(g) is not null ? (g, Hidden(g).Detail, "hidden") : Joined(g, with, without)).ToList();
        }
        else if (scenario == "nevergroup")
        {
            var on = config.Clone();
            on.NeverGroupTitlesEnabled = true;
            var off = on.Clone();
            off.NeverGroupTitlesEnabled = false;
            var groups = Grouped(visible, off, config.GroupEditions || config.ShowEditions);
            hits = all.Where(g => GameGroups.NeverGrouped(g.Name, on)).Select(g =>
            {
                if (Why(g) is not null)
                {
                    return (g, Hidden(g).Detail, "hidden");
                }

                var others = GameGroups.Find(groups, g.Id)?.Group.Versions.Where(v => v.Id != g.Id).ToList() ?? new List<GameEntry>();
                return others.Count == 0 ? (g, " -> no effect (single copy)", "none")
                    : (g, " -> kept apart from: " + string.Join(", ", others.Select(v => v.Name + " (" + GameGroups.StoreLabel(v) + ")")), "held");
            }).ToList();
            unmatched = GameGroups.UnmatchedNeverGroup(on, all.Select(g => g.Name));
        }
        else if (scenario == "roman")
        {
            hits = all.Select(g => (Game: g, Number: GameGroups.RomanNumber(g.Name))).Where(x => x.Number is not null)
                .Select(x => Why(x.Game) is not null ? (x.Game, Hidden(x.Game).Detail, "hidden") : (x.Game, " = " + x.Number, "normal")).ToList();
        }
        else
        {
            return null;
        }

        var repeated = hits.GroupBy(h => h.Game.Name, StringComparer.Ordinal).Where(n => n.Count() > 1).Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        // a line that would equal an earlier one (the same title twice on one store) carries the first 8 characters of its Playnite id
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var parental = config.ApplyParentalControls ? users : null;
        var lines = hits
            .OrderBy(h => GameQuery.SortName(h.Game), StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Game.Name, StringComparer.Ordinal)
            .ThenBy(h => GameGroups.StoreLabel(h.Game), StringComparer.Ordinal)
            .ThenBy(h => h.Game.Id, StringComparer.Ordinal)
            .Select(h =>
            {
                var label = h.Game.Name + (repeated.Contains(h.Game.Name) ? " (" + GameGroups.StoreLabel(h.Game) + ")" : string.Empty);
                if (!seen.Add(label + h.Detail))
                {
                    var id = h.Game.Id.Replace("-", string.Empty, StringComparison.Ordinal);
                    label += " [" + id[..Math.Min(8, id.Length)] + "]";
                }

                var blocked = parental?.Where(u => !u.Allowed(h.Game)).Select(u => u.Name).ToArray() ?? Array.Empty<string>();
                return new ScenarioLine(label + h.Detail, h.State, blocked);
            })
            .ToArray();
        return new ScenarioResult(lines, unmatched);
    }

    // The state of a title that takes part in the grouping: joins (its group has more versions with the word than without), already (it shares a group
    // without the word), none (it stays alone).
    private static (GameEntry Game, string Detail, string State) Joined(GameEntry game, List<GameGroup> with, List<GameGroup> without)
    {
        var after = GameGroups.Find(with, game.Id);
        var before = GameGroups.Find(without, game.Id)?.Group.Versions.Count ?? 1;
        return after is { } joined && joined.Group.Versions.Count > before ? (game, " -> " + GameGroups.Title(joined.Group), "joins")
            : before > 1 ? (game, " -> already joined", "already")
            : (game, " -> nothing to join", "none");
    }

    // The groups with Group Games on and Group Editions as given (the other settings as in config).
    private static List<GameGroup> Grouped(IEnumerable<GameEntry> games, PluginConfiguration config, bool editions)
    {
        var grouping = config.Clone();
        grouping.GroupGames = true;
        grouping.GroupEditions = editions;
        return GameGroups.Group(games, grouping);
    }
}
