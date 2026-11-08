using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Reflection;
using Emby.Naming.Common;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The pure helpers the three picture areas (<see cref="Screenshots"/>, <see cref="Wallpapers"/>, <see cref="Documents"/>) share: folder names, natural name order, the file name
/// hash and the image tag, the pseudo random order, the bytes of an item or row id and their decoding, and the directory checks. No state, no settings: what an area is
/// (its slots, rows, markers and checks) stays in the area.
/// </summary>
public static class AreaCommon
{
    // Junctions, symbolic links and hidden entries are skipped (as the extras scan does).
    public static readonly EnumerationOptions ScanOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false
    };

    // Windows device names (CON, NUL, COM1 ..., also with an extension) are no folder names.
    private static readonly System.Text.RegularExpressions.Regex DeviceName = new(@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>True for a Windows device name (CON, NUL, COM1 ...), also with an extension.</summary>
    internal static bool IsDeviceName(string name) => DeviceName.IsMatch(name);

    /// <summary>
    /// Why a path (cache or extras folder) has a part no folder may have, or null: a Windows device name (CON, NUL, COM1 ...), a part that ends with a dot or a space
    /// (Windows drops it), a stream marker (a colon after the drive), or a formatting character such as the right-to-left override. Parts "." and ".." are left to the path checks.
    /// </summary>
    public static string? PathPartError(string path)
    {
        var parts = path.Split('\\', '/');
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0 || part == "." || part == ".." || (i == 0 && part.Length == 2 && part[1] == ':'))
            {
                continue;
            }

            if (DeviceName.IsMatch(part) || part.EndsWith('.') || part.EndsWith(' ') || part.Contains(':') || part.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
            {
                return "The folder must not have a part that is a device name, ends with a dot or a space, has a colon or a hidden formatting character.";
            }
        }

        return null;
    }

    // Names an area folder must not have: Jellyfin's extras folder names (Emby.Naming) and the folder the image lookup reads as backdrops.
    private static readonly HashSet<string> ReservedAreaNames = new NamingOptions().AllExtrasTypesFolderNames.Keys
        .Append("extrafanart").ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Why <paramref name="name"/> is no usable folder name, or null: not empty, at most 100 characters, no character Windows does not allow (so no path, no way
    /// upwards), no trailing dot or space, not "." or "..". An <paramref name="area"/> name must not be "extrafanart" or one of Jellyfin's extras folder names either.
    /// </summary>
    public static string? FolderNameError(string? name, bool area)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
        {
            return "The folder name is empty or has spaces at its ends.";
        }

        if (name.Length > 100 || name.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '\\', '/' }) >= 0 || name.EndsWith('.') || name == "." || name == ".." || DeviceName.IsMatch(name))
        {
            return "The folder name is no plain folder name.";
        }

        if (area && (ReservedAreaNames.Contains(name) || name.EndsWith(".trickplay", StringComparison.OrdinalIgnoreCase)))
        {
            return "Jellyfin reads a folder of this name for its own purposes.";
        }

        return null;
    }

    /// <summary>
    /// True when another picture or audio area that is on uses the same folder name (case ignored): none of the two is read then (the page says so under both folders), except the soundtrack: it gives way and reads its default folder
    /// (the note stands under the soundtrack only), so one folder never shows the same files in two areas. <paramref name="area"/> is the asking area: screenshots, wallpapers, documents or soundtrack.
    /// </summary>
    public static bool Clashes(Configuration.PluginConfiguration config, string area, string folder)
    {
        var others = new List<(string Area, bool On, string Folder)>
        {
            ("screenshots", config.EnableScreenshots, (config.ScreenshotsFolder ?? string.Empty).Trim()),
            ("wallpapers", config.EnableWallpapers, (config.WallpapersFolder ?? string.Empty).Trim()),
            ("documents", config.EnableDocuments, (config.DocumentsFolder ?? string.Empty).Trim())
        };
        if (area != "soundtrack")
        {
            // the folder the soundtrack really reads (its setting, or the default when that is empty, refused or another area's), so an area never lands on it
            others.Add(("soundtrack", config.EnableSoundtrack, Soundtrack.FolderOf(config)));
        }

        return others.Any(o => o.Area != area && o.On && string.Equals(o.Folder, folder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True for an existing folder that is no junction or symbolic link; false for a missing or unreadable one.</summary>
    public static bool IsPlainDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>File names in natural order: digit runs compare by value, other characters case ignored (so "shot2" comes before "shot10").</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsDigit(b[j]))
                {
                    j++;
                }

                var digitsA = a.AsSpan(startA, i - startA).TrimStart('0');
                var digitsB = b.AsSpan(startB, j - startB).TrimStart('0');
                if (digitsA.Length != digitsB.Length)
                {
                    return digitsA.Length.CompareTo(digitsB.Length);
                }

                var digits = digitsA.CompareTo(digitsB, StringComparison.Ordinal);
                if (digits != 0)
                {
                    return digits;
                }

                continue;
            }

            var order = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (order != 0)
            {
                return order;
            }

            i++;
            j++;
        }

        var rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }

    /// <summary>The 56 bits of a file name's SHA-256 (lower case name) that go into an item id.</summary>
    public static ulong NameHash(string fileName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fileName.ToLowerInvariant()));
        ulong value = 0;
        for (var i = 0; i < 7; i++)
        {
            value = (value << 8) | hash[i];
        }

        return value;
    }

    /// <summary>The image tag of a file: from its last write time and length.</summary>
    public static string Tag(DateTime modified, long length)
    {
        var bytes = BitConverter.GetBytes(modified.Ticks).Concat(BitConverter.GetBytes(length)).ToArray();
        return Convert.ToHexString(SHA256.HashData(bytes), 0, 6).ToLowerInvariant();
    }

    /// <summary>splitmix64 over the seed and the file: a fixed pseudo random key per file and seed, independent of the order the files were listed in.</summary>
    public static ulong Mix(int seed, int slot, ulong hash)
    {
        var z = hash + (ulong)seed * 0x9E3779B97F4A7C15UL + (ulong)slot * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>
    /// The check bytes of a row id: a hash of its first 8 bytes. A row id carries no file hash, so these 7 bytes make it self-verifying: a foreign id
    /// (a real folder of Jellyfin whose last byte happens to be the row marker) passes this check only by a chance of 2^-56.
    /// </summary>
    public static ulong RowCheck(ReadOnlySpan<byte> head)
    {
        Span<byte> input = stackalloc byte[8 + 6];
        "pb-row"u8.CopyTo(input);
        head[..8].CopyTo(input[6..]);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        ulong value = 0;
        for (var i = 0; i < 7; i++)
        {
            value = (value << 8) | hash[i];
        }

        return value;
    }

    /// <summary>
    /// The id of an item or a row: short id of the version (4 bytes), slot (1), seed (3), file name hash (7), marker (1). A row id (<paramref name="rowMarker"/> as the marker) carries the
    /// <see cref="RowCheck"/> bytes instead of a hash. Not a name based id: Jellyfin accepts any GUID, and the fields are what the server needs to find the file again.
    /// </summary>
    public static Guid BuildId(string shortId, int slot, int seed, ulong hash, byte marker, byte? rowMarker)
    {
        var bytes = new byte[16];
        Convert.FromHexString(shortId).CopyTo(bytes, 0);
        bytes[4] = (byte)slot;
        bytes[5] = (byte)(seed >> 16);
        bytes[6] = (byte)(seed >> 8);
        bytes[7] = (byte)seed;
        for (var i = 0; i < 7; i++)
        {
            bytes[8 + i] = (byte)(hash >> (8 * (6 - i)));
        }

        if (rowMarker is not null && marker == rowMarker)
        {
            var check = RowCheck(bytes);
            for (var i = 0; i < 7; i++)
            {
                bytes[8 + i] = (byte)(check >> (8 * (6 - i)));
            }
        }

        bytes[15] = marker;
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>What an id says; null when it ends in neither marker (cheap: no disk, no lookup). A row id is only ours when its check bytes fit.</summary>
    public static (string ShortId, int Slot, int Seed, ulong Hash, bool IsRow)? Decode(Guid id, byte itemMarker, byte? rowMarker)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        var isRow = rowMarker is not null && bytes[15] == rowMarker;
        if (bytes[15] != itemMarker && !isRow)
        {
            return null;
        }

        ulong hash = 0;
        for (var i = 0; i < 7; i++)
        {
            hash = (hash << 8) | bytes[8 + i];
        }

        // A row id is only ours when its check bytes fit; any other id ending in the marker belongs to Jellyfin.
        if (isRow)
        {
            if (hash != RowCheck(bytes))
            {
                return null;
            }

            hash = 0;
        }

        return (Convert.ToHexString(bytes[..4]).ToLowerInvariant(), bytes[4], (bytes[5] << 16) | (bytes[6] << 8) | bytes[7], hash, isRow);
    }

    /// <summary>
    /// The shape of the tiles of a row: "landscape", "square" or "portrait". Landscape, Portrait and Square are fixed and <paramref name="ratios"/> is not called (no cost). Auto is Jellyfin Web's own rule
    /// (10.10.7 cardBuilder setCardData over imageLoader.getPrimaryImageAspectRatio): the median of the ratios (odd count: the middle one, even: the mean of the two middle ones), snapped to 2:3 within 0.15,
    /// to 16:9 within 0.2, to 1 within 0.15, to 4:3 within 0.15; then 3 and wider is Jellyfin's banner, which the plugin's rows do not have, so it is landscape, 1.33 and wider landscape,
    /// above 0.8 square, else portrait; no ratio at all gives landscape.
    /// </summary>
    public static string ShapeOf(TileShape setting, Func<IReadOnlyList<double>> ratios)
    {
        if (setting != TileShape.Auto)
        {
            return setting == TileShape.Portrait ? "portrait" : setting == TileShape.Square ? "square" : "landscape";
        }

        var values = ratios().Where(r => r > 0 && !double.IsNaN(r) && !double.IsInfinity(r)).OrderBy(r => r).ToList();
        if (values.Count == 0)
        {
            return "landscape";
        }

        var half = values.Count / 2;
        var median = values.Count % 2 == 1 ? values[half] : (values[half - 1] + values[half]) / 2.0;
        if (Math.Abs(2.0 / 3.0 - median) <= 0.15)
        {
            median = 2.0 / 3.0;
        }
        else if (Math.Abs(16.0 / 9.0 - median) <= 0.2)
        {
            median = 16.0 / 9.0;
        }
        else if (Math.Abs(1.0 - median) <= 0.15)
        {
            median = 1.0;
        }
        else if (Math.Abs(4.0 / 3.0 - median) <= 0.15)
        {
            median = 4.0 / 3.0;
        }

        return median >= 1.33 ? "landscape" : median > 0.8 ? "square" : "portrait";
    }

    /// <summary>One row of an area as <see cref="Layout"/> plans it: Row = the row id (the slot index, <see cref="MergedRow"/> for the merged row, 0 for Flat) and the slots whose files it holds.</summary>
    public sealed record LayoutRow(int Row, IReadOnlyList<int> Slots);

    /// <summary>The row id of the merged row (the slot byte of its ParentId); the same in all three areas.</summary>
    public const int MergedRow = 255;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, PropertyInfo> MergeProperties = new();

    /// <summary>
    /// The Merge tick of slot <paramref name="slot"/> (1 to 10) of the area whose slot properties start with <paramref name="prefix"/> (ScreenshotSlot, WallpaperSlot, DocumentSlot), read from the
    /// settings each time (never kept in a cached listing, so a toggle shows at once). The implicit slot of Flat (0) is always in.
    /// </summary>
    public static bool MergeOf(PluginConfiguration config, string prefix, int slot) =>
        slot < 1 || slot > 10 || (bool)MergeProperties.GetOrAdd(prefix + slot + "Merge", n => typeof(PluginConfiguration).GetProperty(n)!).GetValue(config)!;

    /// <summary>
    /// The rows of an area for the slots that have files (slot order): Flat is one row of its implicit slot; One Row per Slot one row per slot; Merged puts the ticked slots into one row
    /// (<see cref="MergedRow"/>) that stands where the FIRST ticked slot stands, every unticked slot keeps its own row in slot order; Adaptive does the same but only while at least two
    /// ticked slots have files (with one the slot is an own row as without Merge). Pure: no disk, no settings object.
    /// </summary>
    public static IReadOnlyList<LayoutRow> Layout(bool flat, ScreenshotDisplay display, IEnumerable<(int Index, bool Merge)> slotsWithFiles)
    {
        var slots = slotsWithFiles.ToList();
        if (slots.Count == 0)
        {
            return Array.Empty<LayoutRow>();
        }

        if (flat)
        {
            return new[] { new LayoutRow(0, new[] { slots[0].Index }) };
        }

        var ticked = slots.Where(s => s.Merge).Select(s => s.Index).ToList();
        var merge = display == ScreenshotDisplay.Merged ? ticked.Count > 0 : display == ScreenshotDisplay.Adaptive && ticked.Count > 1;
        if (!merge)
        {
            return slots.Select(s => new LayoutRow(s.Index, new[] { s.Index })).ToList();
        }

        var rows = new List<LayoutRow>();
        var added = false;
        foreach (var slot in slots)
        {
            if (!slot.Merge)
            {
                rows.Add(new LayoutRow(slot.Index, new[] { slot.Index }));
            }
            else if (!added)
            {
                rows.Add(new LayoutRow(MergedRow, ticked));
                added = true;
            }
        }

        return rows;
    }
}
