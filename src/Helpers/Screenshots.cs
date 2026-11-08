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

/// <summary>One slot of the screenshots area: its position in the slot list (1 to 10; 0 = the implicit slot of Flat), its sub folder ("" for Flat), the key of a predefined label (null for an own one) and the label in English or as typed.</summary>
public sealed record ScreenshotSlot(int Index, string Folder, string? Key, string Label);

/// <summary>One screenshot file of a slot: Hash = 56 bits of the SHA-256 of its lower case file name (the part of its id that names it), Modified = last write time (UTC).</summary>
public sealed record ScreenshotFile(int Slot, string Path, string FileName, DateTime Modified, long Length, ulong Hash);

/// <summary>A row of the area as the page shows it: Row = the slot index, <see cref="Screenshots.AllRows"/> for the merged row, 0 for Flat.</summary>
public sealed record ScreenshotRow(int Row, string? Key, string Label, int Count);

/// <summary>What an id says: the short id of the version, the slot (or row), the seed of the random order, the file name hash (items) and whether it is a row id (the ParentId of a row's items).</summary>
public sealed record ScreenshotKey(string ShortId, int Slot, int Seed, ulong Hash, bool IsRow);

/// <summary>The screenshots of one version as they lie on disk: per slot the files in natural name order.</summary>
public sealed class ScreenshotArea
{
    public static readonly ScreenshotArea Empty = new(Array.Empty<ScreenshotSlot>(), new Dictionary<int, IReadOnlyList<ScreenshotFile>>());

    private readonly Dictionary<(int, ulong), ScreenshotFile> _byHash = new();

    public ScreenshotArea(IReadOnlyList<ScreenshotSlot> slots, IReadOnlyDictionary<int, IReadOnlyList<ScreenshotFile>> files)
    {
        Slots = slots;
        Files = files;
        foreach (var file in files.Values.SelectMany(f => f))
        {
            _byHash.TryAdd((file.Slot, file.Hash), file);
        }
    }

    public IReadOnlyList<ScreenshotSlot> Slots { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<ScreenshotFile>> Files { get; }

    public int Total => Files.Values.Sum(f => f.Count);

    public ScreenshotFile? Find(int slot, ulong hash) => _byHash.TryGetValue((slot, hash), out var file) ? file : null;
}

/// <summary>
/// The screenshots area of a game page (a special feature, not artwork): images in &lt;version folder&gt;\&lt;screenshots folder&gt;\&lt;slot folder&gt;\ of the selected
/// version only (the version folder of the extras root GameExtras.Root chooses). Read only, and only when the area is asked: the listing of a version is kept
/// for 10 seconds. A screenshot is served as a virtual Photo item whose id carries everything needed to find it again (short id of the version, slot,
/// seed, file name hash), so it resolves after a restart without any stored map; none is in Jellyfin's database.
/// </summary>
public static class Screenshots
{
    /// <summary>The row id of the merged row (and the slot byte of its ParentId).</summary>
    public const int AllRows = 255;

    /// <summary>The first tiles of a row (Show more adds the same number again).</summary>
    public const int FirstTiles = 60;

    /// <summary>The largest seed (24 bits).</summary>
    public const int MaxSeed = (1 << 24) - 1;

    private const byte ItemMarker = 0xB5;

    private const byte RowMarker = 0xB6;

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(10);

    private static readonly string[] SlotProperties = Enumerable.Range(1, 10).SelectMany(i => new[] { "Folder", "Label", "OwnLabel", "On" }.Select(s => "ScreenshotSlot" + i + s)).ToArray();

    private static readonly Dictionary<string, PropertyInfo> SlotAccess = SlotProperties.ToDictionary(n => n, n => typeof(PluginConfiguration).GetProperty(n)!);

    private static readonly ConcurrentDictionary<string, (DateTime At, ScreenshotArea Area)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static int _reads;

    /// <summary>Number of folder reads made.</summary>
    public static int Reads => _reads;

    /// <summary>
    /// Why <paramref name="name"/> is no usable folder name, or null: not empty, at most 100 characters, no character Windows does not allow (so no path, no way
    /// upwards), no trailing dot or space, not "." or "..". An <paramref name="area"/> name must not be "extrafanart" or one of Jellyfin's extras folder names either.
    /// </summary>
    public static string? FolderNameError(string? name, bool area) => AreaCommon.FolderNameError(name, area);

    /// <summary>The configured screenshots folder name, trimmed (also an unusable one: the extras scan leaves a folder of that name alone in any case).</summary>
    public static string AreaName(PluginConfiguration config) => (config.ScreenshotsFolder ?? string.Empty).Trim();

    /// <summary>The usable screenshots folder name, or null while Enable Screenshots is off or the name is refused.</summary>
    public static string? AreaFolder(PluginConfiguration config) =>
        config.EnableScreenshots && FolderNameError(AreaName(config), true) is null && !AreaCommon.Clashes(config, "screenshots", AreaName(config)) ? AreaName(config) : null;

