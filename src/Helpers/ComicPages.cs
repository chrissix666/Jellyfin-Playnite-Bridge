#if PB_SERVER_CBR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Page 1 of a CBR comic (a RAR archive; ZIP and 7z under a .cbr name too), read on the server with SharpCompress (shipped with
/// Jellyfin 12.x, not bundled by the plugin) and stored as the thumbnail (<see cref="Documents.StoreThumb"/>, the same "-p1.jpg" file
/// and tag suffix a browser-drawn PDF thumbnail has). <see cref="MakeMissingAsync"/> makes them; no browser is involved.
/// Page 1 is the first image entry in natural name order (jpg, jpeg, png, webp, gif, bmp; folders, hidden and system entries skipped).
/// If the entries cannot be listed in that order (some solid RAR), the first readable image in file order is used.
/// Every read is bounded: an archive over <see cref="Documents.MaxComicThumbBytes"/> is not opened, an entry over
/// <see cref="MaxEntryBytes"/> is not read, a file may take 30 seconds at most, two conversions run at a time, and a password or
/// unreadable file leaves a "-p1.fail" marker. Nothing is written outside the cover folder and no path comes from a request.
/// </summary>
public static class ComicPages
{
    /// <summary>The largest picture entry that is unpacked.</summary>
    public const long MaxEntryBytes = 40_000_000;

    /// <summary>The most entries an archive may list.</summary>
    public const int MaxEntries = 10_000;

    /// <summary>The longest one file may be read.</summary>
    public static TimeSpan MaxTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The most conversions at a time.</summary>
    public const int MaxConcurrent = 2;

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp" };

    private static readonly SemaphoreSlim Gate = new(MaxConcurrent);

    /// <summary>The thumbnail width in pixels (as the browser drew it).</summary>
    public const int ThumbWidth = 400;

    /// <summary>The largest page picture that is decoded, in pixels (a comic page of 6000 x 9000 is 54 million); a larger one is refused before the decoder sees it.</summary>
    public const long MaxPixels = 60_000_000;

