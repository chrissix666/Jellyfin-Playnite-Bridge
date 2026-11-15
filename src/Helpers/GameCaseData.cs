using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The data layer of the 3D game case for ArtworkPlus: which shape a game version
/// gets (box or disc case), its platform, and the rows of <c>ArtworkPlusLink.GameCase</c>. Pure: it reads only the files it is given (the picture headers and the
/// one-word file <c>casetype.txt</c>), writes nothing, and the picture paths come from the plugin's own index, never from a request.
/// </summary>
public static class GameCaseData
{
    /// <summary>A spine whose width divided by its height is at least this is a box (the measured groups lie at 0.0525-0.0974 and 0.1163-0.1954).</summary>
    public const double SpineBoxThreshold = 0.1068;

    /// <summary>The depth of a box without a spine picture (the measured median of the box spines).</summary>
    public const string BoxDepthDefault = "0.164";

    /// <summary>The largest depth of a box (the measured boxes reach 0.1954); a wider "spine" picture is no spine of a box.</summary>
    public const double BoxDepthMax = 0.25;

    /// <summary>The file name of the owner's decision per version (one word: box or disc).</summary>
    public const string OverrideFile = "casetype.txt";

    /// <summary>The ArtworkPlus settings that decide the shape of a PC game without a spine picture.</summary>
    public sealed record Settings(string NoSpineForm, int NoSpineYear)
    {
        public static readonly Settings Default = new("Year", 2005);
    }

    // Playnite's platform names (platforms.db) of the consoles the case knows; every other name counts as PC.
    private static readonly Dictionary<string, string> Consoles = new(StringComparer.Ordinal)
    {
        ["Sony PlayStation 3"] = "ps3",
        ["Sony PlayStation 4"] = "ps4",
        ["Sony PlayStation 5"] = "ps5",
        ["Microsoft Xbox 360"] = "xbox360",
        ["Microsoft Xbox One"] = "xboxone",
        ["Microsoft Xbox Series"] = "xboxseries"
    };

    /// <summary>
    /// ps3, ps4, ps5, xbox360, xboxone, xboxseries or pc: "PC (Windows)" in the list gives pc; otherwise the first console of the list; an unknown name or an empty list gives pc.
    /// </summary>
    public static string Platform(IEnumerable<string>? platforms)
    {
        var list = (platforms ?? Array.Empty<string>()).ToArray();
        if (list.Contains("PC (Windows)", StringComparer.Ordinal))
        {
            return "pc";
        }

        foreach (var name in list)
        {
            if (Consoles.TryGetValue(name, out var console))
            {
                return console;
            }
        }

        return "pc";
    }

