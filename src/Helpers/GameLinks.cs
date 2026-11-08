using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>Orders and limits a game's Playnite links for the detail page; pure, no file or network access.</summary>
public static class GameLinks
{
    /// <summary>The reserved LinkHosts entry for the links named "Official" or "Official Website".</summary>
    public const string Official = "official";

    // The fixed order: official website, the stores in the order of the PlatformOrder default (Nintendo beside PlayStation),
    // Wikipedia, PCGamingWiki; every other host comes after them.
    private static readonly string[] Order =
    {
        Official, "steampowered.com", "epicgames.com", "gog.com", "ea.com", "ubisoft.com", "xbox.com", "itch.io", "rockstargames.com",
        "playstation.com", "nintendo.com", "wikipedia.org", "pcgamingwiki.com",
        "fandom.com", "youtube.com", "twitch.tv", "reddit.com", "discord.com", "x.com", "facebook.com", "instagram.com", "apps.apple.com", "play.google.com"
    };

    // Further hosts that belong to an entry of the order (the old name, short links, the other domain of the same service).
    private static readonly (string Entry, string Host)[] Aliases =
    {
        ("x.com", "twitter.com"), ("youtube.com", "youtu.be"), ("fandom.com", "wikia.com"), ("fandom.com", "wikia.org"), ("discord.com", "discord.gg"),
        ("discord.com", "discordapp.com"), ("reddit.com", "redd.it"), ("facebook.com", "fb.com"), ("facebook.com", "fb.me"), ("apps.apple.com", "itunes.apple.com")
    };

    // The readable name of a host for links with a placeholder name: the hosts of the fixed order, then other common hosts.
    private static readonly (string Host, string Name)[] HostNames =
    {
        ("steampowered.com", "Steam"), ("gog.com", "GOG"), ("epicgames.com", "Epic Games"), ("itch.io", "itch.io"), ("xbox.com", "Xbox"),
        ("playstation.com", "PlayStation"), ("nintendo.com", "Nintendo"), ("ea.com", "EA"), ("ubisoft.com", "Ubisoft"),
        ("rockstargames.com", "Rockstar Games"), ("wikipedia.org", "Wikipedia"), ("pcgamingwiki.com", "PCGamingWiki"),
        ("fandom.com", "Fandom"), ("wikia.com", "Fandom"), ("facebook.com", "Facebook"), ("twitter.com", "Twitter"), ("x.com", "X"),
        ("instagram.com", "Instagram"), ("youtube.com", "YouTube"), ("youtu.be", "YouTube"), ("discord.com", "Discord"), ("discord.gg", "Discord"),
        ("reddit.com", "Reddit"), ("twitch.tv", "Twitch"), ("metacritic.com", "Metacritic"), ("ign.com", "IGN"), ("mobygames.com", "MobyGames"),
        ("howlongtobeat.com", "HowLongToBeat"), ("apps.apple.com", "App Store"), ("itunes.apple.com", "App Store"), ("play.google.com", "Google Play"),
        ("discordapp.com", "Discord")
    };

    /// <summary>The icon keys of the links (Web\link-icons\&lt;key&gt;.svg): one per entry of the fixed order, in that order, and "link" for any other host.</summary>
    public static readonly string[] IconKeys =
    {
        "website", "steam", "epic", "gog", "ea", "ubisoft", "xbox", "itch", "rockstar", "playstation", "nintendo", "wikipedia", "pcgamingwiki", "fandom", "youtube", "twitch", "reddit", "discord", "x", "facebook", "instagram", "appstore", "googleplay", "link"
    };

