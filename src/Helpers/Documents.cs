using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.PlayniteBridge.Configuration;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>One slot of the documents area: its position in the slot list (1 to 10; 0 = the implicit slot of Flat), its sub folder ("" for Flat), the key of a predefined label (null for an own one) and its label.</summary>
public sealed record DocumentSlot(int Index, string Folder, string? Key, string Label);

/// <summary>One document file of a slot: Hash = 56 bits of the SHA-256 of its lower case file name (the part of its id that names it), Modified = last write time (UTC).</summary>
public sealed record DocumentFile(int Slot, string Path, string FileName, DateTime Modified, long Length, ulong Hash);

/// <summary>A row of the area as the page shows it: Row = the slot index, <see cref="Documents.AllRows"/> for the merged row, 0 for Flat.</summary>
public sealed record DocumentRow(int Row, string? Key, string Label, int Count);

/// <summary>What a document id says: the short id of the version, the slot, the seed of the random order and the file name hash.</summary>
public sealed record DocumentKey(string ShortId, int Slot, int Seed, ulong Hash);

/// <summary>The documents of one version as they lie on disk: per slot the files in natural name order.</summary>
public sealed class DocumentArea
{
    public static readonly DocumentArea Empty = new(Array.Empty<DocumentSlot>(), new Dictionary<int, IReadOnlyList<DocumentFile>>());

    private readonly Dictionary<(int, ulong), DocumentFile> _byHash = new();

    public DocumentArea(IReadOnlyList<DocumentSlot> slots, IReadOnlyDictionary<int, IReadOnlyList<DocumentFile>> files)
    {
        Slots = slots;
        Files = files;
        foreach (var file in files.Values.SelectMany(f => f))
        {
            _byHash.TryAdd((file.Slot, file.Hash), file);
        }
    }

    public IReadOnlyList<DocumentSlot> Slots { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<DocumentFile>> Files { get; }

    public int Total => Files.Values.Sum(f => f.Count);

    public DocumentFile? Find(int slot, ulong hash) => _byHash.TryGetValue((slot, hash), out var file) ? file : null;
}

/// <summary>
/// The documents area of a game page (reading material: manuals, guides, maps, books ...), built as <see cref="Screenshots"/>: files in
/// &lt;version folder&gt;\&lt;documents folder&gt;\&lt;slot folder&gt;\ of the selected version only. A document is served as a virtual Book item whose id carries
/// everything needed to find it again (short id of the version, slot, seed, file name hash), so it resolves after a restart without any stored map; none is in
/// Jellyfin's database. The files are PDF, EPUB and the comic archives CBZ and CBR, what Jellyfin Web's own readers open (pdfPlayer, bookPlayer, comicsPlayer).
/// Which versions: the version's own folder only (as the screenshots; the soundtrack falls back to other versions, a document list does not).
/// </summary>
public static class Documents
{
    /// <summary>The row id of the merged row (and the slot byte of its items).</summary>
    public const int AllRows = 255;

    /// <summary>The largest seed (24 bits).</summary>
    public const int MaxSeed = (1 << 24) - 1;

    private const byte ItemMarker = 0xB7;

    private static readonly string[] Extensions = { ".pdf", ".epub", ".cbz", ".cbr" };

    private static readonly string[] CoverExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };

    private static readonly TimeSpan Reuse = TimeSpan.FromSeconds(10);

    private static readonly string[] SlotProperties = Enumerable.Range(1, 10).SelectMany(i => new[] { "Folder", "Label", "OwnLabel", "On" }.Select(s => "DocumentSlot" + i + s)).ToArray();

    private static readonly Dictionary<string, PropertyInfo> SlotAccess = SlotProperties.ToDictionary(n => n, n => typeof(PluginConfiguration).GetProperty(n)!);

    private static readonly ConcurrentDictionary<string, (DateTime At, DocumentArea Area)> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The configured documents folder name, trimmed (also an unusable one: the extras scan leaves a folder of that name alone in any case).</summary>
    public static string AreaName(PluginConfiguration config) => (config.DocumentsFolder ?? string.Empty).Trim();

    /// <summary>The usable documents folder name, or null while Enable Documents is off or the name is refused.</summary>
    public static string? AreaFolder(PluginConfiguration config) =>
        config.EnableDocuments && AreaCommon.FolderNameError(AreaName(config), true) is null && !AreaCommon.Clashes(config, "documents", AreaName(config)) ? AreaName(config) : null;

    /// <summary>True when the area can show anything for the settings.</summary>
    public static bool Enabled(PluginConfiguration config) => AreaFolder(config) is not null;