    /// <summary>
    /// The slots in use, in slot list order: Flat = the one implicit slot (index 0, no sub folder, row "Screenshots"); else every slot that is on with a usable
    /// and not repeated (case ignored) folder name. The index is the slot's position in the list, so ids stay when an earlier slot is switched off.
    /// </summary>
    public static IReadOnlyList<ScreenshotSlot> Slots(PluginConfiguration config)
    {
        if (config.ScreenshotsFlat)
        {
            return new[] { new ScreenshotSlot(0, string.Empty, "screenshots", "Screenshots") };
        }

        var slots = new List<ScreenshotSlot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 10; i++)
        {
            string Text(string part) => (string)SlotAccess["ScreenshotSlot" + i + part].GetValue(config)!;
            var folder = Text("Folder").Trim();
            if (!(bool)SlotAccess["ScreenshotSlot" + i + "On"].GetValue(config)! || FolderNameError(folder, false) is not null || !seen.Add(folder))
            {
                continue;
            }

            var label = (ScreenshotLabel)SlotAccess["ScreenshotSlot" + i + "Label"].GetValue(config)!;
            var own = Text("OwnLabel").Trim();
            slots.Add(label switch
            {
                ScreenshotLabel.Title => new ScreenshotSlot(i, folder, "title", "Title"),
                ScreenshotLabel.TitleScreenshots => new ScreenshotSlot(i, folder, "titlescreenshots", "Title Screenshots"),
                ScreenshotLabel.Select => new ScreenshotSlot(i, folder, "select", "Select"),
                ScreenshotLabel.SelectScreenshots => new ScreenshotSlot(i, folder, "selectscreenshots", "Select Screenshots"),
                ScreenshotLabel.Gameplay => new ScreenshotSlot(i, folder, "gameplay", "Gameplay"),
                ScreenshotLabel.GameplayScreenshots => new ScreenshotSlot(i, folder, "gameplayscreenshots", "Gameplay Screenshots"),
                ScreenshotLabel.GameOver => new ScreenshotSlot(i, folder, "gameover", "Game Over"),
                ScreenshotLabel.GameOverScreenshots => new ScreenshotSlot(i, folder, "gameoverscreenshots", "Game Over Screenshots"),
                ScreenshotLabel.HighScores => new ScreenshotSlot(i, folder, "highscores", "High Scores"),
                ScreenshotLabel.HighScoresScreenshots => new ScreenshotSlot(i, folder, "highscoresscreenshots", "High Scores Screenshots"),
                _ => new ScreenshotSlot(i, folder, null, own.Length == 0 || own.Length > 100 ? folder : own)
            });
        }

        return slots;
    }

    /// <summary>True when the area can show anything for the settings: Enable Screenshots on, a usable screenshots folder name.</summary>
    public static bool Enabled(PluginConfiguration config) => AreaFolder(config) is not null;

    /// <summary>
    /// The screenshots of the version folder <paramref name="versionFolder"/>, read at most every 10 seconds per folder and slot settings: per slot the image
    /// files (png, jpg, jpeg, webp, gif; not empty) lying directly in the slot folder (Flat: in the screenshots folder itself) in natural name order. A folder
    /// that is missing, is a junction or cannot be read gives no files. Empty while the area is off.
    /// </summary>
    public static ScreenshotArea Read(string versionFolder, PluginConfiguration config)
    {
        var area = AreaFolder(config);
        if (area is null)
        {
            return ScreenshotArea.Empty;
        }

        var slots = Slots(config);
        var key = versionFolder + "|" + area + "|" + string.Join("|", slots.Select(s => s.Index + ":" + s.Folder + ":" + s.Key + ":" + s.Label));
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < Reuse)
        {
            return cached.Area;
        }

