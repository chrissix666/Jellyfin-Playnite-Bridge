using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>The five actions of a game version's detail page, in the column order of the admin page's start table.</summary>
public enum StartActionKind
{
    OpenInPlaynite,
    StartViaPlaynite,
    DirectStart,
    InstallViaPlaynite,
    InstallDirect
}

/// <summary>How a button acts: the browser opens the link on the PC, or the server starts the action.</summary>
public enum StartVia
{
    Link,
    Server
}

/// <summary>A start action: a URI (opened by the shell) or a program with its arguments.</summary>
public sealed record StartAction(string? Uri, string? Program, string[] Arguments)
{
    public static StartAction ForUri(string uri) => new(uri, null, Array.Empty<string>());

    public static StartAction ForProgram(string program, params string[] arguments) => new(null, program, arguments);

    /// <summary>The URI's scheme or the program's file name (for the log; no paths).</summary>
    public string Target => Uri is not null ? Uri.Split(':')[0] : Path.GetFileName(Program!);
}

/// <summary>One detail page button: its action, how it acts, the command and the key of its logo (playnite or the store key).</summary>
public sealed record GameAction(StartActionKind Kind, StartVia Via, StartAction Action, string Logo);

/// <summary>The launcher programs found on this PC (LauncherPaths); null = not found.</summary>
public sealed record Launchers(string? Gog, string? Rockstar, string? Riot)
{
    public static readonly Launchers None = new(null, null, null);
}

/// <summary>
/// A row of the admin page's start table: the store's games in Playnite (hidden ones included), the actions it can have (without the remote
/// start condition), whether its launcher was found (null = the store starts no program) and whether its Direct start falls back to Start via
/// Playnite for some games.
/// </summary>
public sealed record StoreRow(string Key, string Name, int Games, StartActionKind[] Capable, bool? LauncherFound, bool DirectFallback);

/// <summary>
/// The one decision of the start and install buttons; pure, no file, registry or process access.
/// Commands as Playnite itself handles them: playnite://playnite/start/&lt;GUID&gt; without an action
/// index, because Playnite 10.55 accepts exactly two arguments and silently drops more; a not installed game goes to InstallGame;
/// and playnite://playnite/showgame/&lt;GUID&gt; opens the game in the desktop app.
/// </summary>
public static class GameStart
{
    /// <summary>The only URI schemes the server opens.</summary>
    public static readonly string[] Schemes = { "playnite", "steam", "uplay", "amazon-games", "humble" };

    /// <summary>The only programs the server starts.</summary>
    public static readonly string[] Programs = { "GalaxyClient.exe", "Launcher.exe", "RiotClientServices.exe" };

    /// <summary>The logos of the detail page buttons (Web\action-logos): Playnite and every store with a direct action.</summary>
    public static readonly string[] LogoKeys = { "playnite", "steam", "ubisoft", "amazon", "humble", "gog", "rockstar", "riot" };

    // The headers a reverse proxy sets for the client's address.
    private static readonly string[] ProxyHeaders = { "X-Forwarded-For", "Forwarded", "X-Real-IP" };

    // Launch arguments of the Riot Client per product, as Playnite's Riot library builds them.
    private static readonly Dictionary<string, string[]> RiotArguments = new(StringComparer.Ordinal)
    {
        ["rg-leagueoflegends"] = new[] { "--launch-product=league_of_legends", "--launch-patchline=live" },
        ["rg-valorant"] = new[] { "--launch-product=valorant", "--launch-patchline=live" },
        ["rg-legendsofruneterra"] = new[] { "--launch-product=bacon", "--launch-patchline=live" },
        ["rg-2xko"] = new[] { "--launch-product=lion", "--launch-patchline=live" }
    };

    private static readonly StartActionKind[] Everything =
    {
        StartActionKind.OpenInPlaynite, StartActionKind.StartViaPlaynite, StartActionKind.DirectStart, StartActionKind.InstallViaPlaynite, StartActionKind.InstallDirect
    };

    private static readonly StartActionKind[] Standard = { StartActionKind.OpenInPlaynite, StartActionKind.StartViaPlaynite };

    // The stores whose Direct start is a program the server starts (GOG Galaxy, Rockstar Games Launcher, Riot Client).
    private static readonly string[] ProgramStores = { "gog", "rockstar", "riot" };

    // The stores whose Direct start does not fit every game (Humble single purchases, also their Install direct; Riot products not in RiotArguments).
    private static readonly string[] FallbackStores = { "humble", "riot" };

