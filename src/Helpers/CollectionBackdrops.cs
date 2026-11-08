using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One backdrop of a collection page: the tag of its file and its position (Backdrop/{Index}) in the list of its owner (the own pictures or one game).</summary>
public sealed record CollectionBackdrop(string Tag, int Index);

/// <summary>One game of a collection (the group's main version): its release date, sort name and backdrops in order.</summary>
public sealed record CollectionGameBackdrops(DateTime? Released, string SortName, IReadOnlyList<CollectionBackdrop> Backdrops);

/// <summary>The settings that decide the backdrop list of a collection page (the ArtBackdropsCollection* values).</summary>
public sealed record CollectionBackdropSettings(CollectionBackdropSource Source, CollectionImageCount OwnImages, ArtOrderMode OwnImageOrder, CollectionImageCount GameImages,
    ArtOrderMode GameImageOrder, CollectionGameOrder GameOrder, bool RandomStart)
{
    public static CollectionBackdropSettings Of(PluginConfiguration c) => new(c.ArtBackdropsCollectionSource, c.ArtBackdropsCollectionOwnImages, c.ArtBackdropsCollectionOwnImageOrder,
        c.ArtBackdropsCollectionGameImages, c.ArtBackdropsCollectionGameImageOrder, c.ArtBackdropsCollectionGameOrder, c.ArtBackdropsCollectionRandomStart);
}

/// <summary>
/// The finished backdrop list of a collection page, built once per visit as ArtworkPlus builds the list of a game collection (BackdropsController.BuildGameCollectionImages):
/// an own block (Single = the first picture, Multiple = all in the own image order), then one block per game in the game order; Random Start rotates the games of a
/// sorted order; Mixed = everything flat and shuffled as a whole before the cut (the page shuffles it every round); at most <see cref="Max"/> entries, cut after ordering. Pure, no file access.
/// </summary>
public static class CollectionBackdrops
{
    /// <summary>The most entries of a list.</summary>
    public const int Max = 100;

    /// <summary>The list for <paramref name="settings"/> from the own pictures and the games (any order); <paramref name="random"/> decides every shuffle, draw and rotation.</summary>
    public static List<CollectionBackdrop> Compose(CollectionBackdropSettings settings, IReadOnlyList<CollectionBackdrop> own, IReadOnlyList<CollectionGameBackdrops> games, Random random)
    {
        var mixed = settings.Source == CollectionBackdropSource.Mixed;
        List<CollectionBackdrop> Block(IReadOnlyList<CollectionBackdrop> list, CollectionImageCount count, ArtOrderMode order) =>
            list.Count == 0 ? new List<CollectionBackdrop>() : count != CollectionImageCount.Multiple ? new List<CollectionBackdrop> { list[0] } : mixed ? list.ToList() : OrderList(list, order, random);

        var result = new List<CollectionBackdrop>();
        if (settings.Source != CollectionBackdropSource.Games)
        {
            result.AddRange(Block(own, settings.OwnImages, settings.OwnImageOrder));
        }

        if (settings.Source != CollectionBackdropSource.Own)
        {
            // Release date as the Bridge sorts games (a game without a date last, a tie by sort name); Shuffle and Random are drawn from the ascending order.
            var sorted = games.OrderBy(g => g.Released is null);
            var ordered = (settings.GameOrder == CollectionGameOrder.ReleaseDescending ? sorted.ThenByDescending(g => g.Released) : sorted.ThenBy(g => g.Released))
                .ThenBy(g => g.SortName, StringComparer.OrdinalIgnoreCase).ToList();
            var blocks = ordered.Select(g => Block(g.Backdrops, settings.GameImages, settings.GameImageOrder)).Where(b => b.Count > 0).ToList();
            if (!mixed)
            {
                blocks = settings.GameOrder switch
                {
                    CollectionGameOrder.Shuffle => blocks.OrderBy(_ => random.Next()).ToList(),
                    CollectionGameOrder.Random => blocks.Count == 0 ? blocks : Enumerable.Range(0, blocks.Count).Select(_ => blocks[random.Next(blocks.Count)]).ToList(),
                    _ => settings.RandomStart ? Rotate(blocks, random) : blocks
                };
            }

            foreach (var block in blocks)
            {
                result.AddRange(block);
            }
        }

        // Mixed: the whole list is shuffled first and only then cut, so every picture has the same chance to stay (ArtworkPlus does the same; a cut in release order
        // would never show the later games of a long series). The page shuffles it again every round.
        if (mixed)
        {
            result = result.OrderBy(_ => random.Next()).ToList();
        }

        return result.Count > Max ? result.Take(Max).ToList() : result;
    }

    /// <summary>Sequential keeps the list, Shuffle permutes it, Random draws as many entries with repeats.</summary>
    public static List<CollectionBackdrop> OrderList(IReadOnlyList<CollectionBackdrop> list, ArtOrderMode order, Random random) => order switch
    {
        ArtOrderMode.Shuffle => list.OrderBy(_ => random.Next()).ToList(),
        ArtOrderMode.Random => Enumerable.Range(0, list.Count).Select(_ => list[random.Next(list.Count)]).ToList(),
        _ => list.ToList()
    };

    /// <summary>A copy of the list rotated by a random offset from 0 to count - 1, so the start is uniform over all entries including the first (a list of fewer than two entries unchanged).</summary>
    public static List<List<CollectionBackdrop>> Rotate(List<List<CollectionBackdrop>> blocks, Random random)
    {
        if (blocks.Count < 2)
        {
            return blocks;
        }

        var offset = random.Next(0, blocks.Count);
        return blocks.Skip(offset).Concat(blocks.Take(offset)).ToList();
    }
}

/// <summary>
/// The files of a collection page by their tag, built once per collection and game data and reused for 10 seconds, so a request with a tag that matches nothing (the image
/// routes are anonymous) costs one dictionary lookup and no file-system call. The first file with a tag stays when several carry it (own pictures first, then the games in the
/// order given). A load that fails gives what was read for the 10 seconds. Thread safe: parallel requests share one load.
/// </summary>
public sealed class CollectionTagIndex
{
    /// <summary>How long a map is reused (10 seconds, as the file listings it is built from).</summary>
    public const long ReuseMilliseconds = 10000;

    private sealed record Entry(object Data, long At, Lazy<Dictionary<string, string>> Map);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    /// <summary>The clock in milliseconds (replaceable).</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    /// <summary>The path of the file with <paramref name="tag"/> among <paramref name="load"/>'s files of the collection <paramref name="collection"/> as <paramref name="data"/> shows it; null when none.</summary>
    public string? Find(Guid collection, object data, string tag, Func<IEnumerable<(string Path, string Tag)>> load)
    {
        Entry entry;
        lock (_gate)
        {
            var now = Clock();
            if (!_entries.TryGetValue(collection, out var known) || !ReferenceEquals(known.Data, data) || now - known.At > ReuseMilliseconds)
            {
                if (_entries.Count > 2000)
                {
                    foreach (var stale in _entries.Where(e => now - e.Value.At > ReuseMilliseconds).Select(e => e.Key).ToList())
                    {
                        _entries.Remove(stale);
                    }
                }

                known = new Entry(data, now, new Lazy<Dictionary<string, string>>(() => Build(load), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
                _entries[collection] = known;
            }

            entry = known;
        }

        return entry.Map.Value.TryGetValue(tag, out var path) ? path : null;
    }

    private static Dictionary<string, string> Build(Func<IEnumerable<(string Path, string Tag)>> load)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var (path, tag) in load())
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    map.TryAdd(tag, path);
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            // an unreadable folder gives what was read so far
        }

        return map;
    }
}