        System.Threading.Interlocked.Increment(ref _reads);
        var areaPath = Path.Combine(versionFolder, area);
        var files = new Dictionary<int, IReadOnlyList<ScreenshotFile>>();
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
                        .Select(f => new ScreenshotFile(slot.Index, f.FullName, f.Name, f.LastWriteTimeUtc, f.Length, NameHash(f.Name)))
                        .ToList();
                    if (found.Count > 0)
                    {
                        files[slot.Index] = found;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable slot folder shows no screenshots.
                }
            }
        }

        var read = new ScreenshotArea(slots, files);
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

    /// <summary>The plan of the rows (<see cref="AreaCommon.Layout"/>) over the slots that have images; the Merge ticks are read from <paramref name="config"/> now, not from the cached listing.</summary>
    private static IReadOnlyList<AreaCommon.LayoutRow> LayoutOf(ScreenshotArea area, PluginConfiguration config) =>
        AreaCommon.Layout(config.ScreenshotsFlat, config.ScreenshotsDisplay,
            area.Slots.Where(s => area.Files.ContainsKey(s.Index)).Select(s => (s.Index, AreaCommon.MergeOf(config, "ScreenshotSlot", s.Index))));

    /// <summary>
    /// The rows of the page as <see cref="AreaCommon.Layout"/> plans them: Flat = one row "Screenshots"; one row per slot that has images (slot list order) or, merged, one row
    /// "Screenshots" over the slots ticked in Merge at the place of the first of them. No row without images.
    /// </summary>
    public static IReadOnlyList<ScreenshotRow> Rows(ScreenshotArea area, PluginConfiguration config)
    {
        if (area.Total == 0)
        {
            return Array.Empty<ScreenshotRow>();
        }

        return LayoutOf(area, config).Select(r =>
        {
            if (r.Row == AllRows)
            {
                return new ScreenshotRow(AllRows, "screenshots", "Screenshots", r.Slots.Sum(i => area.Files[i].Count));
            }

            var slot = area.Slots.First(s => s.Index == r.Row);
            return new ScreenshotRow(slot.Index, slot.Key, slot.Label, area.Files[slot.Index].Count);
        }).ToList();
    }

    /// <summary>
    /// The files of a row in the order they are shown and the viewer pages through: a slot's files sorted by the sort setting; the merged row
    /// (<see cref="AllRows"/>) the slots one after the other, each sorted (Keep Slots Together), else all files mixed by the sort. Random uses
    /// <paramref name="seed"/>, so the same seed gives the same order wherever it is asked.
    /// </summary>
    public static IReadOnlyList<ScreenshotFile> Files(ScreenshotArea area, int row, PluginConfiguration config, int seed)
    {
        if (row != AllRows)
        {
            return area.Files.TryGetValue(row, out var one) ? Sort(one, config.ScreenshotsSort, seed) : Array.Empty<ScreenshotFile>();
        }

        if (LayoutOf(area, config).FirstOrDefault(r => r.Row == AllRows) is not { } merged)
        {
            return Array.Empty<ScreenshotFile>();
        }

        if (config.ScreenshotsKeepSlotsTogether)
        {
            return merged.Slots.SelectMany(i => Sort(area.Files[i], config.ScreenshotsSort, seed)).ToList();
        }

        return Sort(merged.Slots.SelectMany(i => area.Files[i]).ToList(), config.ScreenshotsSort, seed);
    }

    private static IReadOnlyList<ScreenshotFile> Sort(IReadOnlyList<ScreenshotFile> files, ScreenshotSort sort, int seed)
    {
        // The input is in natural name order per slot and slot order; the order keys below are stable, so equal keys keep it.
        var name = Comparer<ScreenshotFile>.Create((a, b) => NaturalCompare(a.FileName, b.FileName));
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
    public static int NewSeed(PluginConfiguration config) => config.ScreenshotsSort == ScreenshotSort.Random ? RandomNumberGenerator.GetInt32(1, MaxSeed + 1) : 0;

    /// <summary>The seed a request asks for: itself when it is within 1 to <see cref="MaxSeed"/> and the sort is Random, a new one for Random without one, else 0.</summary>
    public static int SeedFor(PluginConfiguration config, int? asked) =>
        config.ScreenshotsSort != ScreenshotSort.Random ? 0 : asked is > 0 and <= MaxSeed ? asked.Value : NewSeed(config);

    /// <summary>File names in natural order: digit runs compare by value, other characters case ignored (so "shot2" comes before "shot10").</summary>
    public static int NaturalCompare(string? a, string? b) => AreaCommon.NaturalCompare(a, b);

    /// <summary>The 56 bits of a file name's SHA-256 (lower case name) that go into an item id.</summary>
    public static ulong NameHash(string fileName) => AreaCommon.NameHash(fileName);

    /// <summary>The image tag of a file: from its last write time and length, as ImageCache.Tag.</summary>
    public static string Tag(ScreenshotFile file) => AreaCommon.Tag(file.Modified, file.Length);

    /// <summary>
    /// The id of a screenshot: short id of the version (4 bytes), slot (1), seed (3), file name hash (7), marker 0xB5 (1). Not a name based id: Jellyfin
    /// accepts any GUID, and the fields are what the server needs to find the file again.
    /// </summary>
    public static Guid ItemId(string shortId, int slot, int seed, ulong hash) => Build(shortId, slot, seed, hash, ItemMarker);

    /// <summary>The id of a row (the ParentId of its items, which Jellyfin's viewer asks the sibling list by): as an item id with no file hash and marker 0xB6.</summary>
    public static Guid RowId(string shortId, int row, int seed) => Build(shortId, row, seed, 0, RowMarker);

    /// <summary>The row an item of slot <paramref name="slot"/> belongs to for the settings and the listing: 0 for Flat, <see cref="AllRows"/> when the slot is in the merged row, else its slot.</summary>
    public static int RowOf(ScreenshotArea area, PluginConfiguration config, int slot) =>
        config.ScreenshotsFlat ? 0 : LayoutOf(area, config).FirstOrDefault(r => r.Slots.Contains(slot))?.Row ?? slot;

    private static Guid Build(string shortId, int slot, int seed, ulong hash, byte marker) => AreaCommon.BuildId(shortId, slot, seed, hash, marker, RowMarker);

    /// <summary>What an id says; null when it is neither a screenshot id nor a row id (cheap: no disk, no lookup).</summary>
    public static ScreenshotKey? Decode(Guid id) =>
        AreaCommon.Decode(id, ItemMarker, RowMarker) is { } k ? new ScreenshotKey(k.ShortId, k.Slot, k.Seed, k.Hash, k.IsRow) : null;

}
