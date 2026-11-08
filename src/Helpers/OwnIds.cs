using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The one gate for every decision "this id is mine". An interceptor of Jellyfin's routes answers only for an id it has
/// PROVED to be its own: found again in a map built from the current state (game data, extras index, Steam index) or recomputed from the files
/// behind it (screenshot item and row ids), and not known to Jellyfin as a real item. Everything else is null, so Jellyfin answers. No decoder
/// is trusted on its marker byte alone; <see cref="Screenshots.Decode"/> and the maps are called from here only (the
/// filter and the controllers must not call them directly).
/// </summary>
public static class OwnIds
{
    /// <summary>How long Jellyfin's answer for one id (known or not) is kept before the library manager is asked again.</summary>
    public const int MemoSeconds = 60;

    /// <summary>How long the failure of a question to Jellyfin is kept (the id stays ours meanwhile).</summary>
    public const int FailureMemoSeconds = 5;

    /// <summary>How long a batched check (<see cref="Check"/>) stays valid; <see cref="NoteAdded"/> marks a later conflict at once.</summary>
    public const int SweepSeconds = 600;

    /// <summary>Ids per batched query to Jellyfin's library manager.</summary>
    public const int BatchSize = 500;

    private const int MemoLimit = 200_000;

    private static readonly ConcurrentDictionary<Guid, byte> Conflicts = new();
    private static readonly ConcurrentDictionary<Guid, (bool Known, DateTime Until)> Memo = new();
    private static int _probeFailureLogged;
    private static readonly ConcurrentDictionary<Type, bool> Controllers = new();
    private static int _controllerRecognised;
    private static int _controllerUnrecognised;
    private static int _controllerWarned;

    /// <summary>Unrecognised actions with a Jellyfin controller name that are counted before the route override is reported as silently off (see <see cref="NoteUnrecognised"/>).</summary>
    public const int UnrecognisedLimit = 20;

    /// <summary>True when Jellyfin's library manager knows the id as an item (ILibraryManager.GetItemById); set by the filter, none = nothing is known.</summary>
    public static Func<Guid, bool>? KnownToJellyfin { get; set; }

    public static ILogger? Log { get; set; }

    /// <summary>Called once for each id newly marked as a real item of Jellyfin (the filter drops a game of that id from its stored game data, so lists agree with the lookups).</summary>
    public static Action<Guid>? ConflictNoted { get; set; }

    /// <summary>The clock of the memo (replaceable).</summary>
    public static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Ids found to be real items of Jellyfin, never answered.</summary>
    public static int ConflictCount => Conflicts.Count;

    /// <summary>Forgets every memo and conflict.</summary>
    public static void Reset()
    {
        Conflicts.Clear();
        Memo.Clear();
        Controllers.Clear();
        Volatile.Write(ref _probeFailureLogged, 0);
        Volatile.Write(ref _controllerRecognised, 0);
        Volatile.Write(ref _controllerUnrecognised, 0);
        Volatile.Write(ref _controllerWarned, 0);
    }

    /// <summary>
    /// True when Jellyfin knows <paramref name="id"/> as a real item: then the plugin never answers for it, whatever it would have been. Only ever called
    /// for an id that was proved ours first, so a foreign id never costs a query; the answer is kept <see cref="MemoSeconds"/> (a batched check keeps it
    /// longer). Guid.Empty is skipped (GetItemById throws for it); the games library id never reaches here (it is no id of ours).
    /// </summary>
    public static bool Refused(Guid id)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        if (Conflicts.ContainsKey(id))
        {
            return true;
        }

        var now = Clock();
        if (Memo.TryGetValue(id, out var memo) && now < memo.Until)
        {
            return memo.Known;
        }

        var probe = KnownToJellyfin;
        if (probe is null)
        {
            return false;
        }

        bool known;
        try
        {
            known = probe(id);
        }
        catch (Exception ex)
        {
            // Not provable either way: the id stays ours (it was proved ours), one warning, asked again after the memo.
            if (Interlocked.Exchange(ref _probeFailureLogged, 1) == 0)
            {
                Log?.LogWarning("PlayniteBridge: Jellyfin's library manager could not be asked whether an id is a real item: {Message}", ex.Message);
            }

            // Deliberately open (the id was proved ours; a failing database must not blank the library) and short: asked again after FailureMemoSeconds.
            Remember(id, false, FailureMemoSeconds);
            return false;
        }

