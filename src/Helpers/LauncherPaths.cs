using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Finds the launcher programs of GOG, Rockstar and Riot as Playnite does, strictly read-only and without a setting.
/// Read anew for every decision that concerns one of these stores; any read error counts as not found.
/// </summary>
public static class LauncherPaths
{
    /// <summary>The launcher of the game's store; the other two stay null (and are not read).</summary>
    public static Launchers Find(string storeKey, string playniteFolder) => storeKey switch
    {
        "gog" => new Launchers(Found(Gog, "GalaxyClient.exe"), null, null),
        "rockstar" => new Launchers(null, Found(Rockstar, "Launcher.exe"), null),
        "riot" => new Launchers(null, null, Found(() => Riot(playniteFolder), "RiotClientServices.exe")),
        _ => Launchers.None
    };

    /// <summary>An absolute drive-letter path (checked before anything there is opened).</summary>
    public static bool IsAllowedPath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    private static string? Found(Func<string?> folder, string program)
    {
        try
        {
            var dir = folder();
            if (string.IsNullOrWhiteSpace(dir))
            {
                return null;
            }

            var path = Path.Combine(dir, program);
            if (!IsAllowedPath(path))
            {
                return null;
            }

            path = Path.GetFullPath(path);
            return IsAllowedPath(path) && File.Exists(path) ? path : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // As Playnite's GOG library: HKLM GalaxyClient\paths, value "client" (WOW6432Node first).
    private static string? Gog()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths")
            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\GalaxyClient\paths");
        return key?.GetValue("client") as string;
    }

    // As Playnite's Rockstar library and its uninstall-entry lookup: the first uninstall entry named
    // "Rockstar Games Launcher" (64-bit view, then 32-bit; HKLM, then HKCU), InstallLocation without quotes.
    private static string? Rockstar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
        foreach (var view in Environment.Is64BitOperatingSystem ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var list = root.OpenSubKey(Uninstall);
                foreach (var name in list?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var program = list!.OpenSubKey(name);
                    if (program?.GetValue("DisplayName") as string == "Rockstar Games Launcher")
                    {
                        return (program.GetValue("InstallLocation") as string)?.Trim('"');
                    }
                }
            }
        }

        return null;
    }

    // As Playnite's Riot library: setting RiotClientPath in Playnite's ExtensionsData\<plugin id>\config.json,
    // program RiotClientServices.exe. Read only.
    private static string? Riot(string playniteFolder)
    {
        var file = Path.Combine(playniteFolder, "ExtensionsData", "91d13c6f-63d3-42ed-a100-6f811a8387ea", "config.json");
        if (!IsAllowedPath(file) || !File.Exists(file))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        return doc.RootElement.TryGetProperty("RiotClientPath", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