    /// <summary>
    /// The slots in use, in slot list order: Flat = the one implicit slot (index 0, no sub folder, row "Documents"); else every slot that is on with a usable and
    /// not repeated (case ignored) folder name. The index is the slot's position in the list, so ids stay when an earlier slot is switched off.
    /// </summary>
    public static IReadOnlyList<DocumentSlot> Slots(PluginConfiguration config)
    {
        if (config.DocumentsFlat)
        {
            return new[] { new DocumentSlot(0, string.Empty, "documents", "Documents") };
        }

        var slots = new List<DocumentSlot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 10; i++)
        {
            string Text(string part) => (string)SlotAccess["DocumentSlot" + i + part].GetValue(config)!;
            var folder = Text("Folder").Trim();
            if (!(bool)SlotAccess["DocumentSlot" + i + "On"].GetValue(config)! || AreaCommon.FolderNameError(folder, false) is not null || !seen.Add(folder))
            {
                continue;
            }

            var label = (DocumentLabel)SlotAccess["DocumentSlot" + i + "Label"].GetValue(config)!;
            var own = Text("OwnLabel").Trim();
            slots.Add(label switch
            {
                DocumentLabel.Manuals => new DocumentSlot(i, folder, "manuals", "Manuals"),
                DocumentLabel.Guides => new DocumentSlot(i, folder, "guides", "Guides"),
                DocumentLabel.Maps => new DocumentSlot(i, folder, "maps", "Maps"),
                DocumentLabel.ReferenceCards => new DocumentSlot(i, folder, "referencecards", "Reference Cards"),
                DocumentLabel.Books => new DocumentSlot(i, folder, "books", "Books"),
                DocumentLabel.ArtBooks => new DocumentSlot(i, folder, "artbooks", "Art Books"),
                DocumentLabel.Comics => new DocumentSlot(i, folder, "comics", "Comics"),
                DocumentLabel.Magazines => new DocumentSlot(i, folder, "magazines", "Magazines"),
                DocumentLabel.Articles => new DocumentSlot(i, folder, "articles", "Articles"),
                DocumentLabel.Booklets => new DocumentSlot(i, folder, "booklets", "Booklets"),
                DocumentLabel.Merchandise => new DocumentSlot(i, folder, "merchandise", "Merchandise"),
                _ => new DocumentSlot(i, folder, null, own.Length == 0 || own.Length > 100 ? folder : own)
            });
        }

