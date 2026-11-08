using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Answers a file as bytes read once. A <see cref="PhysicalFileResult"/> sets Content-Length first and sends the file later, so a file
/// rewritten in between (image cache, replaced cover) fails with a Content-Length mismatch and a deleted one with an error.
/// Here the file is read to its end with sharing allowed and the length sent is the length actually read.
/// </summary>
public static class StableFile
{
    /// <summary>Files up to this size are read into memory; a larger one is sent from the disk as before (images are a few hundred kilobytes).</summary>
    public const long MemoryLimit = 20 * 1024 * 1024;

    // An empty file, or one that changes while it is read, is retried this many times, this long apart.
    private const int Attempts = 4;
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(40);

    /// <summary>The bytes of <paramref name="path"/> as a file answer; <see cref="NotFoundResult"/> when the file is missing, gone meanwhile or stays empty.</summary>
    public static async Task<IActionResult> Result(string path, string mimeType)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return new NotFoundResult();
                }

                if (info.Length > MemoryLimit)
                {
                    return new PhysicalFileResult(path, mimeType);
                }

                byte[] bytes;
                long before;
                await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true))
                {
                    using var memory = new MemoryStream();
                    await stream.CopyToAsync(memory).ConfigureAwait(false);
                    bytes = memory.ToArray();
                    before = stream.Length;
                }

                // Accept the bytes only if the file still has the length that was read; otherwise read again (the last attempt sends what it got).
                if (bytes.Length > 0 && (bytes.Length == before && new FileInfo(path).Length == bytes.Length || attempt == Attempts - 1))
                {
                    return new FileContentResult(bytes, mimeType);
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new NotFoundResult();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // locked for a moment: retry
            }

            await Task.Delay(Pause).ConfigureAwait(false);
        }

        return new NotFoundResult();
    }
}
