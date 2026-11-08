using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>What a virtual id stands for.</summary>
public enum GameHitKind
{
    Game,
    Collection,
    Genre,
    Person,
    Studio,
    Filter
}

/// <summary>
/// One virtual id: a game (Game = the Playnite entry of the id, Group and Version as GameGroups.Find), a collection folder, or a genre,
/// developer (Person), publisher (Studio) or filter entry with its Name (a filter entry's name without prefix, as GameQuery.FilterName).
/// </summary>
public sealed record GameHit(GameHitKind Kind, GameEntry? Game = null, GameGroup? Group = null, GameEntry? Version = null, GameCollection? Collection = null,
    string? Name = null);

/// <summary>
/// The game data of one copy of Playnite's library and one settings object: the visible games, their groups, the collections and every
/// virtual id; built once, never changed. Pure, no request or file access.
/// </summary>
public sealed class GameData
{
    public static readonly GameData Empty = new(string.Empty, null, false, 0, new List<GameEntry>(), new List<GameGroup>(), new List<GameCollection>(),
        new Dictionary<Guid, GameHit>());

    private GameData(string set, PluginConfiguration? config, bool showHidden, int generation, IReadOnlyList<GameEntry> games, IReadOnlyList<GameGroup> groups,
        IReadOnlyList<GameCollection> collections, IReadOnlyDictionary<Guid, GameHit> byId)
    {
        Set = set;
        Config = config;
        ShowHidden = showHidden;
        Generation = generation;
        Games = games;
        Groups = groups;
        Collections = collections;
        ById = byId;
        _detail = new Lazy<IReadOnlyDictionary<string, GameGroup>>(() => config is null
            ? new Dictionary<string, GameGroup>()
            : GameGroups.DetailGroups(games, config)
                .SelectMany(g => g.Versions.Select(v => (v.Id, g)).Concat((g.Twins?.Keys ?? Enumerable.Empty<string>()).Select(t => (Id: t, g))))
                .GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First().g), LazyThreadSafetyMode.ExecutionAndPublication);
        _shortIds = new Lazy<HashSet<string>>(() => games.Select(g => GameExtras.ShortIdOf(g.Id)).ToHashSet(StringComparer.Ordinal), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private readonly Lazy<IReadOnlyDictionary<string, GameGroup>> _detail;
    private readonly Lazy<HashSet<string>> _shortIds;

    /// <summary>The group of a game as the game page's version list builds it (<see cref="GameGroups.DetailGroups"/>), at least as wide as the library's grouping; null for an unknown id.</summary>
    public GameGroup? DetailGroupOf(string gameId) => _detail.Value.TryGetValue(gameId, out var group) ? group : null;

    /// <summary>True when <paramref name="shortId"/> (GameExtras.ShortIdOf) is the short id of a game of <see cref="Games"/> (twins included): the owner test of a game folder, built once per data.</summary>
    public bool HasShortId(string shortId) => _shortIds.Value.Contains(shortId);

    /// <summary>The copy folder (PlayniteSnapshot.CurrentSet) the games were read for.</summary>
    public string Set { get; }

    public PluginConfiguration? Config { get; }

    public bool ShowHidden { get; }

    /// <summary>The copy generation (PlayniteSnapshot.Generation) read before the copy folder.</summary>
    public int Generation { get; }

    public IReadOnlyList<GameEntry> Games { get; }

    public IReadOnlyList<GameGroup> Groups { get; }

    public IReadOnlyList<GameCollection> Collections { get; }

    public IReadOnlyDictionary<Guid, GameHit> ById { get; }

    /// <summary>
    /// Groups and collections of <paramref name="games"/> and the hit of every id: each game and twin, each collection, then the filter entries
    /// of features, completion statuses and sources, then genres, developers and publishers in game order (the first hit of an id stays).
    /// </summary>
    public static GameData Build(string set, PluginConfiguration config, int generation, IReadOnlyList<GameEntry> games)
    {
        var groups = GameGroups.Group(games, config);
        var collections = GameSuggestions.Collections(groups, config.CollectionPosterOrder);
        var byId = new Dictionary<Guid, GameHit>();
        var entries = games.ToDictionary(g => g.Id);
        foreach (var group in groups)
        {
            foreach (var version in group.Versions)
            {
                byId.TryAdd(Guid.Parse(version.Id), new GameHit(GameHitKind.Game, version, group, version));
            }

            foreach (var (twin, kept) in group.Twins ?? new Dictionary<string, string>())
            {
                byId.TryAdd(Guid.Parse(twin), new GameHit(GameHitKind.Game, entries[twin], group, group.Versions.First(v => v.Id == kept)));
            }
        }

        foreach (var collection in collections)
        {
            byId.TryAdd(collection.Id, new GameHit(GameHitKind.Collection, Collection: collection));
        }

        // The filter entries as GameQuery.FilterName finds them.
        foreach (var value in games.SelectMany(g => g.Features.Select(GameQuery.FeatureFilter)
                .Concat(g.CompletionStatus is string status ? new[] { GameQuery.StatusFilter(status) } : Array.Empty<string>())
                .Append(GameQuery.SourceFilter(GameGroups.StoreLabel(g))))
            .Distinct(StringComparer.Ordinal))
        {
            byId.TryAdd(VirtualIds.Filter(value), new GameHit(GameHitKind.Filter, Name: value.Substring(value.IndexOf(':', GameQuery.PseudoPrefix.Length) + 1)));
        }

        foreach (var game in games)
        {
            foreach (var (refs, kind) in new[] { (game.GenreRefs, GameHitKind.Genre), (game.DeveloperRefs, GameHitKind.Person), (game.PublisherRefs, GameHitKind.Studio) })
            {
                foreach (var named in refs)
                {
                    byId.TryAdd(named.Id, new GameHit(kind, Name: named.Name));
                }
            }
        }

        return new GameData(set, config, config.ShowHidden, generation, games, groups, collections, byId);
    }

    /// <summary>The hit of an id (<see cref="OwnIds.Hit"/>: exact map, and not an id Jellyfin knows); null for any other id.</summary>
    public GameHit? Find(Guid id) => OwnIds.Hit(this, id);

    /// <summary>The group of a game of <see cref="Games"/>.</summary>
    public GameGroup GroupOf(GameEntry game) => ById[Guid.Parse(game.Id)].Group!;

    /// <summary>True while it was built for this copy folder, this settings object and this copy generation.</summary>
    public bool IsFor(string set, PluginConfiguration config, int generation) => Set == set && IsFor(config, generation);

    /// <summary>True while it was built for this settings object and this copy generation (no copy folder needed, so no file access).</summary>
    public bool IsFor(PluginConfiguration config, int generation) =>
        ReferenceEquals(Config, config) && ShowHidden == config.ShowHidden && Generation == generation;
}

/// <summary>
/// The one shared <see cref="GameData"/>, replaced as a whole when the copy folder, the settings object or the copy generation changes; built
/// under a lock, so requests never build it at the same time.
/// </summary>
public sealed class GameDataCache
{
    /// <summary>How long a failed read is remembered: the reader's own pause after a read error (10 s).</summary>
    public const long FailureHoldMilliseconds = 10000;