    // The sizes read from the headers, per path while length and write time stay the same (a failed read is kept too: width 0).
    private static readonly ConcurrentDictionary<string, (long Length, long Ticks, int Width, int Height)> Sizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The width and height of a JPEG or PNG file from its header (no decoding, cached while the file is unchanged); false for anything else or an unreadable file.</summary>
    public static bool TryImageSize(string? path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            var info = string.IsNullOrEmpty(path) ? null : new FileInfo(path);
            if (info is null || !info.Exists)
            {
                return false;
            }

            var ticks = info.LastWriteTimeUtc.Ticks;
            if (!Sizes.TryGetValue(info.FullName, out var known) || known.Length != info.Length || known.Ticks != ticks)
            {
                ReadImageSize(info.FullName, out var w, out var h);
                known = (info.Length, ticks, w, h);
                Sizes[info.FullName] = known;
            }

            width = known.Width;
            height = known.Height;
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            width = height = 0;
            return false;
        }
    }

    /// <summary>The EXIF orientation (1 to 8) of an APP1 segment ("Exif", two zero bytes, then the TIFF block); null when it has none or cannot be read.</summary>
    public static int? ExifOrientation(byte[] segment)
    {
        try
        {
            if (segment.Length < 20 || segment[0] != (byte)'E' || segment[1] != (byte)'x' || segment[2] != (byte)'i' || segment[3] != (byte)'f' || segment[4] != 0 || segment[5] != 0)
            {
                return null;
            }

            var tiff = 6;
            var little = segment[tiff] == (byte)'I' && segment[tiff + 1] == (byte)'I';
            if (!little && !(segment[tiff] == (byte)'M' && segment[tiff + 1] == (byte)'M'))
            {
                return null;
            }

            int U16(int at) => little ? segment[at] | (segment[at + 1] << 8) : (segment[at] << 8) | segment[at + 1];
            long U32(int at) => little
                ? segment[at] | ((long)segment[at + 1] << 8) | ((long)segment[at + 2] << 16) | ((long)segment[at + 3] << 24)
                : ((long)segment[at] << 24) | ((long)segment[at + 1] << 16) | ((long)segment[at + 2] << 8) | segment[at + 3];
            var ifd = tiff + (int)U32(tiff + 4);
            var count = U16(ifd);
            for (var i = 0; i < count && ifd + 2 + ((i + 1) * 12) <= segment.Length; i++)
            {
                var entry = ifd + 2 + (i * 12);
                if (U16(entry) == 0x0112)
                {
                    var value = U16(entry + 8);
                    return value is >= 1 and <= 8 ? value : null;
                }
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private static bool ReadImageSize(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[24];
            var read = stream.Read(head, 0, head.Length);
            if (read >= 24 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            {
                width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                return width > 0 && height > 0;
            }

            if (read < 4 || head[0] != 0xFF || head[1] != 0xD8)
            {
                return false;
            }

            stream.Position = 2;
            var buffer = new byte[4];
            var orientation = 1;
            while (stream.Position < stream.Length)
            {
                int b;
                do
                {
                    b = stream.ReadByte();
                }
                while (b != 0xFF && b >= 0);

                int marker;
                do
                {
                    marker = stream.ReadByte();
                }
                while (marker == 0xFF);
                if (b < 0 || marker < 0)
                {
                    return false;
                }

                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9) || marker == 0x00)
                {
                    continue;                       // markers without a length
                }

                if (stream.Read(buffer, 0, 2) != 2)
                {
                    return false;
                }

                var length = (buffer[0] << 8) | buffer[1];
                if (length < 2)
                {
                    return false;
                }

                if (marker == 0xE1 && length >= 20)
                {
                    // EXIF: the orientation of the picture (5 to 8 = turned by a quarter, width and height change places); IFD0 sits at the start of the
                    // segment, so the first 4 KB are enough (a camera's thumbnail makes the segment much larger)
                    var take = Math.Min(length - 2, 4096);
                    var exif = new byte[take];
                    if (stream.Read(exif, 0, take) != take)
                    {
                        return false;
                    }

                    stream.Position += length - 2 - take;
                    orientation = ExifOrientation(exif) ?? orientation;
                    continue;
                }

                if (marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC)
                {
                    var sof = new byte[5];
                    if (stream.Read(sof, 0, 5) != 5)
                    {
                        return false;
                    }

                    height = (sof[1] << 8) | sof[2];
                    width = (sof[3] << 8) | sof[4];
                    if (orientation >= 5 && orientation <= 8)
                    {
                        (width, height) = (height, width);
                    }

                    return width > 0 && height > 0;
                }

                stream.Position += length - 2;
            }
        }
        catch (Exception)
        {
        }

        width = height = 0;
        return false;
    }

    /// <summary>"box" or "disc" from the one-word file casetype.txt of the version's game folder (UTF-8 with or without BOM, trimmed, lower case only, at most 16 bytes); null for anything else.</summary>
    public static string? ReadOverride(string? gameFolder)
    {
        try
        {
            var path = string.IsNullOrEmpty(gameFolder) ? null : Path.Combine(gameFolder, OverrideFile);
            if (path is null || !File.Exists(path) || new FileInfo(path).Length > 16)
            {
                return null;
            }

            var text = File.ReadAllText(path, new System.Text.UTF8Encoding(false)).Trim('﻿', ' ', '\t', '\r', '\n');
            return text is "box" or "disc" ? text : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsPicture(string? path) =>
        !string.IsNullOrEmpty(path) && Path.GetExtension(path).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";

    /// <summary>
    /// The answer of ArtworkPlusLink.GameCase for one version, rows [part, value, tag]: kind (box | disc), depth (box only), source (spine | rule | user), platform,
    /// front, back, spine, disc, logo (path and tag, empty when missing), name, publisher (one per publisher), developer (one per developer). Empty array when the
    /// front or the back is missing, no JPEG/PNG or unreadable. The shape: casetype.txt first; a console platform is always a disc case; a spine picture decides by its
    /// ratio (box from <see cref="SpineBoxThreshold"/>); otherwise the ArtworkPlus settings (all disc, all box, or up to the year).
    /// </summary>
    public static string[][] Build(
        string name, IEnumerable<string>? platforms, int? year, IEnumerable<string>? publishers, IEnumerable<string>? developers,
        string? gameFolder, string? front, string? back, string? spine, string? disc, string? logo, Settings settings, Func<string?, string?> tag)
    {
        if (!IsPicture(front) || !IsPicture(back) || !TryImageSize(front, out _, out _) || !TryImageSize(back, out _, out _))
        {
            return Array.Empty<string[]>();
        }

        var platform = Platform(platforms);
        string kind;
        string source;
        string? depth = null;

        var sw = 0;
        var sh = 0;
        var spineOk = IsPicture(spine) && TryImageSize(spine, out sw, out sh) && sh > 0;
        var ratio = spineOk ? (double)sw / sh : 0;

        var forced = ReadOverride(gameFolder);
        if (forced is not null)
        {
            kind = forced;
            source = "user";
        }
        else if (platform != "pc")
        {
            kind = "disc";
            source = "rule";
        }
        else if (spineOk)
        {
            kind = ratio >= SpineBoxThreshold ? "box" : "disc";
            source = "spine";
        }
        else
        {
            kind = settings.NoSpineForm switch
            {
                "Disc" => "disc",
                "Box" => "box",
                _ => year is int y && y <= settings.NoSpineYear ? "box" : "disc"
            };
            source = "rule";
        }

        if (kind == "box")
        {
            depth = spineOk && ratio >= SpineBoxThreshold ? Math.Min(ratio, BoxDepthMax).ToString("0.000", CultureInfo.InvariantCulture) : BoxDepthDefault;
        }

        var rows = new List<string[]> { new[] { "kind", kind, "" } };
        if (depth is not null)
        {
            rows.Add(new[] { "depth", depth, "" });
        }

        rows.Add(new[] { "source", source, "" });
        rows.Add(new[] { "platform", platform, "" });
        rows.Add(new[] { "front", front!, tag(front) ?? "" });
        rows.Add(new[] { "back", back!, tag(back) ?? "" });
        rows.Add(spineOk ? new[] { "spine", spine!, tag(spine) ?? "" } : new[] { "spine", "", "" });
        var discOk = IsPicture(disc);
        rows.Add(discOk ? new[] { "disc", disc!, tag(disc) ?? "" } : new[] { "disc", "", "" });
        rows.Add(!string.IsNullOrEmpty(logo) ? new[] { "logo", logo, tag(logo) ?? "" } : new[] { "logo", "", "" });
        rows.Add(new[] { "name", name ?? "", "" });
        foreach (var publisher in publishers ?? Array.Empty<string>())
        {
            rows.Add(new[] { "publisher", publisher, "" });
        }

        foreach (var developer in developers ?? Array.Empty<string>())
        {
            rows.Add(new[] { "developer", developer, "" });
        }

        return rows.ToArray();
    }

    /// <summary>The two ArtworkPlus settings from its saved configuration file (CaseModThreejsGamesNoSpineForm, CaseModThreejsGamesNoSpineYear); the defaults Year / 2005 when the file, an element or a value is missing or invalid.</summary>
    public static Settings ReadSettings(string? artworkPlusConfigurationFile)
    {
        try
        {
            if (string.IsNullOrEmpty(artworkPlusConfigurationFile) || !File.Exists(artworkPlusConfigurationFile))
            {
                return Settings.Default;
            }

            var root = XDocument.Load(artworkPlusConfigurationFile).Root;
            var form = root?.Element("CaseModThreejsGamesNoSpineForm")?.Value.Trim();
            var yearText = root?.Element("CaseModThreejsGamesNoSpineYear")?.Value.Trim();
            var okForm = form is "Disc" or "Box" or "Year" ? form : Settings.Default.NoSpineForm;
            var okYear = int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y is >= 1970 and <= 2100 ? y : Settings.Default.NoSpineYear;
            return new Settings(okForm, okYear);
        }
        catch (Exception)
        {
            return Settings.Default;
        }
    }

    private static readonly object CacheLock = new();
    private static string? _cachedFile;
    private static DateTime _cachedWrite;
    private static Settings _cached = Settings.Default;

    /// <summary>
    /// <see cref="ReadSettings"/> of ArtworkPlus's configuration file that sits beside this plugin's own configuration file, read again only when the file's write time changes.
    /// </summary>
    public static Settings CurrentSettings(string? ownConfigurationFile)
    {
        try
        {
            var folder = string.IsNullOrEmpty(ownConfigurationFile) ? null : Path.GetDirectoryName(ownConfigurationFile);
            var file = folder is null ? null : Path.Combine(folder, "Jellyfin.Plugin.ArtworkPlus.xml");
            var write = file is not null && File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
            lock (CacheLock)
            {
                if (_cachedFile != file || _cachedWrite != write)
                {
                    _cached = ReadSettings(file);
                    _cachedFile = file;
                    _cachedWrite = write;
                }

                return _cached;
            }
        }
        catch (Exception)
        {
            return Settings.Default;
        }
    }
}