    /// <summary>True for an entry name that can be page 1: an image by extension, in no folder or file that is hidden (a leading dot) or macOS or Windows metadata.</summary>
    public static bool IsPageName(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var parts = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.StartsWith('.') || part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var name = parts[^1];
        return !name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) && !name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
            && ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
    }

    /// <summary>Natural name order: digit runs by value (page2 before page10), the rest ignoring case; entry names with / or \ compare alike.</summary>
    public static int NaturalCompare(string? a, string? b)
    {
        a = (a ?? string.Empty).Replace('\\', '/');
        b = (b ?? string.Empty).Replace('\\', '/');
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var si = i;
                var sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var da = a[si..i].TrimStart('0');
                var db = b[sj..j].TrimStart('0');
                if (da.Length != db.Length)
                {
                    return da.Length < db.Length ? -1 : 1;
                }

                var c = string.CompareOrdinal(da, db);
                if (c != 0)
                {
                    return c < 0 ? -1 : 1;
                }
            }
            else
            {
                var c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                if (c != 0)
                {
                    return c < 0 ? -1 : 1;
                }

                i++;
                j++;
            }
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Why a first page could not be read ("" = it was).</summary>
    public const string Ok = "";

    /// <summary>
    /// The first page of the archive <paramref name="path"/> (RAR, or ZIP or 7z under a .cbr name): its bytes and file extension, or null bytes and the reason (size, time, password, no image, too large, or the exception's type). Never throws
    /// except for <paramref name="cancellationToken"/>. A damaged archive whose list of entries cannot be read is walked in file order instead: the first image that can be read.
    /// </summary>
    public static (byte[]? Bytes, string Extension, string Reason) FirstPage(string path, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is 0 || info.Length > Documents.MaxComicThumbBytes)
            {
                return (null, string.Empty, "size");
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(MaxTime);
            try
            {
                try
                {
                    return ReadArchive(path, limit.Token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not SharpCompress.Common.CryptographicException)
                {
                    // No usable entry list (truncated archive): walk the file in order. Not when the limit or the caller ended the read; the library wraps that exception.
                    limit.Token.ThrowIfCancellationRequested();
                    return ReadSequential(path, limit.Token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && limit.Token.IsCancellationRequested)
            {
                // the library may report a cancellation as its own format error
                throw new OperationCanceledException(limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, string.Empty, "time");
        }
        catch (SharpCompress.Common.CryptographicException)
        {
            return (null, string.Empty, "password");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        {
            return (null, string.Empty, ex.GetType().Name);
        }
    }

    private static (byte[]? Bytes, string Extension, string Reason) ReadArchive(string path, CancellationToken token)
    {
        using var file = new TimedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920), token);
        // opened by content, so a renamed .cbz or .cb7 works too
        using var archive = ArchiveFactory.OpenArchive(file, new ReaderOptions { LeaveStreamOpen = true });
        if (archive.IsEncrypted)
        {
            return (null, string.Empty, "password");
        }

        var pages = new List<IArchiveEntry>();
        var count = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if (++count > MaxEntries)
            {
                return (null, string.Empty, "entries");
            }

            if (!entry.IsDirectory && entry.LinkTarget is null && IsPageName(entry.Key))
            {
                pages.Add(entry);
            }
        }

        if (pages.Count == 0)
        {
            return (null, string.Empty, "no image");
        }

        var first = pages.OrderBy(e => e.Key, Comparer<string?>.Create(NaturalCompare)).First();
        if (first.IsEncrypted)
        {
            return (null, string.Empty, "password");
        }

        if (first.Size is <= 0 or > MaxEntryBytes)
        {
            return (null, string.Empty, "too large");
        }

        using var stream = first.OpenEntryStream();
        return Copy(stream, first.Size, first.Key, token);
    }

    private static (byte[]? Bytes, string Extension, string Reason) ReadSequential(string path, CancellationToken token)
    {
        using var file = new TimedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920), token);
        using var reader = ReaderFactory.OpenReader(file, new ReaderOptions { LeaveStreamOpen = true });
        var count = 0;
        while (reader.MoveToNextEntry())
        {
            token.ThrowIfCancellationRequested();
            if (++count > MaxEntries)
            {
                break;
            }

            var entry = reader.Entry;
            if (entry.IsDirectory || entry.LinkTarget is not null || !IsPageName(entry.Key))
            {
                continue;
            }

            if (entry.IsEncrypted)
            {
                return (null, string.Empty, "password");
            }

            if (entry.Size is <= 0 or > MaxEntryBytes)
            {
                return (null, string.Empty, "too large");
            }

            using var stream = reader.OpenEntryStream();
            return Copy(stream, entry.Size, entry.Key, token);
        }

        return (null, string.Empty, "no image");
    }

    // At most MaxEntryBytes + 1 bytes are read, whatever the headers claim.
    private static (byte[]? Bytes, string Extension, string Reason) Copy(Stream stream, long declared, string? key, CancellationToken token)
    {
        using var copy = new MemoryStream((int)Math.Min(declared, MaxEntryBytes));
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (copy.Length + read > MaxEntryBytes)
            {
                return (null, string.Empty, "too large");
            }

            copy.Write(buffer, 0, read);
        }

        return copy.Length == 0 ? (null, string.Empty, "empty") : (copy.ToArray(), Path.GetExtension(key ?? string.Empty).ToLowerInvariant(), Ok);
    }

    /// <summary>
    /// Makes the missing thumbnails of the CBR comics in <paramref name="files"/> (a thumbnail kind without a thumbnail or a recent failure marker): page 1 is read, scaled to
    /// <see cref="ThumbWidth"/> px wide as a JPEG by Jellyfin's image processor and stored; a file that cannot be read gets the failure marker (retried after a week). Returns how many thumbnails were stored.
    /// </summary>
    public static async Task<int> MakeMissingAsync(IEnumerable<DocumentFile> files, IImageProcessor processor, ILogger logger, CancellationToken cancellationToken)
    {
        if (Documents.CoverDirectory.Length == 0 || Plugin.Instance?.Configuration.DocumentsGenerateThumbnails != true)
        {
            return 0;
        }

        var todo = files.Where(f => Documents.ServerKind(f) && f.Length > 0 && !Documents.HasThumb(f) && !Documents.HasFailure(f)).ToList();
        var stored = 0;
        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrent, CancellationToken = cancellationToken }, async (file, token) =>
        {
            await Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var jpeg = await ThumbnailAsync(file.Path, processor, token).ConfigureAwait(false);
                if (jpeg.Bytes is not null && Documents.StoreThumb(file, jpeg.Bytes, Documents.CoverDirectory))
                {
                    Interlocked.Increment(ref stored);
                    logger.LogInformation("PlayniteBridge: stored the thumbnail of the comic \"{File}\"", file.FileName);
                }
                else if (jpeg.Bytes is null)
                {
                    // Timeouts, IO errors and a mismatching host library are retried by the next run; other reasons will not change.
                    if (!IsTransient(jpeg.Reason))
                    {
                        Documents.MarkFailed(file, Documents.CoverDirectory);
                    }

                    logger.LogInformation("PlayniteBridge: no thumbnail for the comic \"{File}\": {Reason}", file.FileName, jpeg.Reason);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad file or unexpected host library must not stop the other comics or the task. No marker; the next run tries again.
                logger.LogWarning("PlayniteBridge: comic \"{File}\" skipped: {Error}", file.FileName, ex.GetType().Name);
            }
            finally
            {
                Gate.Release();
            }
        }).ConfigureAwait(false);
        return stored;
    }

    /// <summary>True for a reason that a later run may not meet again: the time limit, an IO error, or a host library whose members do not match the ones this build was made for.</summary>
    public static bool IsTransient(string reason) =>
        reason is "time" || reason.Contains("IOException", StringComparison.Ordinal) || reason.Contains("MissingMethodException", StringComparison.Ordinal)
        || reason.Contains("MissingMemberException", StringComparison.Ordinal) || reason.Contains("TypeLoadException", StringComparison.Ordinal)
        || reason.Contains("FileNotFoundException", StringComparison.Ordinal) || reason.Contains("FileLoadException", StringComparison.Ordinal);

    /// <summary>The thumbnail of the comic at <paramref name="path"/> (a valid thumbnail JPEG, see <see cref="Documents.ValidThumbJpeg"/>) or null bytes and the reason.</summary>
    public static async Task<(byte[]? Bytes, string Reason)> ThumbnailAsync(string path, IImageProcessor processor, CancellationToken cancellationToken)
    {
        var page = await Task.Run(() => FirstPage(path, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (page.Bytes is null)
        {
            return (null, page.Reason);
        }

        if (PixelSize(page.Bytes) is not (int width, int height) || width <= 0 || height <= 0 || (long)width * height > MaxPixels)
        {
            return (null, "picture size");
        }

        // Jellyfin's image processor reads files, so the page goes to a temporary file in the cover folder and is deleted afterwards.
        Directory.CreateDirectory(Documents.CoverDirectory);
        var temporary = Path.Combine(Documents.CoverDirectory, "page-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant() + page.Extension + ".tmp" + page.Extension);
        try
        {
            await File.WriteAllBytesAsync(temporary, page.Bytes, cancellationToken).ConfigureAwait(false);
            // A quality below 100 makes the processor re-encode even a picture that is narrow enough; output is JPEG only.
            var options = ImageCache.ProcessingOptions(processor, temporary, ImageType.Primary, null, null, ThumbWidth, 2000, null, null, 85, ImageFormat.Jpg, Array.Empty<string>(), null, string.Empty);
            var (resultPath, _, _) = await processor.ProcessImage(options).ConfigureAwait(false);
            var bytes = await File.ReadAllBytesAsync(resultPath, cancellationToken).ConfigureAwait(false);
            return Documents.ValidThumbJpeg(bytes) ? (bytes, Ok) : (null, "picture not usable");
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        {
            return (null, "picture: " + ex.GetType().Name);
        }
        finally
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // left for the cover folder pruning
            }
        }
    }

    /// <summary>The width and height in pixels read from the header of a PNG, JPEG, GIF, BMP or WebP picture; null when the header is not one of these or cut short.</summary>
    public static (int Width, int Height)? PixelSize(byte[] data)
    {
        static int Be16(byte[] d, int i) => (d[i] << 8) | d[i + 1];
        static int Be32(byte[] d, int i) => (d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3];
        static int Le16(byte[] d, int i) => d[i] | (d[i + 1] << 8);
        static int Le24(byte[] d, int i) => d[i] | (d[i + 1] << 8) | (d[i + 2] << 16);
        static int Le32(byte[] d, int i) => d[i] | (d[i + 1] << 8) | (d[i + 2] << 16) | (d[i + 3] << 24);

        if (data.Length >= 24 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
        {
            return (Be32(data, 16), Be32(data, 20));
        }

        if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        {
            return (Le16(data, 6), Le16(data, 8));
        }

        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
        {
            // a top-down BMP has a negative height
            var bmpHeight = Le32(data, 22);
            return bmpHeight == int.MinValue ? null : (Math.Abs(Le32(data, 18)), Math.Abs(bmpHeight));
        }

        if (data.Length >= 30 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'X')
            {
                return (Le24(data, 24) + 1, Le24(data, 27) + 1);
            }

            if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == 'L')
            {
                var bits = Le32(data, 21);
                return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            }

            if (data[12] == 'V' && data[13] == 'P' && data[14] == '8' && data[15] == ' ')
            {
                return (Le16(data, 26) & 0x3FFF, Le16(data, 28) & 0x3FFF);
            }

            return null;
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            // walk the segments to the start-of-frame marker
            var i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF)
                {
                    return null;
                }

                var marker = data[i + 1];
                if (marker == 0xFF)
                {
                    i++;
                    continue;
                }

                if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
                {
                    return (Be16(data, i + 7), Be16(data, i + 5));
                }

                i += 2 + Be16(data, i + 2);
            }
        }

        return null;
    }

    /// <summary>A read-only stream that stops with an OperationCanceledException once the token is cancelled (the 30-second limit): the decoder reads the file all the time.</summary>
    private sealed class TimedStream : Stream
    {
        private readonly Stream _inner;
        private readonly CancellationToken _token;

        public TimedStream(Stream inner, CancellationToken token)
        {
            _inner = inner;
            _token = token;
        }

        public override bool CanRead => true;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set
            {
                _token.ThrowIfCancellationRequested();
                _inner.Position = value;
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _token.ThrowIfCancellationRequested();
            return _inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            _token.ThrowIfCancellationRequested();
            return _inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _token.ThrowIfCancellationRequested();
            return _inner.Seek(offset, origin);
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
#endif
