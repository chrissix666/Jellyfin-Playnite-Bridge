using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// BlurHashes of image files, kept in memory per file and state (path + ImageCache.Tag). <see cref="Get"/> only reads and never waits: a
/// missing entry puts the file once into a queue that one background worker computes, one file after the other. An empty result or an error
/// is kept as "no hash" (one warning) until the file changes. With a store every result is also put into it (BlurHashStore.Set) with the file's
/// time and length before it was computed, so it outlives a restart.
/// </summary>
public sealed class BlurHashCache
{
    // The extensions ByContent hands to the computation as they are (formats of Jellyfin's SkiaEncoder).
    private static readonly HashSet<string> DirectExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };

    private readonly ConcurrentDictionary<string, Entry> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string Path, string Tag)> _queue = new();
    private readonly Func<string, string> _compute;
    private readonly Action<string, string> _warn;
    private readonly Func<BlurHashStore>? _store;
    private int _working;
    private int _computed;

    /// <param name="compute">The hash of a file (IImageProcessor.GetImageBlurHash).</param>
    /// <param name="warn">Called once per file and state that gives no hash, with the path and the reason.</param>
    /// <param name="store">The store every result is put into; null = memory only.</param>
    public BlurHashCache(Func<string, string> compute, Action<string, string> warn, Func<BlurHashStore>? store = null)
    {
        _compute = compute;
        _warn = warn;
        _store = store;
    }

    /// <summary>
    /// <paramref name="compute"/> for files whose extension Jellyfin's encoder may not read (SkiaEncoder.GetImageBlurHash gives "" for one outside
    /// its formats, e.g. none or .jfif): a .jpg, .jpeg, .png, .gif, .webp or .bmp goes to it directly; another file whose first bytes show a known
    /// format (ImageCache.ContentExtension) is computed on a temporary copy &lt;tempFolder&gt;\blurhash-&lt;guid&gt;&lt;extension&gt;, deleted
    /// afterwards; any other file goes to it as it is.
    /// </summary>
    public static Func<string, string> ByContent(Func<string, string> compute, string tempFolder) => path =>
    {
        if (DirectExtensions.Contains(Path.GetExtension(path)) || ImageCache.ContentExtension(path) is not { } extension)
        {
            return compute(path);
        }

        Directory.CreateDirectory(tempFolder);
        var temp = Path.Combine(tempFolder, "blurhash-" + Guid.NewGuid().ToString("N") + extension);
        try
        {
            File.Copy(path, temp, false);
            return compute(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    };

    /// <summary>Number of files computed.</summary>
    public int Computed => _computed;

    /// <summary>Number of files waiting in the queue or being computed.</summary>
    public int Queued => _queued.Count;

    /// <summary>The hash of <paramref name="path"/> in the state <paramref name="tag"/>; null while there is none (then queued once) or the file gives none.</summary>
    public string? Get(string path, string tag)
    {
        if (_hashes.TryGetValue(path, out var entry) && entry.Tag == tag)
        {
            return entry.Hash.Length == 0 ? null : entry.Hash;
        }

        if (_queued.TryAdd(Key(path, tag), 0))
        {
            _queue.Enqueue((path, tag));
            if (Interlocked.CompareExchange(ref _working, 1, 0) == 0)
            {
                _ = Task.Run(Work);
            }
        }

        return null;
    }

    private static string Key(string path, string tag) => path + "|" + tag;

    // The one worker: empties the queue, then ends unless a file came in after the last look.
    private void Work()
    {
        while (true)
        {
            while (_queue.TryDequeue(out var item))
            {
                // The file's state before the computation: a change meanwhile makes the stored entry old, so it is computed again.
                var file = new FileInfo(item.Path);
                var exists = file.Exists;
                var (ticks, length) = exists ? (file.LastWriteTimeUtc.Ticks, file.Length) : (0L, 0L);
                string hash;
                try
                {
                    hash = _compute(item.Path) ?? string.Empty;
                    if (hash.Length == 0)
                    {
                        _warn(item.Path, "the image gives no BlurHash");
                    }
                }
                catch (Exception ex)
                {
                    hash = string.Empty;
                    _warn(item.Path, ex.Message);
                }

                Interlocked.Increment(ref _computed);
                _hashes[item.Path] = new Entry(item.Tag, hash);
                if (exists)
                {
                    _store?.Invoke().Set(item.Path, ticks, length, hash);
                }

                _queued.TryRemove(Key(item.Path, item.Tag), out _);
            }

            Volatile.Write(ref _working, 0);
            if (_queue.IsEmpty || Interlocked.CompareExchange(ref _working, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private sealed record Entry(string Tag, string Hash);
}

/// <summary>The state of the file an entry of a <see cref="FileStore{TEntry}"/> was made for: its ticks (LastWriteTimeUtc) and length.</summary>
public interface IFileState
{
    long Ticks { get; }

    long Length { get; }
}

/// <summary>
/// Entries per file kept in a JSON file of the plugin's data folder: full path (case ignored) -> an entry with the file's ticks and length when
/// it was made; an entry counts only while the file still has that time and length. <see cref="Set"/> writes the file one timer later (5 s after
/// the first change, as ImageCache's list of copies), <see cref="Save"/> at once; every write goes through one lock, so the timer and Sync Artwork
/// never write the same .tmp at the same time.
/// </summary>
public abstract class FileStore<TEntry>
    where TEntry : class, IFileState
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<string, TEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _saveLock = new();
    private readonly string _what;
    private Timer? _timer;
    private int _armed;

    /// <summary>A store read from <paramref name="file"/>: empty when the file is missing; empty with one warning (naming <paramref name="what"/> and <paramref name="again"/>) when it cannot be read.</summary>
    protected FileStore(string file, ILogger logger, string what, string again)
    {
        FilePath = file;
        Logger = logger;
        _what = what;
        try
        {
            if (file.Length > 0 && File.Exists(file))
            {
                foreach (var (path, entry) in JsonSerializer.Deserialize<Dictionary<string, TEntry>>(File.ReadAllText(file), Json) ?? throw new JsonException("empty file"))
                {
                    _entries[path] = entry;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            _entries.Clear();
            logger.LogWarning("PlayniteBridge: cannot read the {What} \"{File}\"; {Again}: {Message}", what, file, again, ex.Message);
        }
    }

    /// <summary>Number of files with an entry.</summary>
    public int Count => _entries.Count;

    /// <summary>The file of the store; empty = nothing read or written.</summary>
    protected string FilePath { get; }

    protected ILogger Logger { get; }

    /// <summary>True when the store has <paramref name="path"/> in the file's current time and length (<paramref name="entry"/> = its entry).</summary>
    protected bool TryGetCurrent(string path, out TEntry entry)
    {
        var info = new FileInfo(path);
        if (_entries.TryGetValue(path, out var found) && info.Exists && found.Ticks == info.LastWriteTimeUtc.Ticks && found.Length == info.Length)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    /// <summary>Puts <paramref name="entry"/> for <paramref name="path"/> without writing the file (Sync Artwork writes it at the end).</summary>
    protected void Put(string path, TEntry entry) => _entries[path] = entry;

    /// <summary>Puts <paramref name="entry"/> for <paramref name="path"/> and, unless already armed, starts the one timer that writes the file 5 s later.</summary>
    protected void Set(string path, TEntry entry)
    {
        _entries[path] = entry;
        if (Interlocked.Exchange(ref _armed, 1) == 0)
        {
            // Disarmed before the write, so a change made while writing arms the next one. Save catches every exception: on a timer thread an
            // escaped one would end Jellyfin.
            var timer = new Timer(_ =>
            {
                Volatile.Write(ref _armed, 0);
                Save();
            });
            Interlocked.Exchange(ref _timer, timer)?.Dispose();
            timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Removes every entry whose path is not in <paramref name="keep"/>; the number removed.</summary>
    public int RemoveExcept(ISet<string> keep)
    {
        var removed = 0;
        foreach (var path in _entries.Keys)
        {
            if (!keep.Contains(path) && _entries.TryRemove(path, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Writes the store to its file (an own .tmp beside it, then moved over it), one write at a time; one warning on any failure.</summary>
    public void Save()
    {
        if (FilePath.Length == 0)
        {
            return;
        }

        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new Dictionary<string, TEntry>(_entries), Json));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("PlayniteBridge: cannot write the {What} \"{File}\": {Message}", _what, FilePath, ex.Message);
            }
        }
    }
}

/// <summary>
/// BlurHashes of the image files the games and their extras show, kept in <see cref="StoreFile"/> (&lt;DataPath&gt;\playnite-bridge\blurhashes.json):
/// full path (case ignored) -> the file's ticks (LastWriteTimeUtc) and length when it was computed and the hash; an empty hash = the image gives
/// none, not computed again until the file changes. Sync Artwork computes (<see cref="Refresh"/>) and writes at its end; a request's miss is
/// computed by BlurHashCache in the background and put in by <see cref="Set"/> (written by the timer).
/// </summary>
public sealed class BlurHashStore : FileStore<BlurHashStore.Entry>
{
    private static readonly object SharedLock = new();
    private static BlurHashStore? _shared;

    private BlurHashStore(string file, ILogger logger)
        : base(file, logger, "BlurHashes", "they are computed again by the next Sync Artwork")
    {
    }

    /// <summary>The file of the hashes, set by the Plugin constructor; empty = nothing read or written.</summary>
    public static string StoreFile { get; set; } = string.Empty;

    /// <summary>The one store of the process, read from <see cref="StoreFile"/> at the first call (again only when the file name changes).</summary>
    public static BlurHashStore Shared(ILogger logger)
    {
        lock (SharedLock)
        {
            if (_shared is null || _shared.FilePath != StoreFile)
            {
                _shared = Load(StoreFile, logger);
            }

            return _shared;
        }
    }

    /// <summary>A store read from <paramref name="file"/>: empty when the file is missing; empty with one warning when it cannot be read.</summary>
    public static BlurHashStore Load(string file, ILogger logger) => new(file, logger);

    /// <summary>True when the store has <paramref name="path"/> in its current time and length; <paramref name="hash"/> is then its hash, empty when the image gives none.</summary>
    public bool TryGet(string path, out string hash)
    {
        var found = TryGetCurrent(path, out var entry);
        hash = found ? entry.Hash : string.Empty;
        return found;
    }

    /// <summary>Puts the hash computed for <paramref name="path"/> while it had <paramref name="ticks"/> and <paramref name="length"/>; written by the timer.</summary>
    public void Set(string path, long ticks, long length, string hash) => Set(path, new Entry(ticks, length, hash));

    /// <summary>
    /// Computes the hash of an existing file by <paramref name="compute"/> unless the store has it in its current time and length; true when it was
    /// computed. An empty result or an error is kept as no hash (one warning).
    /// </summary>
    public bool Refresh(string path, Func<string, string> compute)
    {
        var info = new FileInfo(path);
        if (!info.Exists || TryGetCurrent(path, out _))
        {
            return false;
        }

        string hash;
        try
        {
            hash = compute(path) ?? string.Empty;
            if (hash.Length == 0)
            {
                Logger.LogWarning("PlayniteBridge: no BlurHash of \"{File}\": {Message}", Path.GetFileName(path), "the image gives no BlurHash");
            }
        }
        catch (Exception ex)
        {
            hash = string.Empty;
            Logger.LogWarning("PlayniteBridge: no BlurHash of \"{File}\": {Message}", Path.GetFileName(path), ex.Message);
        }

        Put(path, new Entry(info.LastWriteTimeUtc.Ticks, info.Length, hash));
        return true;
    }

    /// <summary>One file: its ticks and length when the hash was computed, and the hash (empty = none).</summary>
    public sealed record Entry(long Ticks, long Length, string Hash) : IFileState;
}

/// <summary>
/// Jellyfin's ffprobe answers of the local trailers and special features, kept in <see cref="StoreFile"/> (&lt;DataPath&gt;\playnite-bridge\mediainfo.json):
/// full path (case ignored) -> the file's ticks and length when it was probed, container, runtime, bitrate and media streams (without paths).
/// MediaInfoCache reads it first and puts every successful probe in (written by the timer); a failed probe is never stored. Sync Artwork removes
/// the entries of videos its scan no longer finds.
/// </summary>
public sealed class MediaInfoStore : FileStore<MediaInfoStore.Entry>
{
    private static readonly object SharedLock = new();
    private static MediaInfoStore? _shared;

    private MediaInfoStore(string file, ILogger logger)
        : base(file, logger, "media info of the local extras", "the extras are probed again when asked")
    {
    }

    /// <summary>The file of the media info, set by the Plugin constructor; empty = nothing read or written.</summary>
    public static string StoreFile { get; set; } = string.Empty;

    /// <summary>The one store of the process, read from <see cref="StoreFile"/> at the first call (again only when the file name changes).</summary>
    public static MediaInfoStore Shared(ILogger logger)
    {
        lock (SharedLock)
        {
            if (_shared is null || _shared.FilePath != StoreFile)
            {
                _shared = Load(StoreFile, logger);
            }

            return _shared;
        }
    }

    /// <summary>A store read from <paramref name="file"/>: empty when the file is missing; empty with one warning when it cannot be read.</summary>
    public static MediaInfoStore Load(string file, ILogger logger) => new(file, logger);

    /// <summary>True when the store has <paramref name="path"/> in its current time and length; <paramref name="info"/> is then the stored answer.</summary>
    public bool TryGet(string path, out MediaInfo info)
    {
        var found = TryGetCurrent(path, out var entry);
        info = found
            ? new MediaInfo { Container = entry.Container, RunTimeTicks = entry.RunTimeTicks, Bitrate = entry.Bitrate, MediaStreams = entry.MediaStreams.ToList() }
            : null!;
        return found;
    }

    /// <summary>Puts the answer probed for <paramref name="path"/> while it had <paramref name="ticks"/> and <paramref name="length"/>; written by the timer.</summary>
    public void Set(string path, long ticks, long length, MediaInfo info) =>
        Set(path, new Entry(ticks, length, info.Container, info.RunTimeTicks, info.Bitrate, (info.MediaStreams ?? Array.Empty<MediaStream>()).ToArray()));

    /// <summary>One video: its ticks and length when it was probed, and the parts of the answer the plugin uses.</summary>
    public sealed record Entry(long Ticks, long Length, string? Container, long? RunTimeTicks, int? Bitrate, MediaStream[] MediaStreams) : IFileState;
}

/// <summary>
/// Jellyfin's ffprobe answer of a local extra, kept in memory per file and state (path + ImageCache.Tag): each file is probed once, also when
/// several requests ask at the same time (they share the probe). With a store its answer is read first while the file's time and length fit, and
/// every successful probe is put into it. A failed probe is kept as null in memory only (one warning) until the file changes.
/// </summary>
public sealed class MediaInfoCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _running = new(2); // at most two ffprobe processes at once (Jellyfin's GetMediaInfo has no limit)
    private readonly Func<string, Task<MediaInfo>> _probe;
    private readonly Action<string, string> _warn;
    private readonly Func<MediaInfoStore>? _store;
    private int _probes;

    /// <param name="probe">The ffprobe answer of a file (IMediaEncoder.GetMediaInfo).</param>
    /// <param name="warn">Called once per file and state whose probe fails, with the path and the reason.</param>
    /// <param name="store">The store read first and given every successful probe; null = memory only.</param>
    public MediaInfoCache(Func<string, Task<MediaInfo>> probe, Action<string, string> warn, Func<MediaInfoStore>? store = null)
    {
        _probe = probe;
        _warn = warn;
        _store = store;
    }

    /// <summary>Number of probes run.</summary>
    public int Probes => _probes;

    /// <summary>The ffprobe answer of <paramref name="path"/> in the state <paramref name="tag"/>; null when the probe failed.</summary>
    public Task<MediaInfo?> Get(string path, string tag) =>
        _entries.AddOrUpdate(path, p => New(p, tag), (p, old) => old.Tag == tag ? old : New(p, tag)).Info.Value;

    private Entry New(string path, string tag) => new(tag, new Lazy<Task<MediaInfo?>>(() =>
        _store?.Invoke().TryGet(path, out var stored) == true ? Task.FromResult<MediaInfo?>(stored) : Probe(path)));

    private async Task<MediaInfo?> Probe(string path)
    {
        await _running.WaitAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _probes);
        try
        {
            // The file's state before the probe: a change meanwhile makes the stored entry old, so it is probed again.
            var file = new FileInfo(path);
            var exists = file.Exists;
            var (ticks, length) = exists ? (file.LastWriteTimeUtc.Ticks, file.Length) : (0L, 0L);
            var info = await _probe(path).ConfigureAwait(false);
            // No server path leaves the plugin, also not in a stream (an external file's path).
            foreach (var stream in info.MediaStreams ?? Array.Empty<MediaStream>())
            {
                stream.Path = null;
            }

            if (exists)
            {
                _store?.Invoke().Set(path, ticks, length, info);
            }

            return info;
        }
        catch (Exception ex)
        {
            _warn(path, ex.Message);
            return null;
        }
        finally
        {
            _running.Release();
        }
    }

    private sealed record Entry(string Tag, Lazy<Task<MediaInfo?>> Info);
}