        return slots;
    }

    /// <summary>
    /// The documents of the version folder <paramref name="versionFolder"/>, read at most every 10 seconds per folder and slot settings: per slot the files
    /// (pdf, epub, cbz, cbr; not empty) lying directly in the slot folder (Flat: in the documents folder itself) in natural name order. A folder that is missing,
    /// is a junction or cannot be read gives no files. Empty while the area is off.
    /// </summary>
    public static DocumentArea Read(string versionFolder, PluginConfiguration config)
    {
        var area = AreaFolder(config);
        if (area is null)
        {
            return DocumentArea.Empty;
        }

        var slots = Slots(config);
        var key = versionFolder + "|" + area + "|" + string.Join("|", slots.Select(s => s.Index + ":" + s.Folder + ":" + s.Key + ":" + s.Label));
        if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < Reuse)
        {
            return cached.Area;
        }

        var areaPath = System.IO.Path.Combine(versionFolder, area);
        var files = new Dictionary<int, IReadOnlyList<DocumentFile>>();
        if (AreaCommon.IsPlainDirectory(areaPath))
        {
            foreach (var slot in slots)
            {
                var directory = slot.Folder.Length == 0 ? areaPath : System.IO.Path.Combine(areaPath, slot.Folder);
                if (slot.Folder.Length != 0 && !AreaCommon.IsPlainDirectory(directory))
                {
                    continue;
                }

                try
                {
                    var found = new DirectoryInfo(directory).EnumerateFiles("*", AreaCommon.ScanOptions)
                        .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.Length > 0)
                        .OrderBy(f => f.Name, Comparer<string>.Create(AreaCommon.NaturalCompare))
                        .Select(f => new DocumentFile(slot.Index, f.FullName, f.Name, f.LastWriteTimeUtc, f.Length, AreaCommon.NameHash(f.Name)))
                        .ToList();
                    if (found.Count > 0)
                    {
                        files[slot.Index] = found;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An unreadable slot folder shows no documents.
                }
            }
        }

        var read = new DocumentArea(slots, files);
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

    /// <summary>The plan of the rows (<see cref="AreaCommon.Layout"/>) over the slots that have files; the Merge ticks are read from <paramref name="config"/> now, not from the cached listing.</summary>
    private static IReadOnlyList<AreaCommon.LayoutRow> LayoutOf(DocumentArea area, PluginConfiguration config) =>
        AreaCommon.Layout(config.DocumentsFlat, config.DocumentsDisplay,
            area.Slots.Where(s => area.Files.ContainsKey(s.Index)).Select(s => (s.Index, AreaCommon.MergeOf(config, "DocumentSlot", s.Index))));

    /// <summary>
    /// The rows of the page as <see cref="AreaCommon.Layout"/> plans them: Flat = one row "Documents"; one row per slot that has files (slot list order) or, merged, one row
    /// "Documents" over the slots ticked in Merge at the place of the first of them. No row without files.
    /// </summary>
    public static IReadOnlyList<DocumentRow> Rows(DocumentArea area, PluginConfiguration config)
    {
        if (area.Total == 0)
        {
            return Array.Empty<DocumentRow>();
        }

        return LayoutOf(area, config).Select(r =>
        {
            if (r.Row == AllRows)
            {
                return new DocumentRow(AllRows, "documents", "Documents", r.Slots.Sum(i => area.Files[i].Count));
            }

            var slot = area.Slots.First(s => s.Index == r.Row);
            return new DocumentRow(slot.Index, slot.Key, slot.Label, area.Files[slot.Index].Count);
        }).ToList();
    }

    /// <summary>
    /// The files of a row in the order they are shown: a slot's files sorted by the sort setting; the merged row the slots one after the other, each sorted
    /// (Keep Slots Together), else all files mixed by the sort. Random uses <paramref name="seed"/>.
    /// </summary>
    public static IReadOnlyList<DocumentFile> Files(DocumentArea area, int row, PluginConfiguration config, int seed)
    {
        if (row != AllRows)
        {
            return area.Files.TryGetValue(row, out var one) ? Sort(one, config.DocumentsSort, seed) : Array.Empty<DocumentFile>();
        }

        if (LayoutOf(area, config).FirstOrDefault(r => r.Row == AllRows) is not { } merged)
        {
            return Array.Empty<DocumentFile>();
        }

        if (config.DocumentsKeepSlotsTogether)
        {
            return merged.Slots.SelectMany(i => Sort(area.Files[i], config.DocumentsSort, seed)).ToList();
        }

        return Sort(merged.Slots.SelectMany(i => area.Files[i]).ToList(), config.DocumentsSort, seed);
    }

    private static IReadOnlyList<DocumentFile> Sort(IReadOnlyList<DocumentFile> files, ScreenshotSort sort, int seed)
    {
        var name = Comparer<DocumentFile>.Create((a, b) => AreaCommon.NaturalCompare(a.FileName, b.FileName));
        return sort switch
        {
            ScreenshotSort.NameDescending => files.OrderByDescending(f => f, name).ToList(),
            ScreenshotSort.DateAscending => files.OrderBy(f => f.Modified).ThenBy(f => f, name).ToList(),
            ScreenshotSort.DateDescending => files.OrderByDescending(f => f.Modified).ThenBy(f => f, name).ToList(),
            ScreenshotSort.Random => files.OrderBy(f => AreaCommon.Mix(seed, f.Slot, f.Hash)).ThenBy(f => f.Slot).ThenBy(f => f.Hash).ToList(),
            _ => files.OrderBy(f => f, name).ToList()
        };
    }

    /// <summary>The seed a request asks for: itself when it is within 1 to <see cref="MaxSeed"/> and the sort is Random, a new one for Random without one, else 0.</summary>
    public static int SeedFor(PluginConfiguration config, int? asked) =>
        config.DocumentsSort != ScreenshotSort.Random ? 0 : asked is > 0 and <= MaxSeed ? asked.Value : RandomNumberGenerator.GetInt32(1, MaxSeed + 1);

    /// <summary>The image tag of a file: from its last write time and length; with a stored page-1 thumbnail (<see cref="HasThumb"/>) a "t" more, so a tile that showed the neutral tile or an older picture gets a new address.</summary>
    public static string Tag(DocumentFile file) => AreaCommon.Tag(file.Modified, file.Length) + (HasThumb(file) ? "t" : string.Empty);

    /// <summary>The id of a document: short id of the version (4 bytes), slot (1), seed (3), file name hash (7), marker 0xB7 (1).</summary>
    public static Guid ItemId(string shortId, int slot, int seed, ulong hash) => AreaCommon.BuildId(shortId, slot, seed, hash, ItemMarker, null);

    /// <summary>What an id says; null when it is no document id (cheap: no disk, no lookup).</summary>
    public static DocumentKey? Decode(Guid id) =>
        AreaCommon.Decode(id, ItemMarker, null) is { } k ? new DocumentKey(k.ShortId, k.Slot, k.Seed, k.Hash) : null;

    /// <summary>The reader the file's extension needs: "pdf", "epub" or "comic".</summary>
    public static string Kind(DocumentFile file) => System.IO.Path.GetExtension(file.FileName).ToLowerInvariant() switch
    {
        ".pdf" => "pdf",
        ".epub" => "epub",
        _ => "comic"
    };

    /// <summary>The finished cover of a document: a picture file (the extracted cover, kept in the cache folder) or, without a picture, the neutral tile (an SVG).</summary>
    public sealed record CoverResult(string? FilePath, byte[]? Svg);

    private static readonly ConcurrentDictionary<string, Lazy<CoverResult>> Covers = new();

    private static DateTime _coversPruned = DateTime.MinValue;

    private static readonly string[] CoverFileExtensions = { ".jpg", ".png", ".webp", ".gif" };

    /// <summary>
    /// The cover of a document tile, worked out once per file version: a key of path, last write time and length gives one result for everybody (single flight: concurrent requests
    /// wait for the one computation), whatever the id's seed; a failure is the neutral tile and is kept as well. The extracted picture is a file in <paramref name="cacheDirectory"/>
    /// (named by the key's hash), so the caller serves it through Jellyfin's image processor (sizes, caches). At most 512 results are kept in memory.
    /// </summary>
    public static CoverResult CoverOf(DocumentFile file, string cacheDirectory)
    {
        // A stored page-1 thumbnail (made by a browser, Generate Thumbnails) is the true first page and wins over the first picture found in the file.
        if (HasThumb(file) && ThumbFile(file, cacheDirectory) is var thumb && File.Exists(thumb))
        {
            return new CoverResult(thumb, null);
        }

        var key = KeyOf(file);
        if (Covers.Count > 512)
        {
            Covers.Clear();
        }

        var cover = Covers.GetOrAdd(key, k => new Lazy<CoverResult>(() => BuildCover(file, k, cacheDirectory), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (cover.FilePath is not null && !File.Exists(cover.FilePath))
        {
            // the picture file was pruned meanwhile: worked out again
            Covers.TryRemove(key, out _);
            return CoverOf(file, cacheDirectory);
        }

        return cover;
    }

    /// <summary>The folder of the covers and thumbnails (set by the plugin): &lt;cache&gt;\playnite-bridge\document-covers.</summary>
    public static string CoverDirectory { get; set; } = string.Empty;

    private static string KeyOf(DocumentFile file) => file.Path + "|" + file.Modified.Ticks + "|" + file.Length;

    private static string NameOf(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 12).ToLowerInvariant();

    /// <summary>The largest CBR comic that gets a thumbnail; a larger one keeps the default picture.</summary>
    public const long MaxComicThumbBytes = 150_000_000;

    /// <summary>The largest thumbnail picture a browser may post.</summary>
    public const int MaxThumbBytes = 512 * 1024;

    /// <summary>
    /// True where the server itself reads page 1 of a CBR comic (the plugin build compiled with PB_SERVER_CBR: SharpCompress, Jellyfin 12.x ships it); false where the administrator's browser still draws it
    /// (Jellyfin 10.10.7 ships no RAR reader the server could use; that build keeps the browser path).
    /// </summary>
#if PB_SERVER_CBR
    public const bool ServerReadsCbr = true;
#else
    public const bool ServerReadsCbr = false;
#endif

    /// <summary>True for the files that get a stored page-1 thumbnail (the "-p1.jpg" file, its tag suffix t and the "-p1.fail" marker): PDF, and CBR up to <see cref="MaxComicThumbBytes"/> (CBZ and EPUB covers are extracted by the server).</summary>
    public static bool ThumbKind(DocumentFile file) => System.IO.Path.GetExtension(file.FileName).ToLowerInvariant() switch
    {
        ".pdf" => true,
        ".cbr" => file.Length <= MaxComicThumbBytes,
        _ => false
    };

    /// <summary>True for the CBR comics the server reads itself (<see cref="ServerReadsCbr"/>; Sync Extras makes their thumbnails).</summary>
    public static bool ServerKind(DocumentFile file) => ServerReadsCbr && ThumbKind(file) && System.IO.Path.GetExtension(file.FileName).Equals(".cbr", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the thumbnail kinds a browser draws and posts (the pending list, the post and the failure report): PDF, and CBR where the server does not read it itself (<see cref="ServerReadsCbr"/>).</summary>
    public static bool BrowserThumbKind(DocumentFile file) => ThumbKind(file) && !ServerKind(file);

    /// <summary>The file a thumbnail of <paramref name="file"/> has in <paramref name="cacheDirectory"/>: the key's 24 hex characters and "-p1.jpg" (never the name of an extracted cover, which has no suffix).</summary>
    public static string ThumbFile(DocumentFile file, string cacheDirectory) => System.IO.Path.Combine(cacheDirectory, NameOf(KeyOf(file)) + "-p1.jpg");

    // The names of the thumbnails in the cover folder, read again when the folder changed (checked at most every 2 seconds) or after a store: a listed item costs a set lookup, not a file test.
    private static readonly object ThumbLock = new();
    private static HashSet<string> _thumbNames = new(StringComparer.Ordinal);
    private static Dictionary<string, DateTime> _failNames = new(StringComparer.Ordinal);
    private static DateTime _thumbsChecked = DateTime.MinValue;
    private static DateTime _thumbsFolderTime = DateTime.MinValue;

    private static void RefreshThumbs(bool force)
    {
        lock (ThumbLock)
        {
            if (!force && DateTime.UtcNow - _thumbsChecked < TimeSpan.FromSeconds(2))
            {
                return;
            }

            _thumbsChecked = DateTime.UtcNow;
            try
            {
                var changed = Directory.GetLastWriteTimeUtc(CoverDirectory);
                if (!force && changed == _thumbsFolderTime)
                {
                    return;
                }

                _thumbsFolderTime = changed;
                _thumbNames = Directory.Exists(CoverDirectory)
                    ? Directory.EnumerateFiles(CoverDirectory, "*-p1.jpg").Select(f => System.IO.Path.GetFileName(f)).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
                _failNames = Directory.Exists(CoverDirectory)
                    ? Directory.EnumerateFiles(CoverDirectory, "*-p1.fail").ToDictionary(f => System.IO.Path.GetFileName(f), f => File.GetLastWriteTimeUtc(f), StringComparer.Ordinal)
                    : new Dictionary<string, DateTime>(StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _thumbNames = new HashSet<string>(StringComparer.Ordinal);
                _failNames = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            }
        }
    }

    /// <summary>True while Generate Thumbnails is on and the document is of a thumbnail kind with a stored thumbnail.</summary>
    public static bool HasThumb(DocumentFile file)
    {
        if (CoverDirectory.Length == 0 || Plugin.Instance?.Configuration.DocumentsGenerateThumbnails != true || !ThumbKind(file))
        {
            return false;
        }

        RefreshThumbs(false);
        lock (ThumbLock)
        {
            return _thumbNames.Contains(NameOf(KeyOf(file)) + "-p1.jpg");
        }
    }

    /// <summary>How long a document a browser could not draw (password, damaged, rejected picture) stays off the pending list.</summary>
    public static readonly TimeSpan FailureRetry = TimeSpan.FromDays(7);

    /// <summary>True while a browser reported that this document (this state of the file: path, time, length) cannot be drawn, less than <see cref="FailureRetry"/> ago; only with Generate Thumbnails on.</summary>
    public static bool HasFailure(DocumentFile file)
    {
        if (CoverDirectory.Length == 0 || Plugin.Instance?.Configuration.DocumentsGenerateThumbnails != true || !ThumbKind(file))
        {
            return false;
        }

        RefreshThumbs(false);
        lock (ThumbLock)
        {
            return _failNames.TryGetValue(NameOf(KeyOf(file)) + "-p1.fail", out var at) && DateTime.UtcNow - at < FailureRetry;
        }
    }

    /// <summary>Remembers that a browser could not draw this document: an empty marker file <c>&lt;24 hex&gt;-p1.fail</c> beside the thumbnails (its time is the time of the report; a report again after the retry time renews it).</summary>
    public static void MarkFailed(DocumentFile file, string cacheDirectory)
    {
        Directory.CreateDirectory(cacheDirectory);
        var marker = System.IO.Path.Combine(cacheDirectory, NameOf(KeyOf(file)) + "-p1.fail");
        if (!File.Exists(marker) || DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromDays(1))
        {
            File.WriteAllBytes(marker, Array.Empty<byte>());
        }

        if (string.Equals(cacheDirectory, CoverDirectory, StringComparison.OrdinalIgnoreCase))
        {
            RefreshThumbs(true);
        }
    }

    /// <summary>
    /// Stores the thumbnail <paramref name="jpeg"/> (already validated) of <paramref name="file"/>: written to a temporary file and moved, never over an existing thumbnail
    /// (false then). The folder is pruned like the covers.
    /// </summary>
    public static bool StoreThumb(DocumentFile file, byte[] jpeg, string cacheDirectory)
    {
        Directory.CreateDirectory(cacheDirectory);
        var target = ThumbFile(file, cacheDirectory);
        var temporary = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, jpeg);
            File.Move(temporary, target, false);
        }
        catch (IOException) when (File.Exists(target))
        {
            return false;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        if (string.Equals(cacheDirectory, CoverDirectory, StringComparison.OrdinalIgnoreCase))
        {
            RefreshThumbs(true);
        }

        PruneCovers(cacheDirectory);
        return true;
    }

    /// <summary>
    /// Whether <paramref name="data"/> is a baseline or progressive JPEG a thumbnail may be: starts FF D8 FF, ends FF D9, its marker segments (at most 64) lead to a start-of-frame
    /// of 8 bits, 100 to 1000 wide and 100 to 2000 high. Only the headers are read; no pixel is decoded.
    /// </summary>
    public static bool ValidThumbJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 4 or > MaxThumbBytes || data[0] != 0xFF || data[1] != 0xD8 || data[2] != 0xFF || data[^2] != 0xFF || data[^1] != 0xD9)
        {
            return false;
        }

        var at = 2;
        for (var segments = 0; segments < 64 && at + 4 <= data.Length; segments++)
        {
            if (data[at] != 0xFF)
            {
                return false;
            }

            var marker = data[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            var length = (data[at + 2] << 8) | data[at + 3];
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (length < 8 || at + 2 + length > data.Length)
                {
                    return false;
                }

                var height = (data[at + 5] << 8) | data[at + 6];
                var width = (data[at + 7] << 8) | data[at + 8];
                return data[at + 4] == 8 && width is >= 100 and <= 1000 && height is >= 100 and <= 2000;
            }

            if (marker is 0xD9 or 0xDA || length < 2)
            {
                return false;
            }

            at += 2 + length;
        }

        return false;
    }

    private static CoverResult BuildCover(DocumentFile file, string key, string cacheDirectory)
    {
        var neutral = new CoverResult(null, Encoding.UTF8.GetBytes(Tile(file)));
        try
        {
            var name = NameOf(key);
            Directory.CreateDirectory(cacheDirectory);
            var existing = CoverFileExtensions.Select(e => System.IO.Path.Combine(cacheDirectory, name + e)).FirstOrDefault(File.Exists);
            if (existing is not null)
            {
                return new CoverResult(existing, null);
            }

            var (bytes, extension) = ExtractCover(file);
            if (bytes is null)
            {
                return neutral;
            }

            var target = System.IO.Path.Combine(cacheDirectory, name + extension);
            var temporary = target + "." + Environment.CurrentManagedThreadId + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, true);
            PruneCovers(cacheDirectory);
            return new CoverResult(target, null);
        }
        catch (Exception)
        {
            // Whatever is wrong with the file or the folder (a damaged archive, a refused parse, no disk): the neutral tile, and it is not tried again.
            return neutral;
        }
    }

    // At most once an hour: when the folder holds more than 2000 files, the covers not written for 7 days go (stored thumbnails and failure markers are not covers: see Prune).
    private static void PruneCovers(string directory)
    {
        if (DateTime.UtcNow - _coversPruned < TimeSpan.FromHours(1))
        {
            return;
        }

        _coversPruned = DateTime.UtcNow;
        Prune(directory, DateTime.UtcNow);
    }

    /// <summary>
    /// The pruning itself: with more than 2000 files in <paramref name="directory"/>, a cover (an extracted picture) not written for 7 days is deleted. A stored thumbnail ("-p1.jpg") and a failure marker ("-p1.fail")
    /// are not pruned by that age (a thumbnail is slow to make only once, and a marker keeps a hostile file from being tried again); they go only after a year, which also bounds the folder when documents are gone
    /// (their names cannot be told here: the name is a hash). Returns the number of files deleted.
    /// </summary>
    public static int Prune(string directory, DateTime now)
    {
        var deleted = 0;
        try
        {
            var files = new DirectoryInfo(directory).EnumerateFiles().ToList();
            if (files.Count > 2000)
            {
                foreach (var old in files.Where(f => now - f.LastWriteTimeUtc > (IsStoredByAge(f.Name) ? TimeSpan.FromDays(365) : TimeSpan.FromDays(7))))
                {
                    old.Delete();
                    deleted++;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to clean up now.
        }

        return deleted;
    }

    // A stored thumbnail or a failure marker (not an extracted cover).
    private static bool IsStoredByAge(string name) => name.EndsWith("-p1.jpg", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-p1.fail", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The picture of a document: the first page of a comic archive (CBZ: its first image), the cover of an EPUB (its cover image, else its first image), the first JPEG picture of a
    /// PDF (<see cref="PdfFirstJpeg"/>); null for the others (no rasteriser for the other PDFs and no RAR reader for CBR is part of Jellyfin or the plugin), an archive with more than
    /// 10,000 entries and an entry that is not 1 byte to 20 MB.
    /// </summary>
    private static (byte[]? Bytes, string Extension) ExtractCover(DocumentFile file)
    {
        var kind = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        if (kind == ".pdf")
        {
            return PdfFirstJpeg(file.Path) is { } jpeg ? (jpeg, ".jpg") : (null, string.Empty);
        }

        if (kind is ".cbz" or ".epub")
        {
            using var zip = ZipFile.OpenRead(file.Path);
            if (zip.Entries.Count > 10_000)
            {
                return (null, string.Empty);
            }

            var entry = kind == ".cbz" ? FirstImage(zip) : EpubCover(zip) ?? FirstImage(zip);
            if (entry is not null && entry.Length is > 0 and < 20_000_000)
            {
                using var stream = entry.Open();
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                var extension = System.IO.Path.GetExtension(entry.Name).ToLowerInvariant();
                return (copy.ToArray(), CoverFileExtensions.Contains(extension) ? extension : ".jpg");
            }
        }

        return (null, string.Empty);
    }

    /// <summary>
    /// The first JPEG picture stored in a PDF (a scanned manual keeps its pages as JPEG streams, the first one is usually the cover); null for a PDF without one
    /// (text and vector pages need a rasteriser, which neither Jellyfin nor the plugin has), a CMYK picture, a file over 80 MB or an unreadable one. Every search is bounded: the
    /// dictionary of a picture is looked for in the 4 KB before its /DCTDecode and the stream key in the 4 KB after it, so a crafted file costs at most 200 bounded tries.
    /// </summary>
    private static byte[]? PdfFirstJpeg(string path)
    {
        if (new FileInfo(path).Length is 0 or > 80_000_000)
        {
            return null;
        }

        var data = File.ReadAllBytes(path);
        var filter = Encoding.ASCII.GetBytes("/DCTDecode");
        var objKey = Encoding.ASCII.GetBytes("obj");
        var streamKey = Encoding.ASCII.GetBytes("stream");
        var endKey = Encoding.ASCII.GetBytes("endstream");
        var from = 0;
        for (var tries = 0; tries < 200 && from < data.Length; tries++)
        {
            var at = data.AsSpan(from).IndexOf(filter);
            if (at < 0)
            {
                return null;
            }

            at += from;
            from = at + filter.Length;
            var windowStart = Math.Max(0, at - 4096);
            var start = data.AsSpan(windowStart, at - windowStart).LastIndexOf(objKey);
            var streamAt = data.AsSpan(at, Math.Min(4102, data.Length - at)).IndexOf(streamKey);
            if (start < 0 || streamAt < 0)
            {
                continue;
            }

            start += windowStart;
            streamAt += at;
            var dictionary = Encoding.Latin1.GetString(data, start, streamAt - start);
            if (!dictionary.Contains("/Image", StringComparison.Ordinal) || dictionary.Contains("DeviceCMYK", StringComparison.Ordinal) || dictionary.Contains("/ImageMask", StringComparison.Ordinal))
            {
                continue;
            }

            var begin = streamAt + streamKey.Length;
            if (begin + 1 >= data.Length)
            {
                return null;
            }

            begin += data[begin] == 0x0D && data[begin + 1] == 0x0A ? 2 : data[begin] == 0x0A ? 1 : 0;
            if (begin + 2 >= data.Length)
            {
                return null;
            }

            var end = data.AsSpan(begin, Math.Min(data.Length - begin, 20_000_001)).IndexOf(endKey);
            if (end < 0)
            {
                return null;
            }

            end += begin;
            while (end > begin && (data[end - 1] == 0x0A || data[end - 1] == 0x0D))
            {
                end--;
            }

            if (end - begin is > 1_000 and < 20_000_000 && data[begin] == 0xFF && data[begin + 1] == 0xD8 && data[end - 2] == 0xFF && data[end - 1] == 0xD9)
            {
                return data.AsSpan(begin, end - begin).ToArray();
            }
        }

        return null;
    }

    private static string ContentTypeOf(string name) => System.IO.Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg"
    };

    private static ZipArchiveEntry? FirstImage(ZipArchive zip) => zip.Entries
        .Where(e => e.Length > 0 && CoverExtensions.Contains(System.IO.Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase) && !e.FullName.StartsWith("__MACOSX", StringComparison.Ordinal))
        .OrderBy(e => e.FullName, Comparer<string>.Create(AreaCommon.NaturalCompare)).FirstOrDefault();

    // The XML of an EPUB (container, OPF): at most 1 MB read, no DTD, no resolver, at most 1 million characters (a decompression bomb or an entity bomb gives an exception, not a cover).
    private static XDocument? LoadXml(ZipArchiveEntry entry)
    {
        if (entry.Length is 0 or > 1_000_000)
        {
            return null;
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1_000_000 };
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader);
    }

    // The cover of an EPUB: the OPF's cover-image item, or the item the cover meta names (an image entry only).
    private static ZipArchiveEntry? EpubCover(ZipArchive zip)
    {
        var container = zip.GetEntry("META-INF/container.xml");
        if (container is null || LoadXml(container) is not { } containerXml)
        {
            return null;
        }

        var opfPath = containerXml.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value;
        var opfEntry = opfPath is null ? null : zip.GetEntry(opfPath);
        if (opfEntry is null || LoadXml(opfEntry) is not { } opf)
        {
            return null;
        }

        var items = opf.Descendants().Where(e => e.Name.LocalName == "item").ToList();
        var coverId = opf.Descendants().FirstOrDefault(e => e.Name.LocalName == "meta" && (string?)e.Attribute("name") == "cover")?.Attribute("content")?.Value;
        var cover = items.FirstOrDefault(e => ((string?)e.Attribute("properties") ?? string.Empty).Split(' ').Contains("cover-image"))
            ?? items.FirstOrDefault(e => coverId is not null && (string?)e.Attribute("id") == coverId);
        var href = (string?)cover?.Attribute("href");
        if (href is null)
        {
            return null;
        }

        var baseDir = opfPath!.Contains('/') ? opfPath[..(opfPath.LastIndexOf('/') + 1)] : string.Empty;
        var entry = zip.GetEntry(Uri.UnescapeDataString(baseDir + href));
        return entry is not null && CoverExtensions.Contains(System.IO.Path.GetExtension(entry.Name), StringComparer.OrdinalIgnoreCase) ? entry : null;
    }

    // A neutral document tile: a page with a folded corner and the file type, drawn as SVG (a background image of the tile; no rasteriser needed).
    private static string Tile(DocumentFile file)
    {
        var type = System.IO.Path.GetExtension(file.FileName).TrimStart('.').ToUpperInvariant();
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 300 400\"><rect width=\"300\" height=\"400\" fill=\"#2a2e38\"/>"
            + "<path d=\"M70 60h110l50 50v230H70z\" fill=\"#e9ecf2\"/><path d=\"M180 60v50h50z\" fill=\"#b9bfcc\"/>"
            + "<rect x=\"92\" y=\"140\" width=\"116\" height=\"8\" rx=\"4\" fill=\"#b9bfcc\"/><rect x=\"92\" y=\"164\" width=\"116\" height=\"8\" rx=\"4\" fill=\"#b9bfcc\"/>"
            + "<rect x=\"92\" y=\"188\" width=\"84\" height=\"8\" rx=\"4\" fill=\"#b9bfcc\"/>"
            + "<rect x=\"84\" y=\"262\" width=\"132\" height=\"44\" rx=\"6\" fill=\"#c0392b\"/>"
            + "<text x=\"150\" y=\"294\" font-family=\"Arial,Helvetica,sans-serif\" font-size=\"26\" font-weight=\"bold\" fill=\"#ffffff\" text-anchor=\"middle\">" + type + "</text></svg>";
    }
}

