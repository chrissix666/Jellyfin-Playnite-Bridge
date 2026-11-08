using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Emby.Naming.Common;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One slot of the wallpapers area: its position in the slot list (1 to 10; 0 = the implicit slot of Flat), its sub folder ("" for Flat), the key of a predefined label (null for an own one) and the label in English or as typed.</summary>
public sealed record WallpaperSlot(int Index, string Folder, string? Key, string Label);

/// <summary>One wallpaper file of a slot: Hash = 56 bits of the SHA-256 of its lower case file name (the part of its id that names it), Modified = last write time (UTC).</summary>
public sealed record WallpaperFile(int Slot, string Path, string FileName, DateTime Modified, long Length, ulong Hash);

/// <summary>A row of the area as the page shows it: Row = the slot index, <see cref="Wallpapers.AllRows"/> for the merged row, 0 for Flat.</summary>
public sealed record WallpaperRow(int Row, string? Key, string Label, int Count);

/// <summary>What an id says: the short id of the version, the slot (or row), the seed of the random order, the file name hash (items) and whether it is a row id (the ParentId of a row's items).</summary>
public sealed record WallpaperKey(string ShortId, int Slot, int Seed, ulong Hash, bool IsRow);

/// <summary>The wallpapers of one version as they lie on disk: per slot the files in natural name order.</summary>
public sealed class WallpaperArea
{
    public static readonly WallpaperArea Empty = new(Array.Empty<WallpaperSlot>(), new Dictionary<int, IReadOnlyList<WallpaperFile>>());

    private readonly Dictionary<(int, ulong), WallpaperFile> _byHash = new();

    public WallpaperArea(IReadOnlyList<WallpaperSlot> slots, IReadOnlyDictionary<int, IReadOnlyList<WallpaperFile>> files)
    {
        Slots = slots;
        Files = files;
        foreach (var file in files.Values.SelectMany(f => f))
        {
            _byHash.TryAdd((file.Slot, file.Hash), file);
        }
    }

    public IReadOnlyList<WallpaperSlot> Slots { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<WallpaperFile>> Files { get; }

    public int Total => Files.Values.Sum(f => f.Count);

    public WallpaperFile? Find(int slot, ulong hash) => _byHash.TryGetValue((slot, hash), out var file) ? file : null;
}

/// <summary>
/// The wallpapers area of a game page (a special feature, not artwork): images in &lt;version folder&gt;\&lt;wallpapers folder&gt;\&lt;slot folder&gt;\ of the selected
/// version only (the version folder of the extras root GameExtras.Root chooses). Read only, and only when the area is asked: the listing of a version is kept
/// for 10 seconds. A wallpaper is served as a virtual Photo item whose id carries everything needed to find it again (short id of the version, slot,
/// seed, file name hash), so it resolves after a restart without any stored map; none is in Jellyfin's database.
/// </summary>
public static class Wallpapers
{
    /// <summary>The row id of the merged row (and the slot byte of its ParentId).</summary>
    public const int AllRows = 255;

    /// <summary>The first tiles of a row (Show more adds the same number again).</summary>
    public const int FirstTiles = 60;

    /// <summary>The largest seed (24 bits).</summary>
    public const int MaxSeed = (1 << 24) - 1;

    private const byte ItemMarker = 0xB8;

    private const byte RowMarker = 0xB9;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(10);

    private static readonly string[] SlotProperties = Enumerable.Range(1, 10).SelectMany(i => new[] { "Folder", "Label", "OwnLabel", "On" }.Select(s => "WallpaperSlot" + i + s)).ToArray();

    private static readonly Dictionary<string, PropertyInfo> SlotAccess = SlotProperties.ToDictionary(n => n, n => typeof(PluginConfiguration).GetProperty(n)!);

