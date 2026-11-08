using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The single pictures of a game that Jellyfin has no image type for (square, the four boxes, icon) and its discs, found in the game's own folder (ImageCache), for the routes
/// games/{id}/pictures and games/{id}/discs. Pure: no request, nothing written. The path comes from the plugin's own index of the game folder, never from a request.
/// </summary>
public static class GamePictures
{
    /// <summary>The picture types of games/{id}/pictures/{type}.</summary>
    public static readonly string[] Types = { "square", "box3d", "boxfront", "boxback", "boxspine", "icon" };

    /// <summary>The file of <paramref name="type"/> of the game <paramref name="id"/> (any version or twin, its own folder); null for any other type or id, or without a file.</summary>
    public static string? Picture(GameData data, ImageCache images, Guid id, string? type)
    {
        if (type is null || !Types.Contains(type, StringComparer.Ordinal) || ArtworkPlusLinkCore.Game(data, id) is not GameHit hit)
        {
            return null;
        }

        return type switch
        {
            "square" => images.Square(hit.Game!),
            "icon" => images.Icon(hit.Group!, hit.Game!),
            _ => images.Box(hit.Game!, type)
        };
    }

    /// <summary>The disc files of the game <paramref name="id"/> in order without repeats (<see cref="ImageCache.DistinctDiscs"/>); empty for any other id.</summary>
    public static IReadOnlyList<string> Discs(GameData data, ImageCache images, Guid id) =>
        ArtworkPlusLinkCore.Game(data, id) is GameHit hit ? images.DistinctDiscs(hit.Group!, hit.Game!) : Array.Empty<string>();

    /// <summary>Disc number <paramref name="index"/> (0-based) of <see cref="Discs"/>; null out of range.</summary>
    public static string? Disc(GameData data, ImageCache images, Guid id, int index) =>
        index >= 0 ? Discs(data, images, id).ElementAtOrDefault(index) : null;

    /// <summary>The media type of an image file by its extension.</summary>
    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".apng" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "image/jpeg"
    };
}
