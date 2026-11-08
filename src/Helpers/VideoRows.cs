using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>The nine special feature types of Jellyfin as table rows of the Videos area (Extras = Jellyfin's "Unknown" type); the order is the table order.</summary>
public enum VideoRowType
{
    Extras,
    Clips,
    BehindTheScenes,
    DeletedScenes,
    Interviews,
    Scenes,
    Samples,
    Shorts,
    Featurettes
}

/// <summary>One own video slot that is in use and valid: Index 1 to 10 (stable when an earlier slot is off), Parent (empty = directly in the game folder), Folder, Label (empty = the folder name).</summary>
public sealed record VideoSlot(int Index, string Parent, string Folder, string Label, bool Merge)
{
    /// <summary>The path of the slot below a game folder, with '\' ("videos\Gameplay" or "Gameplay").</summary>
    public string Path => Parent.Length == 0 ? Folder : Parent + "\\" + Folder;
}

/// <summary>Which rows go into Jellyfin's Special Features row (Native) and which stay rows of their own, planned for one game page; SteamMerged = the Steam clips are in the native row.</summary>
public sealed record VideoPlan(IReadOnlySet<string> NativeKeys, bool SteamMerged);

/// <summary>
/// The Videos area as a table: the nine types, then ten own slots (the Steam row is handled by the Steam code). Pure functions on the settings and on the extras of a page:
/// which rows are shown, which are merged into Jellyfin's Special Features row, in which order the merged row lists its videos.
/// </summary>
public static class VideoRows
{
    /// <summary>The first tiles of an unmerged row (as the picture areas); "Show more" asks for more.</summary>
    public const int FirstTiles = 60;

    /// <summary>The key of the Steam row in plans and answers.</summary>
    public const string SteamKey = "steam";

    private static readonly ConcurrentDictionary<string, PropertyInfo> Properties = new();

    /// <summary>The types in table order.</summary>
    public static readonly IReadOnlyList<VideoRowType> Types = Enum.GetValues<VideoRowType>();

    /// <summary>The key of a type in rows and answers: lower case ("extras", "clips", "behindthescenes" ...).</summary>
    public static string TypeKey(VideoRowType type) => type.ToString().ToLowerInvariant();

    /// <summary>The Jellyfin type of a special feature as a table row; null for a type that is no special feature.</summary>
    public static VideoRowType? TypeOf(ExtraType type) => type switch
    {
        ExtraType.Unknown => VideoRowType.Extras,
        ExtraType.Clip => VideoRowType.Clips,
        ExtraType.BehindTheScenes => VideoRowType.BehindTheScenes,
        ExtraType.DeletedScene => VideoRowType.DeletedScenes,
        ExtraType.Interview => VideoRowType.Interviews,
        ExtraType.Scene => VideoRowType.Scenes,
        ExtraType.Sample => VideoRowType.Samples,
        ExtraType.Short => VideoRowType.Shorts,
        ExtraType.Featurette => VideoRowType.Featurettes,
        _ => null
    };

    private static T Get<T>(PluginConfiguration config, string name) =>
        (T)Properties.GetOrAdd(name, n => typeof(PluginConfiguration).GetProperty(n)!).GetValue(config)!;

    /// <summary>The On tick of a type.</summary>
    public static bool TypeOn(PluginConfiguration config, VideoRowType type) => Get<bool>(config, "Video" + type + "On");

    /// <summary>The Merge tick of a type.</summary>
    public static bool TypeMerge(PluginConfiguration config, VideoRowType type) => Get<bool>(config, "Video" + type + "Merge");

    /// <summary>The title key of a type ("" = the type word in the viewer's language, "Own" = <see cref="TypeLabel"/>).</summary>
    public static string TypeTitle(PluginConfiguration config, VideoRowType type) => Get<string>(config, "Video" + type + "Title") == "Own" ? "Own" : string.Empty;

    /// <summary>The typed title of a type, at most 40 characters, trimmed.</summary>
    public static string TypeLabel(PluginConfiguration config, VideoRowType type) => Clamp(Get<string>(config, "Video" + type + "Label"));

    private static string Clamp(string? text) => (text ?? string.Empty).Trim() is { Length: > 40 } t ? t[..40] : (text ?? string.Empty).Trim();

    /// <summary>
    /// The own slots in use that are valid, in slot order: a plain Folder (not empty; for an empty Parent also none of Jellyfin's extras folder names, which the types already read), a Parent
    /// that is empty or a plain folder name, a path not used by an earlier slot (case ignored), no clash with the folder of another area that is on (Parent when set, else Folder). A slot that
    /// fails is left out, the admin page says why under the table.
    /// </summary>
    public static IReadOnlyList<VideoSlot> Slots(PluginConfiguration config)
    {
        var result = new List<VideoSlot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 10; i++)
        {
            if (!Get<bool>(config, "VideoSlot" + i + "On"))
            {
                continue;
            }

            var parent = (Get<string>(config, "VideoSlot" + i + "Parent") ?? string.Empty).Trim();
            var folder = (Get<string>(config, "VideoSlot" + i + "Folder") ?? string.Empty).Trim();
            var label = Clamp(Get<string>(config, "VideoSlot" + i + "Label"));
            if (AreaCommon.FolderNameError(folder, parent.Length == 0) is not null || (parent.Length > 0 && AreaCommon.FolderNameError(parent, true) is not null))
            {
                continue;
            }

            if (AreaCommon.Clashes(config, "videos", parent.Length == 0 ? folder : parent))
            {
                continue;
            }

            var slot = new VideoSlot(i, parent, folder, label.Length == 0 ? folder : label, Get<bool>(config, "VideoSlot" + i + "Merge"));
            if (seen.Add(slot.Path))
            {
                result.Add(slot);
            }
        }

        return result;
    }

    /// <summary>The names the generic read of a game folder must skip because a valid slot reads them: the Folder of a slot without a Parent. A Parent folder is NOT skipped: its loose files stay what they are without the slot (the generic read does not go deeper, so the slot folders below are not read twice). Joined with '|'.</summary>
    public static string SkipNames(IReadOnlyList<VideoSlot> slots) =>
        string.Join("|", slots.Where(s => s.Parent.Length == 0).Select(s => s.Folder).Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>The cache key of the slots: their paths, so a changed slot folder reads again.</summary>
    public static string SlotKey(IReadOnlyList<VideoSlot> slots) => string.Join(";", slots.Select(s => s.Index + "=" + s.Path.ToLowerInvariant()));

    /// <summary>The key of the row an extra belongs to: "slot3" for an own slot, else the type key.</summary>
    public static string RowKey(GameExtra extra) =>
        extra.Slot > 0 ? "slot" + extra.Slot : TypeOf(extra.ExtraType) is VideoRowType t ? TypeKey(t) : TypeKey(VideoRowType.Extras);

    /// <summary>The keys of all rows in their default order: the nine types, then the ten own slots.</summary>
    public static readonly IReadOnlyList<string> DefaultOrder = Types.Select(TypeKey).Concat(Enumerable.Range(1, 10).Select(n => "slot" + n)).ToList();

    /// <summary>The order of the rows as set in VideosRowOrder: the known keys listed (each once), then the missing ones in their default order.</summary>
    public static IReadOnlyList<string> RowOrder(PluginConfiguration config)
    {
        var order = new List<string>();
        foreach (var part in (config.VideosRowOrder ?? string.Empty).Split(','))
        {
            var key = part.Trim().ToLowerInvariant();
            if (DefaultOrder.Contains(key) && !order.Contains(key))
            {
                order.Add(key);
            }
        }

        order.AddRange(DefaultOrder.Where(key => !order.Contains(key)));
        return order;
    }

    /// <summary>The position of a row key in the order of the table, counted from 1; 99 for an unknown key.</summary>
    public static int TableIndex(PluginConfiguration config, string key)
    {
        var at = RowOrder(config).ToList().IndexOf(key);
        return at < 0 ? 99 : at + 1;
    }

    /// <summary>True when the row of a special feature is switched on (a type's On tick; an own slot is on by being read at all).</summary>
    public static bool Shown(PluginConfiguration config, GameExtra extra) =>
        extra.Kind != ExtraKind.Special || extra.Slot > 0 || (TypeOf(extra.ExtraType) is VideoRowType t ? TypeOn(config, t) : TypeOn(config, VideoRowType.Extras));

    /// <summary>The Merge tick of the row of an extra.</summary>
    public static bool MergeOf(PluginConfiguration config, GameExtra extra, IReadOnlyList<VideoSlot> slots) =>
        extra.Slot > 0 ? slots.FirstOrDefault(s => s.Index == extra.Slot)?.Merge ?? true
            : TypeMerge(config, TypeOf(extra.ExtraType) ?? VideoRowType.Extras);

    /// <summary>
    /// Which rows go into Jellyfin's Special Features row on a page: <paramref name="specials"/> = the shown special features of the page, <paramref name="steamHasItems"/> = the Steam clips of the page
    /// that would be merged (Steam on and its Merge ticked). One Row per Slot merges nothing; Merged puts every ticked row that has videos into the native row; Adaptive does so only while at least two
    /// ticked rows have videos (Steam counts as a row).
    /// </summary>
    public static VideoPlan Plan(PluginConfiguration config, IEnumerable<GameExtra> specials, bool steamHasItems)
    {
        var slots = Slots(config);
        var keys = specials.Where(e => e.Kind == ExtraKind.Special && MergeOf(config, e, slots)).Select(RowKey).Distinct().ToList();
        var steam = steamHasItems && config.SteamSpecialsMerge;
        var candidates = keys.Count + (steam ? 1 : 0);
        var merge = config.VideosDisplay switch
        {
            ScreenshotDisplay.Merged => candidates > 0,
            ScreenshotDisplay.Adaptive => candidates > 1,
            _ => false
        };
        return merge ? new VideoPlan(new HashSet<string>(keys), steam) : new VideoPlan(new HashSet<string>(), false);
    }

    /// <summary>The seed of a page: the same for one user, one game and one 10-minute bucket, so the merged row, the rows of their own and every "Show more" page agree on a Random order.</summary>
    public static int Seed(Guid? user, string gameId, DateTime nowUtc) =>
        StableHash((user?.ToString("N") ?? string.Empty) + "|" + gameId.ToLowerInvariant() + "|" + (nowUtc.Ticks / (TimeSpan.TicksPerMinute * 10)).ToString(System.Globalization.CultureInfo.InvariantCulture));

    // FNV-1a over the characters (string.GetHashCode differs between runs).
    private static int StableHash(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    /// <summary>The sort of level 1: the files inside one row (a stable order; Random shuffles with <paramref name="seed"/>, a new shuffle each call without one).</summary>
    public static IReadOnlyList<GameExtra> SortRow(IEnumerable<GameExtra> files, ScreenshotSort mode, int? seed = null)
    {
        var list = files.ToList();
        return mode switch
        {
            ScreenshotSort.NameDescending => list.OrderByDescending(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenByDescending(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            ScreenshotSort.DateAscending => list.OrderBy(e => e.Modified).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            ScreenshotSort.DateDescending => list.OrderByDescending(e => e.Modified).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(),
            // the list is put in a fixed order first, so the same seed gives the same shuffle whatever order the files came in
            ScreenshotSort.Random => Shuffle(list.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(), seed is int s0 ? new Random(s0) : new Random()),
            _ => list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static List<GameExtra> Shuffle(List<GameExtra> list, Random random)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>
    /// The order of the videos inside Jellyfin's merged Special Features row. The page's list comes block by block (the shown version first, then the others, <see cref="GameExtra.Block"/> from
    /// <see cref="ExtrasIndex.ForVersion"/>), the blocks stay; inside a block: Keep Slots Together off sorts all videos together by VideosSort (slot videos too: nothing is forced last), on lists the rows
    /// in table order, each sorted by VideosSort. Random uses <paramref name="seed"/> (<see cref="Seed"/>).
    /// </summary>
    public static IReadOnlyList<GameExtra> OrderNative(PluginConfiguration config, IReadOnlyList<GameExtra> merged, int? seed = null)
    {
        var result = new List<GameExtra>(merged.Count);
        foreach (var block in merged.GroupBy(e => e.Block).OrderBy(g => g.Key))
        {
            if (config.VideosKeepSlotsTogether)
            {
                foreach (var row in block.GroupBy(RowKey).OrderBy(g => TableIndex(config, g.Key)))
                {
                    result.AddRange(SortRow(row, config.VideosSort, seed is int s1 ? s1 ^ StableHash("n" + block.Key + row.Key) : null));
                }
            }
            else
            {
                result.AddRange(SortRow(block, config.VideosSort, seed is int s2 ? s2 ^ StableHash("n" + block.Key) : null));
            }
        }

        return result;
    }

    /// <summary>The extras of an unmerged row <paramref name="key"/>, sorted by VideosSort (Random with a seed of the page and the row, so every "Show more" page cuts the same order).</summary>
    public static IReadOnlyList<GameExtra> OrderRow(PluginConfiguration config, IEnumerable<GameExtra> row, string key, int? seed = null) =>
        SortRow(row, config.VideosSort, seed is int s3 ? s3 ^ StableHash("r" + key) : null);

    /// <summary>
    /// The type a video is shown as in an own slot: the file name's ending read by Jellyfin's rules (ExtraRuleResolver, suffix and file name rules; the folder rules do not apply because the slot is the folder),
    /// Unknown when the name says none. A trailer ending is kept (it shows as "Trailer"), the theme endings are not shown.
    /// </summary>
    public static ExtraType SlotType(string fileName, NamingOptions naming)
    {
        // a neutral directory, so the folder rules of Jellyfin can never match
        var info = ExtraRuleResolver.GetExtraInfo(System.IO.Path.Combine("slot", fileName), naming);
        return info.ExtraType is { } type && type != ExtraType.ThemeSong && type != ExtraType.ThemeVideo ? type : ExtraType.Unknown;
    }
}