    private static readonly ConcurrentDictionary<string, (DateTime At, WallpaperArea Area)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Why <paramref name="name"/> is no usable folder name, or null: not empty, at most 100 characters, no character Windows does not allow (so no path, no way
    /// upwards), no trailing dot or space, not "." or "..". An <paramref name="area"/> name must not be "extrafanart" or one of Jellyfin's extras folder names either.
    /// </summary>
    public static string? FolderNameError(string? name, bool area) => AreaCommon.FolderNameError(name, area);

    /// <summary>The configured wallpapers folder name, trimmed (also an unusable one: the extras scan leaves a folder of that name alone in any case).</summary>
    public static string AreaName(PluginConfiguration config) => (config.WallpapersFolder ?? string.Empty).Trim();

    /// <summary>The usable wallpapers folder name, or null while Enable Wallpapers is off or the name is refused.</summary>
    public static string? AreaFolder(PluginConfiguration config) =>
        config.EnableWallpapers && FolderNameError(AreaName(config), true) is null && !AreaCommon.Clashes(config, "wallpapers", AreaName(config)) ? AreaName(config) : null;

    /// <summary>
    /// The slots in use, in slot list order: Flat = the one implicit slot (index 0, no sub folder, row "Wallpapers"); else every slot that is on with a usable
    /// and not repeated (case ignored) folder name. The index is the slot's position in the list, so ids stay when an earlier slot is switched off.
    /// </summary>
    public static IReadOnlyList<WallpaperSlot> Slots(PluginConfiguration config)
    {
        if (config.WallpapersFlat)
        {
            return new[] { new WallpaperSlot(0, string.Empty, "wallpapersmerged", "Wallpapers & Artwork") };
        }

        var slots = new List<WallpaperSlot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 10; i++)
        {
            string Text(string part) => (string)SlotAccess["WallpaperSlot" + i + part].GetValue(config)!;
            var folder = Text("Folder").Trim();
            if (!(bool)SlotAccess["WallpaperSlot" + i + "On"].GetValue(config)! || FolderNameError(folder, false) is not null || !seen.Add(folder))
            {
                continue;
            }

            var label = (WallpaperLabel)SlotAccess["WallpaperSlot" + i + "Label"].GetValue(config)!;
            var own = Text("OwnLabel").Trim();
            slots.Add(label switch
            {
                WallpaperLabel.Wallpapers => new WallpaperSlot(i, folder, "wallpapers", "Wallpapers"),
                WallpaperLabel.FanArt => new WallpaperSlot(i, folder, "fanart", "Fan Art"),
                WallpaperLabel.ConceptArt => new WallpaperSlot(i, folder, "conceptart", "Concept Art"),
                WallpaperLabel.Posters => new WallpaperSlot(i, folder, "posters", "Posters"),
                WallpaperLabel.BoxArt => new WallpaperSlot(i, folder, "boxart", "Box Art"),
                WallpaperLabel.Renders => new WallpaperSlot(i, folder, "renders", "Renders"),
                WallpaperLabel.Logos => new WallpaperSlot(i, folder, "logos", "Logos"),
                WallpaperLabel.Icons => new WallpaperSlot(i, folder, "icons", "Icons"),
                _ => new WallpaperSlot(i, folder, null, own.Length == 0 || own.Length > 100 ? folder : own)
            });
        }

        return slots;
    }

    /// <summary>True when the area can show anything for the settings: Enable Wallpapers on, a usable wallpapers folder name.</summary>
    public static bool Enabled(PluginConfiguration config) => AreaFolder(config) is not null;

    /// <summary>
    /// The wallpapers of the version folder <paramref name="versionFolder"/>, read at most every 10 seconds per folder and slot settings: per slot the image
    /// files (png, jpg, jpeg, webp, gif; not empty) lying directly in the slot folder (Flat: in the wallpapers folder itself) in natural name order. A folder
    /// that is missing, is a junction or cannot be read gives no files. Empty while the area is off.
    /// </summary>
    public static WallpaperArea Read(string versionFolder, PluginConfiguration config)
    {
        var area = AreaFolder(config);
        if (area is null)
        {
            return WallpaperArea.Empty;
        }

        var slots = Slots(config);
        var key = versionFolder + "|" + area + "|" + string.Join("|", slots.Select(s => s.Index + ":" + s.Folder + ":" + s.Key + ":" + s.Label));
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < Reuse)
        {
            return cached.Area;
        }

