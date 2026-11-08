using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>The own pictures of one collection: its poster file (null without one) and its backdrop files in order (index i = Backdrop/{i}).</summary>
public sealed record CollectionPicture(string? Poster, IReadOnlyList<string> Backdrops)
{
    public static readonly CollectionPicture None = new(null, Array.Empty<string>());
}

/// <summary>
/// The own pictures of the collections (series): a folder with one sub folder per series, named like the series (<see cref="GameExtras.CleanName"/>, case ignored),
/// holding poster.* (else folder.*, cover.*) and backdrop.*, backdrop1.* ... Read only: nothing is created, written or deleted, a missing folder is no
/// own pictures. The path comes from the settings and the stored series name, never from a request; plain files only (no links, no hidden files, no sub
/// folders), at most <see cref="MaxBackdrops"/> backdrops per series, files over <see cref="MaxBytes"/> and empty files ignored. Listings are reused for 10 seconds.
/// </summary>
public static class CollectionPictures
{
    /// <summary>
    /// The tag of an own picture: write time, length AND file name, so two files of one batch (same length, same time) never share a tag; null if the file does not exist.
    /// (<see cref="ImageCache.Tag"/> is for the games' images, whose tags other code compares.)
    /// </summary>
    public static string? Tag(string? path)
    {
        var info = path is null ? null : new FileInfo(path);
        if (info is null || !info.Exists)
        {
            return null;
        }

        var bytes = BitConverter.GetBytes(info.LastWriteTimeUtc.Ticks).Concat(BitConverter.GetBytes(info.Length)).Concat(System.Text.Encoding.UTF8.GetBytes(info.Name.ToLowerInvariant())).ToArray();
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes), 0, 6).ToLowerInvariant();
    }

    /// <summary>The most backdrops read per series.</summary>
    public const int MaxBackdrops = 50;

    /// <summary>The largest picture file read (20 MB).</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    /// <summary>How long a listing is reused (10 seconds).</summary>
    public const long ReuseMilliseconds = 10000;

    // An SVG opened at its image address runs in Jellyfin's origin and a .tbn is no picture format of its own: neither is read as an own picture.
    private static readonly string[] NotShown = { ".svg", ".tbn" };

    private static readonly string[] PosterNames = { "poster", "folder", "cover" };

    // The sub folders per root folder and the pictures per series folder, each with the time it was read.
    private static readonly ConcurrentDictionary<string, (long At, Dictionary<string, string> Folders)> Roots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (long At, CollectionPicture Pictures)> Series = new(StringComparer.OrdinalIgnoreCase);
    private static int _listings;

    /// <summary>The clock in milliseconds (replaceable).</summary>
    public static Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Number of folder listings made.</summary>
    public static int Listings => _listings;

    /// <summary>Forgets every listing.</summary>
    public static void Forget()
    {
        Roots.Clear();
        Series.Clear();
    }

    /// <summary>The folder of the own pictures: the configured one, empty = Jellyfin's internal metadata path + Game Collections (as the game folder's default).</summary>
    public static string Resolve(string? configured, string internalMetadataPath) =>
        string.IsNullOrWhiteSpace(configured) ? Path.Combine(internalMetadataPath, "Game Collections") : configured;

    /// <summary>
    /// Why a folder is refused, or null: the checks of <paramref name="checkPaths"/> (ImageCache.CheckPaths: absolute, no link, not Jellyfin's People, Studio or
    /// library folders, not the Playnite or Steam folder), and not the game folder <paramref name="gameFolder"/> itself, inside it or containing it. A folder that does not exist is no reason.
    /// </summary>
    public static string? FolderError(string folder, string gameFolder, Func<string, string?> checkPaths) =>
        GameExtras.ExtrasFolderError(folder, gameFolder, checkPaths)?.Replace("The extras folder", "The collection pictures folder", StringComparison.Ordinal);

    /// <summary>The own pictures of the series <paramref name="seriesName"/> below <paramref name="folder"/>; <see cref="CollectionPicture.None"/> without a folder, a matching sub folder or files.</summary>
    public static CollectionPicture Read(string? folder, string seriesName)
    {
        var name = GameExtras.CleanName(seriesName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(folder) || name.Length == 0 || AreaCommon.IsDeviceName(name))
        {
            return CollectionPicture.None;
        }

        try
        {
            var now = Clock();
            if (!Roots.TryGetValue(folder, out var root) || now - root.At > ReuseMilliseconds)
            {
                root = (now, ListFolders(folder));
                Roots[folder] = root;
            }

            if (!root.Folders.TryGetValue(name, out var series))
            {
                return CollectionPicture.None;
            }

            if (Series.TryGetValue(series, out var known) && now - known.At <= ReuseMilliseconds)
            {
                return known.Pictures;
            }

            var pictures = ReadSeries(series);
            Series[series] = (now, pictures);
            return pictures;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return CollectionPicture.None;
        }
    }

    // The direct sub folders by name; links and hidden folders are skipped (AreaCommon.ScanOptions), the first of equal names by ordinal order.
    private static Dictionary<string, string> ListFolders(string folder)
    {
        Interlocked_Listings();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder))
        {
            return result;
        }

        foreach (var dir in Directory.EnumerateDirectories(folder, "*", AreaCommon.ScanOptions).OrderBy(d => d, StringComparer.Ordinal))
        {
            result.TryAdd(Path.GetFileName(dir), dir);
        }

        return result;
    }

    private static CollectionPicture ReadSeries(string series)
    {
        Interlocked_Listings();
        var files = new DirectoryInfo(series).EnumerateFiles("*", AreaCommon.ScanOptions)
            .Where(f => f.Length > 0 && f.Length <= MaxBytes && !NotShown.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && GameExtras.SupportedImageExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => Array.IndexOf(GameExtras.SupportedImageExtensions, f.Extension))
            .ToList();
        string? Named(string name) => files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f.Name).Equals(name, StringComparison.OrdinalIgnoreCase))?.FullName;

        var poster = PosterNames.Select(Named).FirstOrDefault(p => p is not null);

        // The plain name, then every numbered one that exists (backdrop1, backdrop2, ... also after a gap: "as many as exist", unlike Jellyfin's rule of three numbers missing), up to MaxBackdrops.
        var backdrops = new List<string>();
        if (Named("backdrop") is string first)
        {
            backdrops.Add(first);
        }

        for (var i = 1; i < MaxBackdrops; i++)
        {
            if (Named("backdrop" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)) is string numbered)
            {
                backdrops.Add(numbered);
            }
        }

        return poster is null && backdrops.Count == 0 ? CollectionPicture.None : new CollectionPicture(poster, backdrops);
    }

    private static void Interlocked_Listings() => System.Threading.Interlocked.Increment(ref _listings);
}
