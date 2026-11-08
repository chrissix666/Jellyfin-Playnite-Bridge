using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Parses the title of a Steam video (Steam's own name, mostly English) into a label key; the web script shows it
/// in the viewer's language. The rules live in <see cref="Rules"/>, first match wins; a title nothing matches is "Clip".
/// </summary>
public static class SteamVideoLabels
{
    /// <summary>The label of a trailer or teaser whose title has none of the <see cref="TrailerQualifiers"/>, and of an unparsed title.</summary>
    public const string Trailer = "Trailer";

    public const string Clip = "Clip";

    // Words that carry no label (whole tokens only). Tokens of digits are dropped too.
    private static readonly HashSet<string> Noise = new(StringComparer.Ordinal)
    {
        "pegi", "esrb", "en", "eng", "english", "uk", "de", "fr", "hd", "official", "edition"
    };

    // Words glued to others in titles (launchtrailer, traileren, gameplaytrailer) are cut out of their token; only these, so "launcher" stays one word.
    private static readonly Regex Glued = new("(trailers?|teasers?|gameplay|cinematics?)", RegexOptions.Compiled);

    private static readonly Regex Camel = new("(?<=[a-z])(?=[A-Z])", RegexOptions.Compiled);

    private static readonly Regex NonLetters = new("[^a-z0-9]+", RegexOptions.Compiled);

    private static Regex Words(string alternatives) => new("(?: )(?:" + alternatives + ")(?: )", RegexOptions.Compiled);

    /// <summary>"Trailer" or "Teaser" in the title: the qualifier decides the label.</summary>
    private static readonly Regex IsTrailer = Words("trailers?|teasers?");

    private static readonly Regex IsTeaser = Words("teasers?");

    /// <summary>The qualifier of a trailer, first match in this order; "release date" is an announcement, so it comes before "release".</summary>
    private static readonly (string Label, Regex Match)[] TrailerQualifiers =
    {
        ("GameplayTrailer", Words("gameplay")),
        ("LaunchTrailer", Words("launch")),
        ("AnnouncementTrailer", Words("release date")),
        ("ReleaseTrailer", Words("release")),
        ("AnnouncementTrailer", Words("announce|announcement|reveal|date")),
        ("CinematicTrailer", Words("cinematics?")),
        ("StoryTrailer", Words("story")),
        ("AccoladesTrailer", Words("accolades?|awards?")),
        ("UpdateTrailer", Words("update|updates|dlc|season|expansion|pack"))
    };

    /// <summary>The remaining rules, first match wins.</summary>
    private static readonly (string Label, Regex Match)[] Rules =
    {
        ("Gameplay", Words("gameplay|walkthrough|overview|footage|demo|deep dive|first look")),
        ("Intro", Words("intro|opening|introduction|opening crawl|welcome")),
        ("Cutscene", Words("cutscenes?|cut scenes?")),
        ("Cinematic", Words("cinematics?|cgi|animated|story")),
        ("Tutorial", Words("tutorials?")),
        ("DeveloperDiary", Words("diary|diaries|dev diary|developers?|making|behind the scenes|dev")),
        ("Trivia", Words("trivia")),
        ("Features", Words("features?")),
        ("Accolades", Words("accolades?|awards?")),
        ("Update", Words("updates?|dlc|season|expansion|pack|content")),
        ("Showcase", Words("showcase|sizzle")),
        ("Spotlight", Words("spotlight")),
        ("Preview", Words("preview")),
        ("Promo", Words("promo")),
        ("Event", Words("event|gamescom|e3")),
        ("Launch", Words("launch|out now|release")),
        ("Announcement", Words("announce|announcement|reveal|date|coming"))
    };

    /// <summary>The title as the rules read it: lower case words separated by single spaces, glued words cut apart, noise dropped, a space at both ends.</summary>
    public static string Normalise(string? title)
    {
        var tokens = NonLetters.Split(Camel.Replace(title ?? string.Empty, " ").ToLowerInvariant())
            .SelectMany(token => Glued.Split(token))
            .Where(token => token.Length > 0 && !Noise.Contains(token) && !token.All(char.IsDigit));
        return " " + string.Join(' ', tokens) + " ";
    }

    /// <summary>The label key of a Steam video title: Trailer, Clip, or one of the keys of the rule tables.</summary>
    public static string Label(string? title)
    {
        var text = Normalise(title);
        if (IsTrailer.IsMatch(text))
        {
            foreach (var (label, match) in TrailerQualifiers)
            {
                if (match.IsMatch(text))
                {
                    return label;
                }
            }

            return IsTeaser.IsMatch(text) ? "Teaser" : Trailer;
        }

        foreach (var (label, match) in Rules)
        {
            if (match.IsMatch(text))
            {
                return label;
            }
        }

        return Clip;
    }
}