    private readonly object _buildLock = new();
    private GameData? _data;
    private int _builds;
    private Failure? _failure;
    // Bumped when the stored data must not be kept (a game of it turned out to be a real item of Jellyfin); a build that started before is read again.
    private int _epoch;
    private int _building;
    // Clock() value after which data built from an incomplete check against Jellyfin (OwnIds.CheckAll) is built again; 0 = complete.
    private long _incompleteUntil;

    /// <summary>How long data built from an incomplete check against Jellyfin is used before it is built again (10 s, as a failed read).</summary>
    public const long IncompleteHoldMilliseconds = 10000;

    /// <summary>The clock in milliseconds (replaceable so the hold can end without waiting).</summary>
    public Func<long> Clock { get; init; } = () => Environment.TickCount64;

    /// <summary>Number of builds so far.</summary>
    public int Builds => _builds;

    /// <summary>The stored game data as it is, possibly for an older copy or settings object; null before the first build. Never reads or builds.</summary>
    public GameData? Current => Volatile.Read(ref _data);

    /// <summary>
    /// The game data of <paramref name="set"/>, <paramref name="config"/> and <paramref name="generation"/>, built from <paramref name="read"/> when
    /// the stored one is for another copy, settings object or generation. <paramref name="read"/> gives null when the games cannot be read: then
    /// the answer is <see cref="GameData.Empty"/>, nothing is stored, and for <see cref="FailureHoldMilliseconds"/> the same settings object and
    /// generation get <see cref="GameData.Empty"/> without a new read; another settings object or a new generation reads at once.
    /// </summary>
    public GameData Get(string set, PluginConfiguration config, int generation, Func<IReadOnlyList<GameEntry>?> read)
    {
        var data = Volatile.Read(ref _data);
        if (data is not null && data.IsFor(set, config, generation) && !IncompleteDue())
        {
            return data;
        }

        lock (_buildLock)
        {
            data = _data;
            if (data is not null && data.IsFor(set, config, generation) && !IncompleteDue())
            {
                return data;
            }

            if (FailureHeld(config, generation))
            {
                return GameData.Empty;
            }

            Volatile.Write(ref _building, 1);
            try
            {
                // Read again when a game of the data turned out to be a real item meanwhile (Invalidate): at most three times, the known conflicts are then left out.
                for (var attempt = 0; ; attempt++)
                {
                    var epoch = Volatile.Read(ref _epoch);
                    Volatile.Write(ref _incompleteUntil, 0);
                    var games = read();
                    if (games is null)
                    {
                        Volatile.Write(ref _failure, new Failure(config, config.ShowHidden, generation, Clock() + FailureHoldMilliseconds));
                        return GameData.Empty;
                    }

                    data = GameData.Build(set, config, generation, games);
                    Interlocked.Increment(ref _builds);
                    if (epoch != Volatile.Read(ref _epoch) && attempt < 2)
                    {
                        continue;
                    }

                    Volatile.Write(ref _data, data);
                    return data;
                }
            }
            finally
            {
                Volatile.Write(ref _building, 0);
            }
        }
    }

