using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The characterart images of a game for GamesController's characterart endpoints:
/// characterart.png is image 1, then characterart1.png, characterart2.png ... in number order, in the game's own folder (each version has its own); when it has none, the same
/// names in its sub folder characterart\. Only .png, case ignored; empty files, hidden files, junctions and symbolic links (files and the
/// sub folder) are left out. Reads only, writes nothing.
/// </summary>
public static class CharacterArtFiles
{
    public const string Prefix = "characterart";

    /// <summary>The base name the settings choose for CharacterArt (CharacterartFileName, or its own name); <see cref="Prefix"/> when the own name is empty or not a file name.</summary>
    public static string Name(PluginConfiguration config) => ImageCache.ConfiguredName(config.CharacterartFileName, config.CharacterartOwnName, Prefix);

    private static readonly EnumerationOptions FileOptions = new() { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden };

    /// <summary>True while a game page can show characterart: CharacterArt Show is on, or the LogoArt chain reaches the Characterart stage.</summary>
    public static bool Wanted(PluginConfiguration config) => config.ArtCharacterArtShow || LogoArtChainHasCharacterart(config);

    /// <summary>The LogoArt chain as ArtworkPlus reads it (LogoArtSettings.Chain): source, then the fallbacks up to the first None; Hide ends it.</summary>
    public static bool LogoArtChainHasCharacterart(PluginConfiguration config)
    {
        if (config.ArtLogoArtSource != ArtLogoArtSourceKind.VanillaLogo && config.ArtLogoArtSource != ArtLogoArtSourceKind.Clearart)
        {
            return config.ArtLogoArtSource == ArtLogoArtSourceKind.Characterart;
        }

        return config.ArtLogoArtFallback == ArtLogoArtFallbackKind.Characterart
            || (config.ArtLogoArtFallback != ArtLogoArtFallbackKind.None && config.ArtLogoArtSecondFallback == ArtLogoArtFallbackKind.Characterart);
    }

    /// <summary>
    /// The images of the game <paramref name="id"/> for the list endpoint: null (204) unless <paramref name="mayOpen"/> (the user may see the games
    /// library), the id is a game of <paramref name="data"/>, <paramref name="allowed"/> (parental controls, null = off) lets it through and
    /// <paramref name="gameFolder"/> gives its own game folder; then empty unless <paramref name="wanted"/>, else <see cref="List"/>.
    /// </summary>
    public static IReadOnlyList<string>? Visible(GameData data, Guid id, bool mayOpen, Func<GameEntry, bool>? allowed, Func<GameEntry, string?> gameFolder, bool wanted, string prefix = Prefix)
    {
        if (!mayOpen || ArtworkPlusLinkCore.Game(data, id) is not GameHit hit || (allowed is not null && !allowed(hit.Game!)) || gameFolder(hit.Game!) is not string folder)
        {
            return null;
        }

        return wanted ? List(folder, prefix) : Array.Empty<string>();
    }

    /// <summary>
    /// The file of image <paramref name="index"/> of the game <paramref name="id"/> for the file endpoint: only an entry of the list computed here
    /// (no file name from the client), and only while its full path lies inside the game folder and is no link; null otherwise (404).
    /// </summary>
    public static string? Served(GameData data, Guid id, int index, Func<GameEntry, string?> gameFolder, bool wanted, string prefix = Prefix)
    {
        if (!wanted || ArtworkPlusLinkCore.Game(data, id) is not GameHit hit || gameFolder(hit.Game!) is not string folder)
        {
            return null;
        }

        var files = List(folder, prefix);
        if (index < 0 || index >= files.Count)
        {
            return null;
        }

        var path = Path.GetFullPath(files[index]);
        var root = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0
            ? path
            : null;
    }

    /// <summary>The ordered full paths of the characterart images in <paramref name="gameFolder"/> (see the class); empty without any or for a link.</summary>
    public static IReadOnlyList<string> List(string? gameFolder, string prefix = Prefix)
    {
        if (gameFolder is null || !IsPlainFolder(gameFolder))
        {
            return Array.Empty<string>();
        }

        var own = Numbered(gameFolder, prefix);
        if (own.Count > 0)
        {
            return own;
        }

        var sub = Path.Combine(gameFolder, prefix);
        return IsPlainFolder(sub) ? Numbered(sub, prefix) : Array.Empty<string>();
    }

    /// <summary>0 for "characterart.png", n for "characterart&lt;n&gt;.png" (n from 1, no leading zero), null for any other name; case ignored.</summary>
    public static int? Number(string name, string prefix = Prefix)
    {
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.Length < prefix.Length + 4)
        {
            return null;
        }

        var digits = name[prefix.Length..^4];
        if (digits.Length == 0)
        {
            return 0;
        }

        return digits[0] != '0' && digits.All(char.IsAsciiDigit) && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private static List<string> Numbered(string folder, string prefix) =>
        new DirectoryInfo(folder).EnumerateFiles("*", FileOptions)
            .Select(file => (File: file, Number: Number(file.Name, prefix)))
            .Where(f => f.Number is not null && f.File.Length > 0)
            .OrderBy(f => f.Number)
            .Select(f => f.File.FullName)
            .ToList();

    private static bool IsPlainFolder(string path) => Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
}
