using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Trickplay for local extra videos that have none, in Jellyfin's "save with media" layout (TrickplayManager):
/// &lt;base name&gt;.trickplay\&lt;width&gt; - &lt;cols&gt;x&lt;rows&gt;\0.jpg, 1.jpg ... next to the video. Planning is pure; generation takes the
/// frame extraction and the tile encoder as functions and never uses Jellyfin's TrickplayManager or its database.
/// An existing &lt;base name&gt;.trickplay entry of any kind is never read, changed, moved or deleted.
/// </summary>
public static class TrickplayGenerator
{
    // Videos whose generation failed, per path with the ImageCache.Tag (time and length) they had then. Memory only.
    private static readonly ConcurrentDictionary<string, string> FailedVideos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The trickplay folder of a video: &lt;folder of the video&gt;\&lt;base name&gt;.trickplay.</summary>
    public static string FolderOf(string videoPath) =>
        Path.Combine(Path.GetDirectoryName(videoPath)!, Path.GetFileNameWithoutExtension(videoPath) + ".trickplay");

    /// <summary>Whether anything exists at <paramref name="path"/>: folder, file, junction or link (also a broken one); an unreadable entry counts as existing.</summary>
    public static bool Exists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// The extras to generate for: none while <paramref name="generate"/> is off; else every video extra of the index without an entry
    /// &lt;base name&gt;.trickplay, never a theme song and never a video in a folder "backdrops", and never a video
    /// whose generation failed in its current state (<see cref="MarkFailed"/>).
    /// </summary>
    public static IReadOnlyList<GameExtra> Plan(ExtrasIndex index, bool generate) => !generate
        ? Array.Empty<GameExtra>()
        : index.Folders.SelectMany(f => f.Extras)
            .Where(e => e.Kind != ExtraKind.ThemeSong && e.Kind != ExtraKind.Soundtrack
                && !string.Equals(Path.GetFileName(Path.GetDirectoryName(e.Path)), "backdrops", StringComparison.OrdinalIgnoreCase)
                && !Exists(FolderOf(e.Path))
                && !(FailedVideos.TryGetValue(e.Path, out var failed) && failed == ImageCache.Tag(e.Path)))
            .ToList();

    /// <summary>Remembers that the generation of <paramref name="videoPath"/> failed in its current state; planned again once it changes.</summary>
    public static void MarkFailed(string videoPath)
    {
        if (ImageCache.Tag(videoPath) is string tag)
        {
            FailedVideos[videoPath] = tag;
        }
    }

    /// <summary>Forgets a failure of <paramref name="videoPath"/> (its generation succeeded).</summary>
    public static void ClearFailed(string videoPath) => FailedVideos.TryRemove(videoPath, out _);

    /// <summary>The interval in ms: the server's, at least 1000; the configuration itself is not changed.</summary>
    public static int Interval(int configured) => Math.Max(1000, configured);

    /// <summary>
    /// The thumbnail widths: each configured width made even, at most the even video width; widths of 0 left out,
    /// each width once, in the configured order.
    /// </summary>
    public static IReadOnlyList<int> Widths(IEnumerable<int> configured, int? videoWidth) => configured
        .Select(w => videoWidth is int v && v < w ? 2 * (v / 2) : 2 * (w / 2))
        .Where(w => w > 0)
        .Distinct()
        .ToList();

    /// <summary>The number of sheets for <paramref name="images"/> thumbnails of <paramref name="cols"/> x <paramref name="rows"/> each.</summary>
    public static int Sheets(int images, int cols, int rows) => (images + (cols * rows) - 1) / (cols * rows);

    /// <summary>
    /// Generates the trickplay folder of <paramref name="video"/>: per width the thumbnails of <paramref name="extract"/> (a folder of *.jpg it made,
    /// deleted afterwards), the sheets by <paramref name="createTile"/> (input paths, output path, thumbnail width, known thumbnail height or null;
    /// returns the thumbnail height) in a work folder of its own under <paramref name="tempRoot"/>. Only after the last sheet the finished tree is
    /// moved next to the video: Directory.Move, across volumes a copy into a uniquely named folder beside the video, then renamed. True when it
    /// was moved in; false when the trickplay folder appeared meanwhile (it wins, the own tree is discarded). Every temporary folder is removed,
    /// also on failure or cancellation.
    /// </summary>
    public static async Task<bool> Generate(
        string video,
        IReadOnlyList<int> widths,
        int cols,
        int rows,
        string tempRoot,
        Func<int, CancellationToken, Task<string>> extract,
        Func<IReadOnlyList<string>, string, int, int?, int> createTile,
        CancellationToken cancellationToken)
    {
        var target = FolderOf(video);
        var work = Path.Combine(tempRoot, "playnite-bridge-trickplay-" + Guid.NewGuid().ToString("N"));
        var tree = Path.Combine(work, Path.GetFileName(target));
        string? staging = null;
        try
        {
            foreach (var width in widths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var images = await extract(width, cancellationToken).ConfigureAwait(false);
                try
                {
                    var files = Directory.GetFiles(images, "*.jpg").OrderBy(f => f, StringComparer.Ordinal).ToList();
                    if (files.Count == 0)
                    {
                        throw new InvalidOperationException("ffmpeg made no thumbnails");
                    }

                    var layout = Directory.CreateDirectory(Path.Combine(tree, string.Format(CultureInfo.InvariantCulture, "{0} - {1}x{2}", width, cols, rows))).FullName;
                    var perSheet = cols * rows;
                    int? height = null;
                    for (var i = 0; i < Sheets(files.Count, cols, rows); i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // The thumbnail height of the first sheet is passed on to the others.
                        var sheetHeight = createTile(files.Skip(i * perSheet).Take(perSheet).ToList(), Path.Combine(layout, i.ToString(CultureInfo.InvariantCulture) + ".jpg"), width, height);
                        height ??= sheetHeight;
                    }
                }
                finally
                {
                    TryDelete(images);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Exists(target))
            {
                return false;
            }

            try
            {
                Directory.Move(tree, target);
                return true;
            }
            catch (IOException) when (Exists(target))
            {
                return false;
            }
            catch (IOException)
            {
                // Another volume: copy beside the video under a name the extras scan does not read as trickplay, then rename.
            }

            staging = Path.Combine(Path.GetDirectoryName(target)!, ".playnite-bridge-trickplay-" + Guid.NewGuid().ToString("N"));
            CopyTree(tree, staging);
            try
            {
                Directory.Move(staging, target);
                staging = null;
                return true;
            }
            catch (IOException) when (Exists(target))
            {
                return false;
            }
        }
        finally
        {
            TryDelete(work);
            if (staging is not null)
            {
                TryDelete(staging);
            }
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), false);
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