    /// <summary>
    /// The actions a store can have, without the remote start condition: no Install via Playnite for Epic,
    /// Battle.net and Rockstar, where Playnite only opens the launcher; PlayStation only Open; custom and unknown stores Open and Start via Playnite.
    /// </summary>
    public static StartActionKind[] Capable(string storeKey) => storeKey switch
    {
        "steam" or "ubisoft" or "amazon" or "humble" => Everything,
        "gog" => new[] { StartActionKind.OpenInPlaynite, StartActionKind.StartViaPlaynite, StartActionKind.DirectStart, StartActionKind.InstallViaPlaynite },
        "rockstar" or "riot" => new[] { StartActionKind.OpenInPlaynite, StartActionKind.StartViaPlaynite, StartActionKind.DirectStart },
        "itch" or "indiegala" or "xbox" => new[] { StartActionKind.OpenInPlaynite, StartActionKind.StartViaPlaynite, StartActionKind.InstallViaPlaynite },
        "playstation" => new[] { StartActionKind.OpenInPlaynite },
        _ => Standard
    };

    /// <summary>
    /// The rows of the admin page's start table: every store of GameQuery.StoreNames and custom (no plugin-* stores) in the order of
    /// <paramref name="platformOrder"/> as GameGroups.PlatformRank reads it; <paramref name="launcherFound"/> tells per store key whether its launcher was found.
    /// </summary>
    public static StoreRow[] StoreRows(IEnumerable<GameEntry> games, Func<string, bool> launcherFound, string platformOrder)
    {
        var counts = games.GroupBy(g => g.StoreKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return GameQuery.StoreNames.Values.Append((Key: "custom", Name: "Custom"))
            .Select(s => (Store: s, Rank: GameGroups.PlatformRank(s.Key, s.Name, platformOrder)))
            .OrderBy(s => s.Rank.Position)
            .ThenBy(s => s.Rank.Tier)
            .ThenBy(s => s.Rank.Name, StringComparer.Ordinal)
            .Select(s => new StoreRow(
                s.Store.Key,
                s.Store.Name,
                counts.GetValueOrDefault(s.Store.Key),
                Capable(s.Store.Key),
                ProgramStores.Contains(s.Store.Key) ? launcherFound(s.Store.Key) : null,
                FallbackStores.Contains(s.Store.Key)))
            .ToArray();
    }

    /// <summary>
    /// StartActions parsed: entries "&lt;storeKey&gt;:&lt;Kind&gt;|&lt;Kind&gt;" comma-separated, keys trimmed and lower case, unknown action names
    /// ignored, an entry without exactly one colon or without a key ignored, the first entry of a key counts ("&lt;storeKey&gt;:" = nothing ticked).
    /// </summary>
    public static Dictionary<string, HashSet<StartActionKind>> ParseActions(string? value)
    {
        var result = new Dictionary<string, HashSet<StartActionKind>>(StringComparer.Ordinal);
        foreach (var entry in (value ?? string.Empty).Split(','))
        {
            var parts = entry.Split(':');
            var key = parts[0].Trim().ToLowerInvariant();
            if (parts.Length != 2 || key.Length == 0)
            {
                continue;
            }

            var kinds = parts[1].Split('|')
                .Select(n => Enum.GetNames<StartActionKind>().FirstOrDefault(k => k.Equals(n.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Where(n => n is not null)
                .Select(n => Enum.Parse<StartActionKind>(n!));
            result.TryAdd(key, kinds.ToHashSet());
        }

        return result;
    }

    /// <summary>
    /// The ticked actions of a store: its StartActions entry; Open in Playnite and Start via Playnite for a store without one and always for a
    /// plugin-* store.
    /// </summary>
    public static IReadOnlyCollection<StartActionKind> Ticked(string? startActions, string storeKey) =>
        !storeKey.StartsWith("plugin-", StringComparison.Ordinal) && ParseActions(startActions).TryGetValue(storeKey, out var kinds) ? kinds : Standard;

    /// <summary>
    /// A request from the PC itself: the remote address (an IPv4-mapped address mapped first) is loopback and the request carries none of
    /// X-Forwarded-For, Forwarded or X-Real-IP (case ignored). Jellyfin takes over forwarded addresses only from proxies listed in
    /// KnownProxies (ApiServiceCollectionExtensions), so an unlisted reverse proxy on this PC shows as
    /// loopback; such a header can only make a request not local, never local.
    /// </summary>
    public static bool IsLocal(IPAddress? remote, IEnumerable<string> headerNames) =>
        remote is not null
        && IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote)
        && !headerNames.Any(h => ProxyHeaders.Contains(h, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether the server may start for this user (RemoteStart).</summary>
    public static bool Allowed(RemoteStartPermission remoteStart, bool isAdmin) =>
        remoteStart == RemoteStartPermission.AllUsers || (remoteStart == RemoteStartPermission.AdminsOnly && isAdmin);

    /// <summary>
    /// The buttons of one game version in detail page order: Direct start or Install (direct), then Start or Install via Playnite, Open in
    /// Playnite last. Only actions the store can have and that are ticked count; an installed game offers the start actions, a not installed
    /// one the install actions, Open both. A ticked direct action that does not fit this version (its id, its install folder, or a program on
    /// the PC without the user's permission) shows the via Playnite action in its place, once. On the PC a URI is a link for every user and a
    /// program runs on the server with the permission; elsewhere every action runs on the server with the permission, without it there is no button.
    /// </summary>
    public static GameAction[] Decide(GameEntry game, PluginConfiguration config, Launchers launchers, bool isAdmin, bool isLocal)
    {
        var allowed = Allowed(config.RemoteStart, isAdmin);
        if (!isLocal && !allowed)
        {
            return Array.Empty<GameAction>();
        }

        var guid = Guid.Parse(game.Id).ToString("D");
        var launcher = game.StoreKey switch
        {
            "gog" => launchers.Gog,
            "rockstar" => launchers.Rockstar,
            "riot" => launchers.Riot,
            _ => null
        };
        // Xbox console games have no start or install action; a program store's Direct start needs its launcher
        // and a remote start that is not Off.
        var capable = game.StoreKey == "xbox" && game.StoreGameId.StartsWith("CONSOLE", StringComparison.Ordinal)
            ? new[] { StartActionKind.OpenInPlaynite }
            : Capable(game.StoreKey);
        if (ProgramStores.Contains(game.StoreKey) && (launcher is null || config.RemoteStart == RemoteStartPermission.Off))
        {
            capable = capable.Where(k => k != StartActionKind.DirectStart).ToArray();
        }

        var ticked = Ticked(config.StartActions, game.StoreKey);
        bool On(StartActionKind kind) => capable.Contains(kind) && ticked.Contains(kind);

        var actions = new List<(StartActionKind Kind, StartAction Action, string Logo)>();
        var (direct, playnite) = game.Installed
            ? (StartActionKind.DirectStart, StartActionKind.StartViaPlaynite)
            : (StartActionKind.InstallDirect, StartActionKind.InstallViaPlaynite);
        var fallback = false;
        if (On(direct))
        {
            var action = game.Installed ? DirectStart(game, launcher) : DirectInstall(game);
            if (action is not null && (action.Uri is not null || allowed))
            {
                actions.Add((direct, action, game.StoreKey));
            }
            else
            {
                fallback = true;
            }
        }

        // Playnite's start starts an installed game and installs a not installed one.
        if (fallback || On(playnite))
        {
            actions.Add((playnite, StartAction.ForUri($"playnite://playnite/start/{guid}"), "playnite"));
        }

        if (On(StartActionKind.OpenInPlaynite))
        {
            actions.Add((StartActionKind.OpenInPlaynite, StartAction.ForUri($"playnite://playnite/showgame/{guid}"), "playnite"));
        }

        return actions.Select(a => new GameAction(a.Kind, isLocal && a.Action.Uri is not null ? StartVia.Link : StartVia.Server, a.Action, a.Logo)).ToArray();
    }

    /// <summary>
    /// The button a POST games/{id}/action/{name} asks for, decided anew by <see cref="Decide"/> (nothing of the request but the game and the
    /// name counts): 204 with the action the server runs; 404 no such action name or no such button; 403 the button exists only with the
    /// permission (a remote request without it); 409 the browser opens the link itself.
    /// </summary>
    public static (int Status, GameAction? Action) Pick(GameEntry game, PluginConfiguration config, Launchers launchers, bool isAdmin, bool isLocal, string name)
    {
        if (!Enum.GetNames<StartActionKind>().Contains(name, StringComparer.Ordinal))
        {
            return (404, null);
        }

        var kind = Enum.Parse<StartActionKind>(name);
        var action = Decide(game, config, launchers, isAdmin, isLocal).FirstOrDefault(a => a.Kind == kind);
        if (action is null)
        {
            // On the PC every button of the game exists; elsewhere a button is missing for lack of permission (403) or because the game has none (404).
            var onPc = Decide(game, config, launchers, isAdmin, true).Any(a => a.Kind == kind);
            return (!isLocal && onPc && !Allowed(config.RemoteStart, isAdmin) ? 403 : 404, null);
        }

        return action.Via == StartVia.Link ? (409, null) : (204, action);
    }

    /// <summary>The start task of an action; null for a scheme or program not in the fixed lists.</summary>
    public static ProcessStartInfo? ToStartInfo(StartAction action)
    {
        if (action.Uri is not null)
        {
            var colon = action.Uri.IndexOf(':', StringComparison.Ordinal);
            return colon > 0 && Schemes.Contains(action.Uri[..colon], StringComparer.Ordinal)
                ? new ProcessStartInfo(action.Uri) { UseShellExecute = true }
                : null;
        }

        if (action.Program is null || !Path.IsPathFullyQualified(action.Program) || !Programs.Contains(Path.GetFileName(action.Program), StringComparer.Ordinal))
        {
            return null;
        }

        var info = new ProcessStartInfo(action.Program) { UseShellExecute = false };
        foreach (var argument in action.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    /// <summary>The store's own start (Direct start); null when the id or the install folder does not fit.</summary>
    private static StartAction? DirectStart(GameEntry game, string? launcher)
    {
        var id = game.StoreGameId;
        switch (game.StoreKey)
        {
            case "steam":
                // as Playnite's Steam library
                return IsSteamAppId(id) ? StartAction.ForUri("steam://rungameid/" + id) : null;
            case "ubisoft":
                // as Playnite's Ubisoft library
                return Regex.IsMatch(id, @"\A[0-9]+\z") ? StartAction.ForUri("uplay://launch/" + id) : null;
            case "amazon":
                // as Playnite's Amazon library
                return Regex.IsMatch(id, @"\A[A-Za-z0-9._-]+\z") ? StartAction.ForUri("amazon-games://play/" + id) : null;
            case "humble":
                // as Playnite's Humble library; only games of the Humble app
                return IsHumbleAppId(id) ? StartAction.ForUri("humble://launch/" + id) : null;
            case "gog":
                // as Playnite's GOG library
                return launcher is not null && Regex.IsMatch(id, @"\A[0-9]+\z") && IsInstallDirectory(game.InstallDirectory)
                    ? StartAction.ForProgram(launcher, "/launchViaAutostart", "/gameId=" + id, "/command=runGame", "/path=" + game.InstallDirectory)
                    : null;
            case "rockstar":
                // as Playnite's Rockstar library
                return launcher is not null && IsInstallDirectory(game.InstallDirectory)
                    ? StartAction.ForProgram(launcher, "-launchTitleInFolder", game.InstallDirectory!)
                    : null;
            case "riot":
                return launcher is not null && RiotArguments.TryGetValue(id, out var arguments) ? StartAction.ForProgram(launcher, arguments) : null;
            default:
                return null;
        }
    }

    /// <summary>The store's own install URI (Install direct); null for every other store or an id that does not fit.</summary>
    private static StartAction? DirectInstall(GameEntry game)
    {
        var id = game.StoreGameId;
        return game.StoreKey switch
        {
            // as Playnite's Steam library
            "steam" when IsSteamAppId(id) => StartAction.ForUri("steam://install/" + id),
            // as Playnite's Ubisoft library
            "ubisoft" when Regex.IsMatch(id, @"\A[0-9]+\z") => StartAction.ForUri("uplay://install/" + id),
            // as Playnite's Amazon library
            "amazon" when Regex.IsMatch(id, @"\A[A-Za-z0-9._-]+\z") => StartAction.ForUri("amazon-games://install/" + id),
            // as Playnite's Humble library
            "humble" when IsHumbleAppId(id) => StartAction.ForUri("humble://download/" + id),
            _ => null
        };
    }

    // Digits only and a uint (anything else is a mod or a shortcut).
    private static bool IsSteamAppId(string id) =>
        Regex.IsMatch(id, @"\A[0-9]+\z") && uint.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsHumbleAppId(string id) =>
        Regex.IsMatch(id, @"\A[A-Za-z0-9_-]+\z") && (id.EndsWith("_trove", StringComparison.Ordinal) || id.EndsWith("_collection", StringComparison.Ordinal));

    private static bool IsInstallDirectory(string? directory) =>
        !string.IsNullOrWhiteSpace(directory) && !directory.Contains('"', StringComparison.Ordinal) && !directory.Contains('{', StringComparison.Ordinal);
}