        var areaPath = Path.Combine(versionFolder, area);
        var files = new Dictionary<int, IReadOnlyList<WallpaperFile>>();
        if (AreaCommon.IsPlainDirectory(areaPath))
        {
            foreach (var slot in slots)
            {
                var directory = slot.Folder.Length == 0 ? areaPath : Path.Combine(areaPath, slot.Folder);
                if (slot.Folder.Length != 0 && !AreaCommon.IsPlainDirectory(directory))
                {
                    continue;
                }

                try
                {
                    var found = new DirectoryInfo(directory).EnumerateFiles("*", AreaCommon.ScanOptions)
                        .Where(f => ImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.Length > 0)
                        .OrderBy(f => f.Name, Comparer<string>.Create(NaturalCompare))
                        .Select(f => new WallpaperFile(slot.Index, f.FullName, f.Name, f.LastWriteTimeUtc, f.Length, NameHash(f.Name)))
                        .ToList();
                    if (found.Count > 0)
                    {
                        files[slot.Index] = found;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable slot folder shows no wallpapers.
                }
            }
        }

        var read = new WallpaperArea(slots, files);
        if (Cache.Count > 64)
        {
            foreach (var old in Cache.Where(e => DateTime.UtcNow - e.Value.At >= Reuse).Select(e => e.Key).ToList())
            {
                Cache.TryRemove(old, out _);
            }
        }

        Cache[key] = (DateTime.UtcNow, read);
        return read;
    }

    /// <summary>The plan of the rows (<see cref="AreaCommon.Layout"/>) over the slots that have pictures; the Merge ticks are read from <paramref name="config"/> now, not from the cached listing.</summary>
    private static IReadOnlyList<AreaCommon.LayoutRow> LayoutOf(WallpaperArea area, PluginConfiguration config) =>
        AreaCommon.Layout(config.WallpapersFlat, config.WallpapersDisplay,
            area.Slots.Where(s => area.Files.ContainsKey(s.Index)).Select(s => (s.Index, AreaCommon.MergeOf(config, "WallpaperSlot", s.Index))));

    /// <summary>
    /// The rows of the page as <see cref="AreaCommon.Layout"/> plans them: Flat = one row "Wallpapers &amp; Artwork"; one row per slot that has pictures (slot list order) or, merged, one row
    /// over the slots ticked in Merge at the place of the first of them, whatever shape their pictures have. No row without pictures.
    /// </summary>
    public static IReadOnlyList<WallpaperRow> Rows(WallpaperArea area, PluginConfiguration config)
    {
        if (area.Total == 0)
        {
            return Array.Empty<WallpaperRow>();
        }

        return LayoutOf(area, config).Select(r =>
        {
            if (r.Row == AllRows)
            {
                return new WallpaperRow(AllRows, "wallpapersmerged", "Wallpapers & Artwork", r.Slots.Sum(i => area.Files[i].Count));
            }

            var slot = area.Slots.First(s => s.Index == r.Row);
            return new WallpaperRow(slot.Index, slot.Key, slot.Label, area.Files[slot.Index].Count);
        }).ToList();
    }

    /// <summary>
    /// The files of a row in the order they are shown and the viewer pages through: a slot's files sorted by the sort setting; the merged row
    /// (<see cref="AllRows"/>) the slots one after the other, each sorted (Keep Slots Together), else all files mixed by the sort. Random uses
    /// <paramref name="seed"/>, so the same seed gives the same order wherever it is asked.
    /// </summary>
    public static IReadOnlyList<WallpaperFile> Files(WallpaperArea area, int row, PluginConfiguration config, int seed)
    {
        if (row != AllRows)
        {
            return area.Files.TryGetValue(row, out var one) ? Sort(one, config.WallpapersSort, seed) : Array.Empty<WallpaperFile>();
        }

        if (LayoutOf(area, config).FirstOrDefault(r => r.Row == AllRows) is not { } merged)
        {
            return Array.Empty<WallpaperFile>();
        }

        if (config.WallpapersKeepSlotsTogether)
        {
            return merged.Slots.SelectMany(i => Sort(area.Files[i], config.WallpapersSort, seed)).ToList();
        }

        return Sort(merged.Slots.SelectMany(i => area.Files[i]).ToList(), config.WallpapersSort, seed);
    }

