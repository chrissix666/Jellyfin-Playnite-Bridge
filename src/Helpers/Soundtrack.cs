using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>What the list shows of one soundtrack file: title, track number, artists, length, and the index of the picture embedded in it (null = none).</summary>
public sealed record TrackInfo(string Name, int? Number, IReadOnlyList<string> Artists, long? RunTimeTicks, int? EmbeddedImage);

/// <summary>
/// The soundtrack of a game: the audio files directly in its "soundtrack" folder (see GameExtras.Scan). Tags come from Jellyfin's ffprobe
/// (MediaInfoCache); the file name stands in for a missing title or track number. Nothing here reads or writes a file.
/// </summary>
public static class Soundtrack
{
    /// <summary>The default folder name inside a game's extras folder (case ignored); the setting Soundtrack Folder can name another.</summary>
    public const string DefaultFolder = "soundtrack";

    /// <summary>The configured soundtrack folder name, trimmed; the default when it is empty, not a plain folder name, one of Jellyfin's own folder names (trailers, extras ...) or the folder of another area that is on.</summary>
    public static string FolderOf(Configuration.PluginConfiguration config) =>
        AreaCommon.FolderNameError((config.SoundtrackFolder ?? string.Empty).Trim(), true) is null && !AreaCommon.Clashes(config, "soundtrack", config.SoundtrackFolder!.Trim()) ? config.SoundtrackFolder!.Trim() : DefaultFolder;

    // "01 Name", "01 - Name", "1. Name", "01_Name": 1-3 digits and a separator; "1999 Song" has none after three digits and stays whole.
    private static readonly Regex Numbered = new(@"^(\d{1,3})(?:\s*[-._)]\s*|\s+)(.+)$", RegexOptions.Compiled);

    /// <summary>The cover names of the folder, in the order of preference (the extensions follow GameExtras.SupportedImageExtensions).</summary>
    public static readonly string[] CoverNames = { "cover", "folder", "front", "albumart", "poster" };

    /// <summary>
    /// The info of a track: the tag title, else the file name without extension and without its leading number; the tag track number, else that leading number.
    /// Null when ffprobe gave nothing (<paramref name="media"/> null) or the file has no audio stream: such a file is not a track.
    /// </summary>
    public static TrackInfo? Info(GameExtra track, MediaInfo? media)
    {
        if (media is null || media.MediaStreams?.Any(s => s.Type == MediaStreamType.Audio) != true)
        {
            return null;
        }

        var stem = System.IO.Path.GetFileNameWithoutExtension(track.Path);
        var match = Numbered.Match(stem);
        var fileName = match.Success ? match.Groups[2].Value.Trim() : stem;
        var fileNumber = match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        var embedded = media.MediaStreams.Where(s => s.Type == MediaStreamType.EmbeddedImage).ToList();
        // Same choice as Jellyfin's AudioImageProvider: the picture whose comment says front or cover, else the first.
        var picture = embedded.FirstOrDefault(s => s.Comment?.Contains("front", StringComparison.OrdinalIgnoreCase) == true
            || s.Comment?.Contains("cover", StringComparison.OrdinalIgnoreCase) == true) ?? embedded.FirstOrDefault();
        return new TrackInfo(
            string.IsNullOrWhiteSpace(media.Name) ? fileName : media.Name.Trim(),
            media.IndexNumber > 0 ? media.IndexNumber : fileNumber,
            media.Artists?.Where(a => !string.IsNullOrWhiteSpace(a)).ToArray() ?? Array.Empty<string>(),
            media.RunTimeTicks > 0 ? media.RunTimeTicks : null,
            picture?.Index);
    }

    /// <summary>The order of the list: track number (those without last), then title.</summary>
    public static IOrderedEnumerable<(GameExtra Track, TrackInfo Info)> Order(IEnumerable<(GameExtra Track, TrackInfo Info)> tracks) =>
        tracks.OrderBy(t => t.Info.Number ?? int.MaxValue).ThenBy(t => t.Info.Name, StringComparer.OrdinalIgnoreCase);
}