/// <summary>One document that waits for a browser to draw its page-1 thumbnail: its item id (seed 0), file name (no extension), length and kind ("pdf" or "comic").</summary>
public sealed record PendingThumb(string Id, string Name, long Size, string Kind);

/// <summary>
/// The list of documents without a thumbnail that Sync Extras writes (&lt;data&gt;\playnite-bridge\document-thumbs-pending.json) and an administrator's browser works off
/// (GET PlayniteBridge/documents/pending). No path is in it: the id names the file.
/// </summary>
public static class DocumentThumbs
{
    /// <summary>At most this many entries are written.</summary>
    public const int MaxPending = 5000;

    /// <summary>The file of the list (set by the plugin).</summary>
    public static string PendingFile { get; set; } = string.Empty;

    private static readonly object FileLock = new();

    /// <summary>The documents of <paramref name="files"/> that need a thumbnail: of a thumbnail kind, not empty, without one yet, as pending entries for the version with the short id <paramref name="shortId"/>.</summary>
    public static IEnumerable<PendingThumb> Missing(string shortId, IEnumerable<DocumentFile> files) =>
        files.Where(f => Documents.BrowserThumbKind(f) && f.Length > 0 && !Documents.HasThumb(f) && !Documents.HasFailure(f))
            .Select(f => new PendingThumb(Documents.ItemId(shortId, f.Slot, 0, f.Hash).ToString("N"), System.IO.Path.GetFileNameWithoutExtension(f.FileName), f.Length, Documents.Kind(f)));

    /// <summary>Writes the list (temporary file, then moved; an empty list removes the file).</summary>
    public static void WritePending(IReadOnlyList<PendingThumb> entries)
    {
        if (PendingFile.Length == 0)
        {
            return;
        }

        lock (FileLock)
        {
            if (entries.Count == 0)
            {
                if (File.Exists(PendingFile))
                {
                    File.Delete(PendingFile);
                }

                return;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PendingFile)!);
            var temporary = PendingFile + ".tmp";
            File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(entries.Take(MaxPending).ToList()), new UTF8Encoding(false));
            File.Move(temporary, PendingFile, true);
        }
    }

    /// <summary>The list as written; empty when there is none or it is unreadable.</summary>
    public static IReadOnlyList<PendingThumb> ReadPending()
    {
        try
        {
            lock (FileLock)
            {
                return PendingFile.Length > 0 && File.Exists(PendingFile)
                    ? System.Text.Json.JsonSerializer.Deserialize<List<PendingThumb>>(File.ReadAllText(PendingFile)) ?? new List<PendingThumb>()
                    : new List<PendingThumb>();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new List<PendingThumb>();
        }
    }
}