    /// <summary>
    /// Valid http/https links in the order of <paramref name="order"/> (the LinkHostOrder setting; null = the fixed order; stable, Playnite's order
    /// within an entry), links of hosts outside the list after them in Playnite's order; with <paramref name="limitHosts"/>
    /// only links whose entry is listed in <paramref name="hosts"/> (comma-separated, "official" for the official website).
    /// A link with a placeholder name (empty, white space, "Unknown" or "Uknown") is named after its host.
    /// </summary>
    public static ExternalUrl[] Arrange(IEnumerable<ExternalUrl> links, bool limitHosts, string hosts, string? order = null)
    {
        var ranking = Ranking(order);
        var allowed = hosts.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        return links
            .Select(l => (Link: l, Uri: Uri.TryCreate(l.Url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null))
            .Where(x => x.Uri is not null)
            .Select(x =>
            {
                var official = IsOfficial(x.Link.Name);
                var entry = official ? Official : Order.FirstOrDefault(e => e != Official && Matches(x.Uri!.Host, e));
                var visible = !limitHosts || (official
                    ? allowed.Contains(Official, StringComparer.OrdinalIgnoreCase)
                    : allowed.Any(h => !string.Equals(h, Official, StringComparison.OrdinalIgnoreCase) && Matches(x.Uri!.Host, h)));
                var rank = entry is null ? ranking.Length : Array.IndexOf(ranking, entry);
                return (Visible: visible, Rank: rank, Link: new ExternalUrl { Name = official ? "Official Website" : IsPlaceholder(x.Link.Name) ? HostName(x.Uri!.Host) : x.Link.Name, Url = x.Uri!.AbsoluteUri });
            })
            .Where(x => x.Visible)
            .OrderBy(x => x.Rank)
            .Select(x => x.Link)
            .ToArray();
    }

    /// <summary>
    /// The entries of the fixed order as the admin list ranks them: the listed known entries first (each once, in the list's order), the unlisted
    /// known ones after them in the fixed order. Null or empty = the fixed order.
    /// </summary>
    private static string[] Ranking(string? order)
    {
        var listed = (order ?? string.Empty).Split(',').Select(h => h.Trim().ToLowerInvariant())
            .Where(h => Array.IndexOf(Order, h) >= 0).Distinct().ToList();
        return listed.Concat(Order.Where(e => !listed.Contains(e))).ToArray();
    }

    /// <summary>The icon key of an arranged link: "website" for the official website, the key of its host entry, else "link".</summary>
    public static string IconKey(ExternalUrl link)
    {
        if (IsOfficial(link.Name))
        {
            return IconKeys[0];
        }

        var host = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
        var index = Array.FindIndex(Order, e => e != Official && Matches(host, e));
        return index < 0 ? "link" : IconKeys[index];
    }

    /// <summary>The icon key the game page draws for an arranged link: only with LinkHostLimiter on and LinkDisplay Icon, else null (text).</summary>
    public static string? Icon(ExternalUrl link, PluginConfiguration config) =>
        config.LinkHostLimiter && config.LinkDisplay == LinkDisplayMode.Icon ? IconKey(link) : null;

    /// <summary>
    /// With icons (LinkHostLimiter on and LinkDisplay Icon) one icon per service, the first link of it in the arranged order; unknown hosts
    /// ("link") are different services each and all stay. In text mode every link stays.
    /// </summary>
    public static IEnumerable<ExternalUrl> OneIconPerService(IEnumerable<ExternalUrl> arranged, PluginConfiguration config)
    {
        if (!config.LinkHostLimiter || config.LinkDisplay != LinkDisplayMode.Icon)
        {
            return arranged;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return arranged.Where(l => IconKey(l) == "link" || seen.Add(IconKey(l)));
    }

    private static bool IsOfficial(string? name) =>
        string.Equals(name?.Trim(), "Official", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name?.Trim(), "Official Website", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlaceholder(string? name) =>
        string.IsNullOrWhiteSpace(name)
        || string.Equals(name.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name.Trim(), "Uknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>The name of a host from <see cref="HostNames"/>, else the host in lower case without a leading "www.".</summary>
    private static string HostName(string host)
    {
        var known = HostNames.FirstOrDefault(h => Matches(host, h.Host)).Name;
        var lower = host.ToLowerInvariant();
        return known ?? (lower.StartsWith("www.", StringComparison.Ordinal) ? lower.Substring(4) : lower);
    }

    /// <summary>The host is the entry itself or a sub-domain of it.</summary>
    private static bool Matches(string host, string entry) =>
        IsHost(host, entry) || Aliases.Any(a => a.Entry == entry && IsHost(host, a.Host));

    private static bool IsHost(string host, string entry) =>
        string.Equals(host, entry, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + entry, StringComparison.OrdinalIgnoreCase);
}