        Remember(id, known, MemoSeconds);
        if (known)
        {
            Conflict(id, "Jellyfin knows it as an item");
        }

        return known;
    }

    /// <summary>Marks <paramref name="id"/> as a real item of Jellyfin for good (until the next start): one warning, never answered.</summary>
    public static void Conflict(Guid id, string why)
    {
        if (Conflicts.TryAdd(id, 0))
        {
            Log?.LogWarning("PlayniteBridge: the id {Id} of a virtual item is a real item of Jellyfin ({Why}); the plugin never answers for it", id, why);
            try
            {
                ConflictNoted?.Invoke(id);
            }
            catch (Exception ex)
            {
                Log?.LogWarning("PlayniteBridge: the stored game data could not be dropped after the conflict of {Id}: {Message}", id, ex.Message);
            }
        }
    }

    /// <summary>True when <paramref name="id"/> was found to be a real item of Jellyfin (no question to Jellyfin).</summary>
    public static bool IsConflict(Guid id) => Conflicts.ContainsKey(id);

    /// <summary>The ids of a batched check that are real items of Jellyfin, and whether every id was checked (a failing query or the time budget stops it).</summary>
    public readonly record struct CheckResult(HashSet<Guid> Known, bool Complete);

    /// <summary>As <see cref="CheckAll"/> without a time budget; the known ids only.</summary>
    public static HashSet<Guid> Check(IEnumerable<Guid> ids, Func<IReadOnlyList<Guid>, IEnumerable<Guid>> existing) =>
        CheckAll(ids, existing, TimeSpan.MaxValue).Known;

    /// <summary>
    /// A batched check of <paramref name="ids"/> against Jellyfin: <paramref name="existing"/> gives, for up to <see cref="BatchSize"/> ids, those that are
    /// items of Jellyfin. Those are conflicts (returned and refused from now on, ids found earlier included); the others are remembered as free for
    /// <see cref="SweepSeconds"/>, so the requests that follow cost no query. A failing query is one warning and stops the check; so does
    /// <paramref name="budget"/> (measured by <see cref="Clock"/>, tested between two queries): the ids not reached are left to <see cref="Refused"/> and
    /// <see cref="CheckResult.Complete"/> is false, so the caller asks again later.
    /// </summary>
    public static CheckResult CheckAll(IEnumerable<Guid> ids, Func<IReadOnlyList<Guid>, IEnumerable<Guid>> existing, TimeSpan budget)
    {
        var known = new HashSet<Guid>();
        var checkedCount = 0;
        var complete = true;
        var start = Clock();
        var all = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        foreach (var id in all)
        {
            if (Conflicts.ContainsKey(id))
            {
                known.Add(id);
            }
        }

        foreach (var batch in all.Chunk(BatchSize))
        {
            if (Clock() - start > budget)
            {
                Log?.LogWarning("PlayniteBridge: the batched check of ids against Jellyfin ran out of time after {Count} ids; the rest is checked one by one and again at the next build", checkedCount);
                complete = false;
                break;
            }

            try
            {
                var set = existing(batch).ToHashSet();
                foreach (var id in batch)
                {
                    if (set.Contains(id))
                    {
                        known.Add(id);
                        Conflict(id, "Jellyfin knows it as an item");
                    }
                    else
                    {
                        Remember(id, false, SweepSeconds);
                    }
                }

                checkedCount += batch.Length;
            }
            catch (Exception ex)
            {
                Log?.LogWarning("PlayniteBridge: the batched check of ids against Jellyfin failed: {Message}", ex.Message);
                complete = false;
                break;
            }
        }

        Log?.LogInformation("PlayniteBridge: {Count} ids checked against Jellyfin's library, {Conflicts} conflicts", checkedCount, known.Count);
        return new CheckResult(known, complete);
    }

    /// <summary>
    /// For Jellyfin's ItemAdded: a real item with an id of ours (a game, collection, genre, person, studio or filter of <paramref name="data"/>, or a
    /// screenshot id of one of its games) is a conflict at once. Extras and Steam ids are left to <see cref="Refused"/> (their maps are not kept here).
    /// </summary>
    public static void NoteAdded(Guid id, GameData? data)
    {
        if (id == Guid.Empty || data is null)
        {
            return;
        }

        if (data.ById.ContainsKey(id))
        {
            Conflict(id, "added to Jellyfin's library");
        }
        else if (Screenshots.Decode(id) is { } key && data.HasShortId(key.ShortId))
        {
            Conflict(id, "added to Jellyfin's library");
        }
        else if (Wallpapers.Decode(id) is { } wallpaper && data.HasShortId(wallpaper.ShortId))
        {
            Conflict(id, "added to Jellyfin's library");
        }
        else if (Documents.Decode(id) is { } document && data.HasShortId(document.ShortId))
        {
            Conflict(id, "added to Jellyfin's library");
        }
    }

    private static void Remember(Guid id, bool known, int seconds)
    {
        if (Memo.Count > MemoLimit)
        {
            Memo.Clear();
        }

        Memo[id] = (known, Clock().AddSeconds(seconds));
    }

    /// <summary>The hit of an id in <paramref name="data"/> (exact map); null for any other id and for an id Jellyfin knows.</summary>
    public static GameHit? Hit(GameData data, Guid id) =>
        data.ById.TryGetValue(id, out var hit) && !Refused(id) ? hit : null;

    /// <summary>
    /// The game of <paramref name="games"/> with the id <paramref name="id"/> (exact id, as Playnite writes it); null for any other id and for an id Jellyfin knows.
    /// The one way for code outside the gate (the controllers) to find a game by a request's id.
    /// </summary>
    public static GameEntry? Game(IEnumerable<GameEntry> games, Guid id)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        var text = id.ToString();
        return games.FirstOrDefault(g => string.Equals(g.Id, text, StringComparison.Ordinal)) is { } game && !Refused(id) ? game : null;
    }

    /// <summary>
    /// Cheap test before an extras index is built or read: false for the ids no extra can have (<see cref="VirtualIds.IsDerived"/>). A negative filter
    /// only; <see cref="Extra"/> proves.
    /// </summary>
    public static bool MayBeExtra(Guid id) => id != Guid.Empty && VirtualIds.IsDerived(id);

    /// <summary>The local extra of an id in <paramref name="index"/> (exact map); null for any other id and for an id Jellyfin knows.</summary>
    public static GameExtra? Extra(Guid id, ExtrasIndex index) =>
        id != Guid.Empty && index.ById.TryGetValue(id, out var extra) && !Refused(id) ? extra : null;

    /// <summary>The Steam Special Feature of an id (exact map of the stored entries, read only for an id of the derived form); null for any other id and for an id Jellyfin knows.</summary>
    public static SteamVideoItem? Steam(Guid id) =>
        MayBeExtra(id) && SteamVideos.Find(id) is SteamVideoItem item && !Refused(id) ? item : null;

    /// <summary>
    /// The screenshot an item id names. The id must decode as an item id (no disk, no lookup before that), <paramref name="mayShow"/> must accept its version's
    /// short id (the version is a game the asking user may see), <paramref name="areaOf"/> (the version's listing) must hold the file, and the id
    /// RECOMPUTED from the file (short id, slot, seed, file hash) must be the asked one; else null.
    /// </summary>
    public static (ScreenshotFile File, ScreenshotKey Key)? Screenshot(Guid id, Func<string, bool> mayShow, Func<string, ScreenshotArea?> areaOf)
    {
        if (id == Guid.Empty || Screenshots.Decode(id) is not { IsRow: false } key || !mayShow(key.ShortId))
        {
            return null;
        }

        if (areaOf(key.ShortId)?.Find(key.Slot, key.Hash) is not { } file || Screenshots.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash) != id || Refused(id))
        {
            return null;
        }

        return (file, key);
    }

    /// <summary>
    /// The document an item id names: as <see cref="Screenshot"/>, the id must decode as a document id, <paramref name="mayShow"/> must accept its version's short id,
    /// <paramref name="areaOf"/> must hold the file and the id RECOMPUTED from the file must be the asked one; else null.
    /// </summary>
    public static (DocumentFile File, DocumentKey Key)? Document(Guid id, Func<string, bool> mayShow, Func<string, DocumentArea?> areaOf)
    {
        if (id == Guid.Empty || Documents.Decode(id) is not { } key || !mayShow(key.ShortId))
        {
            return null;
        }

        if (areaOf(key.ShortId)?.Find(key.Slot, key.Hash) is not { } file || Documents.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash) != id || Refused(id))
        {
            return null;
        }

        return (file, key);
    }

    /// <summary>
    /// The row a parent id names (the ParentId of the screenshots of one row): it must decode as a row id (check bytes), be the id RECOMPUTED from its fields,
    /// <paramref name="mayShow"/> must accept its version, and Jellyfin must not know it; else null. That the row holds files is the caller's last test.
    /// </summary>
    public static ScreenshotKey? ScreenshotRow(Guid id, Func<string, bool> mayShow)
    {
        if (id == Guid.Empty || Screenshots.Decode(id) is not { IsRow: true } key || Screenshots.RowId(key.ShortId, key.Slot, key.Seed) != id
            || !mayShow(key.ShortId) || Refused(id))
        {
            return null;
        }

        return key;
    }

    /// <summary>The wallpaper or artwork picture an item id names: as <see cref="Screenshot"/>, with the wallpapers id marker and the wallpapers listing.</summary>
    public static (WallpaperFile File, WallpaperKey Key)? Wallpaper(Guid id, Func<string, bool> mayShow, Func<string, WallpaperArea?> areaOf)
    {
        if (id == Guid.Empty || Wallpapers.Decode(id) is not { IsRow: false } key || !mayShow(key.ShortId))
        {
            return null;
        }

        if (areaOf(key.ShortId)?.Find(key.Slot, key.Hash) is not { } file || Wallpapers.ItemId(key.ShortId, file.Slot, key.Seed, file.Hash) != id || Refused(id))
        {
            return null;
        }

        return (file, key);
    }

    /// <summary>The row a parent id names (the ParentId of the pictures of one wallpapers row): as <see cref="ScreenshotRow"/>.</summary>
    public static WallpaperKey? WallpaperRow(Guid id, Func<string, bool> mayShow)
    {
        if (id == Guid.Empty || Wallpapers.Decode(id) is not { IsRow: true } key || Wallpapers.RowId(key.ShortId, key.Slot, key.Seed) != id
            || !mayShow(key.ShortId) || Refused(id))
        {
            return null;
        }

        return key;
    }

    /// <summary>True when <paramref name="parentId"/> is the games library <paramref name="library"/> (itself proved by <c>VirtualItemsFilter.GamesLibrary</c>).</summary>
    public static bool IsGamesLibrary(Guid? parentId, Guid? library) => parentId is Guid p && p != Guid.Empty && library == p;

    /// <summary>Controller types judged so far (<see cref="IsJellyfinController"/> reads the assembly name once per type, not once per action).</summary>
    public static int ControllerTypesJudged => Controllers.Count;

    /// <summary>True when <paramref name="type"/> is a Jellyfin API controller (assembly Jellyfin.Api, namespace Jellyfin.Api.Controllers): the route override matches by name only after this. Judged once per type.</summary>
    public static bool IsJellyfinController(Type? type)
    {
        if (type is null)
        {
            return false;
        }

        var yes = Controllers.GetOrAdd(type, t =>
            t.Assembly.GetName().Name == "Jellyfin.Api" && t.Namespace is not null && t.Namespace.StartsWith("Jellyfin.Api.Controllers", StringComparison.Ordinal));
        if (yes)
        {
            Volatile.Write(ref _controllerRecognised, 1);
        }

        return yes;
    }

    /// <summary>
    /// For an action that carries the name of a Jellyfin controller (Items, UserLibrary, ...) but is none: when this happens <see cref="UnrecognisedLimit"/> times
    /// and no Jellyfin controller was ever recognised, the route override is off (Jellyfin.Api under another assembly name or namespace), so ONE warning says so.
    /// </summary>
    public static void NoteUnrecognised(string controllerName)
    {
        if (Volatile.Read(ref _controllerRecognised) == 1 || Interlocked.Increment(ref _controllerUnrecognised) < UnrecognisedLimit)
        {
            return;
        }

        if (Interlocked.Exchange(ref _controllerWarned, 1) == 0)
        {
            Log?.LogWarning("PlayniteBridge: no controller of Jellyfin's API was recognised in {Count} requests (last: {Controller}); the games library is NOT shown. Jellyfin.Api may have another assembly name or namespace in this Jellyfin version", UnrecognisedLimit, controllerName);
        }
    }
}