    private static IReadOnlyList<WallpaperFile> Sort(IReadOnlyList<WallpaperFile> files, ScreenshotSort sort, int seed)
    {
        // The input is in natural name order per slot and slot order; the order keys below are stable, so equal keys keep it.
        var name = Comparer<WallpaperFile>.Create((a, b) => NaturalCompare(a.FileName, b.FileName));
        return sort switch
        {
            ScreenshotSort.NameDescending => files.OrderByDescending(f => f, name).ToList(),
            ScreenshotSort.DateAscending => files.OrderBy(f => f.Modified).ThenBy(f => f, name).ToList(),
            ScreenshotSort.DateDescending => files.OrderByDescending(f => f.Modified).ThenBy(f => f, name).ToList(),
            ScreenshotSort.Random => files.OrderBy(f => AreaCommon.Mix(seed, f.Slot, f.Hash)).ThenBy(f => f.Slot).ThenBy(f => f.Hash).ToList(),
            _ => files.OrderBy(f => f, name).ToList()
        };
    }

    /// <summary>A new seed for a page view: random within 1 to <see cref="MaxSeed"/> for Random, else 0 (the order does not depend on it).</summary>
    public static int NewSeed(PluginConfiguration config) => config.WallpapersSort == ScreenshotSort.Random ? RandomNumberGenerator.GetInt32(1, MaxSeed + 1) : 0;

    /// <summary>The seed a request asks for: itself when it is within 1 to <see cref="MaxSeed"/> and the sort is Random, a new one for Random without one, else 0.</summary>
    public static int SeedFor(PluginConfiguration config, int? asked) =>
        config.WallpapersSort != ScreenshotSort.Random ? 0 : asked is > 0 and <= MaxSeed ? asked.Value : NewSeed(config);

    /// <summary>File names in natural order: digit runs compare by value, other characters case ignored (so "shot2" comes before "shot10").</summary>
    public static int NaturalCompare(string? a, string? b) => AreaCommon.NaturalCompare(a, b);

    /// <summary>The 56 bits of a file name's SHA-256 (lower case name) that go into an item id.</summary>
    public static ulong NameHash(string fileName) => AreaCommon.NameHash(fileName);

    /// <summary>The image tag of a file: from its last write time and length, as ImageCache.Tag.</summary>
    public static string Tag(WallpaperFile file) => AreaCommon.Tag(file.Modified, file.Length);

    /// <summary>
    /// The id of a wallpaper: short id of the version (4 bytes), slot (1), seed (3), file name hash (7), marker 0xB8 (1). Not a name based id: Jellyfin
    /// accepts any GUID, and the fields are what the server needs to find the file again.
    /// </summary>
    public static Guid ItemId(string shortId, int slot, int seed, ulong hash) => Build(shortId, slot, seed, hash, ItemMarker);

    /// <summary>The id of a row (the ParentId of its items, which Jellyfin's viewer asks the sibling list by): as an item id with no file hash and marker 0xB9.</summary>
    public static Guid RowId(string shortId, int row, int seed) => Build(shortId, row, seed, 0, RowMarker);

    /// <summary>The row an item of slot <paramref name="slot"/> belongs to for the settings and the listing: 0 for Flat, <see cref="AllRows"/> when the slot is in the merged row, else its slot.</summary>
    public static int RowOf(WallpaperArea area, PluginConfiguration config, int slot) =>
        config.WallpapersFlat ? 0 : LayoutOf(area, config).FirstOrDefault(r => r.Slots.Contains(slot))?.Row ?? slot;

    private static Guid Build(string shortId, int slot, int seed, ulong hash, byte marker) => AreaCommon.BuildId(shortId, slot, seed, hash, marker, RowMarker);

    /// <summary>What an id says; null when it is neither a wallpaper id nor a row id (cheap: no disk, no lookup).</summary>
    public static WallpaperKey? Decode(Guid id) =>
        AreaCommon.Decode(id, ItemMarker, RowMarker) is { } k ? new WallpaperKey(k.ShortId, k.Slot, k.Seed, k.Hash, k.IsRow) : null;

}
