using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// One &lt;media&gt; block of a game.nfo: Kind "tmdb" (movie), "tvdb" (series) or "tmdbset" (TMDb collection), its numeric id and its name (&lt;moviename&gt;,
/// &lt;showname&gt;, &lt;setname&gt;); either may be empty, never both. The id has priority, the name is used only when the id is empty.
/// </summary>
public sealed record NfoMedia(string Kind, string Id, string Name);

/// <summary>One &lt;actor&gt; block of a game.nfo.</summary>
public sealed record NfoActor(string Name, string? Role);

/// <summary>The links of a game.nfo in display order (&lt;order&gt;, else file order).</summary>
public sealed record GameNfoContent(IReadOnlyList<NfoMedia> Media, IReadOnlyList<NfoActor> Actors);

/// <summary>
/// Reads game.nfo: only the root's &lt;media&gt; and &lt;actor&gt; children count. The parsed content is kept
/// in memory and read again when the file's last-write time or length changes. Nothing is ever written.
/// </summary>
public static class GameNfo
{
    public const string FileName = "game.nfo";

    private const long MaxBytes = 1_000_000;
    private const int MaxMedia = 100;
    public const int MaxActors = 200;

    private static readonly ConcurrentDictionary<string, (DateTime Written, long Length, GameNfoContent? Content)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] MediaKinds = { "tmdb", "tvdb", "tmdbset" };

    // The name field that belongs to the id field of a kind.
    private static readonly Dictionary<string, string> NameFields = new() { ["tmdb"] = "moviename", ["tvdb"] = "showname", ["tmdbset"] = "setname" };

    /// <summary>The content of the file at <paramref name="path"/>; null when there is no file or it is no valid XML (one log line per change of the file).</summary>
    public static GameNfoContent? Read(string path, ILogger? logger)
    {
        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
            {
                Cache.TryRemove(path, out _);
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }

        if (Cache.TryGetValue(path, out var kept) && kept.Written == file.LastWriteTimeUtc && kept.Length == file.Length)
        {
            return kept.Content;
        }

        GameNfoContent? content = null;
        try
        {
            if (file.Length > MaxBytes)
            {
                throw new InvalidDataException("the file is larger than 1 MB");
            }

            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes };
            using var reader = XmlReader.Create(path, settings);
            content = Parse(XDocument.Load(reader));
        }
        catch (Exception ex)
        {
            logger?.LogWarning("PlayniteBridge: cannot read {File}: {Message}", path, ex.Message);
        }

        Cache[path] = (file.LastWriteTimeUtc, file.Length, content);
        return content;
    }

    /// <summary>The media and actors of a parsed document in display order; a media block without a numeric id and without a name is left out.</summary>
    public static GameNfoContent Parse(XDocument document)
    {
        var root = document.Root;
        var children = root is null ? new List<XElement>() : root.Elements().ToList();
        var media = children.Where(e => e.Name.LocalName == "media").Select((e, index) =>
        {
            var idElement = e.Elements().FirstOrDefault(c => MediaKinds.Contains(c.Name.LocalName));
            var kind = idElement?.Name.LocalName ?? MediaKinds.FirstOrDefault(k => e.Element(NameFields[k]) is not null);
            var id = idElement?.Value.Trim() ?? string.Empty;
            var name = kind is null ? string.Empty : e.Element(NameFields[kind])?.Value.Trim() ?? string.Empty;
            // the digit test is for the id only; a block needs an id or a name
            var usable = kind is not null && (id.Length == 0 ? name.Length > 0 : id.All(char.IsAsciiDigit));
            return (Item: usable ? new NfoMedia(kind!, id, name) : null, Key: OrderOf(e, index), index);
        });
        var actors = children.Where(e => e.Name.LocalName == "actor").Select((e, index) =>
        {
            var name = e.Element("name")?.Value.Trim() ?? string.Empty;
            var role = e.Element("role")?.Value.Trim();
            return (Item: name.Length == 0 ? null : new NfoActor(name, string.IsNullOrEmpty(role) ? null : role), Key: OrderOf(e, index), index);
        });
        return new GameNfoContent(
            media.Where(m => m.Item is not null).OrderBy(m => m.Key).ThenBy(m => m.index).Select(m => m.Item!).Take(MaxMedia).ToArray(),
            actors.Where(a => a.Item is not null).OrderBy(a => a.Key).ThenBy(a => a.index).Select(a => a.Item!).Take(MaxActors).ToArray());
    }

    private static readonly string[] ActorImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    /// <summary>
    /// The picture of an actor in the folder ".actors" of a game folder (Kodi's local information): the actor's name with every space turned into "_" plus
    /// .jpg, .jpeg, .png or .webp (case ignored, the first in that order); null without one. The folder's files are compared by name, so the actor's name never
    /// reaches a path.
    /// </summary>
    public static string? ActorImage(string gameFolder, string actorName)
    {
        try
        {
            var folder = new DirectoryInfo(Path.Combine(gameFolder, GameExtras.ActorsFolder));
            if (!folder.Exists || (folder.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            // a junction or symbolic link as ".actors" and hidden entries are not read (as the extras scan leaves them out)
            var stem = actorName.Replace(' ', '_');
            var files = folder.EnumerateFiles("*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden }).Where(f => f.Length > 0).ToList();
            return ActorImageExtensions.Select(extension => files.FirstOrDefault(f => string.Equals(f.Name, stem + extension, StringComparison.OrdinalIgnoreCase))?.FullName)
                .FirstOrDefault(path => path is not null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The media type of an actor picture file.</summary>
    public static string ImageType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };

    // <order> as a number, else the block's place in the file.
    private static int OrderOf(XElement block, int index) =>
        int.TryParse(block.Element("order")?.Value.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var order) ? order : index;
}