    /// <summary>Called by the reader while it builds: the games were not all checked against Jellyfin (OwnIds.CheckAll), so the data is built again after <see cref="IncompleteHoldMilliseconds"/>.</summary>
    public void NoteIncomplete() => Volatile.Write(ref _incompleteUntil, Clock() + IncompleteHoldMilliseconds);

    private bool IncompleteDue() => Volatile.Read(ref _incompleteUntil) is long until and not 0 && Clock() >= until;

    /// <summary>Drops the stored data: the next request builds it again (forces a rebuild).</summary>
    public void Invalidate()
    {
        Interlocked.Increment(ref _epoch);
        Volatile.Write(ref _data, null);
    }

    /// <summary>
    /// For an id just found to be a real item of Jellyfin (OwnIds.ConflictNoted): when the stored data lists a game of that id, or a build is running that may
    /// read it, the data is dropped, so lists, tiles and lookups agree (the build leaves the game out, OwnIds.CheckAll). Takes no lock. Returns whether it dropped.
    /// </summary>
    public bool InvalidateGame(Guid id)
    {
        var stored = Volatile.Read(ref _data);
        var listed = stored is not null && stored.ById.TryGetValue(id, out var hit) && hit.Kind == GameHitKind.Game;
        if (!listed && Volatile.Read(ref _building) == 0)
        {
            return false;
        }

        Invalidate();
        return true;
    }

    /// <summary>
    /// The hit of an id and the game data it was found in. The id is first looked up in the stored data as it is (no read, no file access): an id
    /// unknown there gets null at once while the stored data is for <paramref name="config"/> and <paramref name="generation"/>, so other libraries
    /// pay nothing. When the stored data is for another settings object, ShowHidden or an older copy (a sync since), or there is
    /// none yet, and for every known id, <paramref name="current"/> gives the current data and the id is looked up there. While a failed read
    /// of the same settings object and generation is held (<see cref="Get"/>), an id unknown in the stored data gets null without
    /// <paramref name="current"/> (no copy listing, no read).
    /// </summary>
    public GameHit? Find(Guid id, PluginConfiguration config, int generation, Func<GameData> current, out GameData data)
    {
        var stored = Current;
        if (stored?.Find(id) is null && FailureHeld(config, generation))
        {
            data = stored ?? GameData.Empty;
            return null;
        }

        data = stored ?? current();
        if (data.Find(id) is null && (stored is null || stored.IsFor(config, generation)))
        {
            return null;
        }

        data = current();
        return data.Find(id);
    }

    // True while the last failed read was for this settings object, its ShowHidden and this generation, and its hold has not ended.
    private bool FailureHeld(PluginConfiguration config, int generation) =>
        Volatile.Read(ref _failure) is { } failure && ReferenceEquals(failure.Config, config) && failure.ShowHidden == config.ShowHidden
            && failure.Generation == generation && Clock() < failure.Until;

    private sealed record Failure(PluginConfiguration Config, bool ShowHidden, int Generation, long Until);
}

/// <summary>
/// The game data a parental control policy leaves of one base <see cref="GameData"/> (setting ApplyParentalControls): built once per policy key with
/// <see cref="GameData.Build"/> over the allowed games, so groups, collections and ids follow from them; all of them dropped when the base changes.
/// </summary>
public sealed class ParentalDataCache
{
    private readonly object _lock = new();
    private GameData? _base;
    private Dictionary<string, GameData> _byKey = new(StringComparer.Ordinal);
    private int _builds;

    /// <summary>Number of builds so far.</summary>
    public int Builds => _builds;

    /// <summary>The game data of the games of <paramref name="baseData"/> that <paramref name="allowed"/> keeps, for the policy key <paramref name="key"/>.</summary>
    public GameData Get(GameData baseData, string key, Func<GameEntry, bool> allowed)
    {
        if (baseData.Config is null)
        {
            return baseData;
        }

        lock (_lock)
        {
            if (!ReferenceEquals(_base, baseData))
            {
                _base = baseData;
                _byKey = new Dictionary<string, GameData>(StringComparer.Ordinal);
            }

            if (!_byKey.TryGetValue(key, out var data))
            {
                data = GameData.Build(baseData.Set, baseData.Config, baseData.Generation, baseData.Games.Where(allowed).ToList());
                _builds++;
                _byKey[key] = data;
            }

            return data;
        }
    }
}
