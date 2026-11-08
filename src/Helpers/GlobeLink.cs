using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PlayniteBridge;

/// <summary>
/// The read-only interface that Globe finds by reflection, next to <see cref="ArtworkPlusLink"/>. Names, signatures and meaning must stay
/// stable, and there are no overloads (Type.GetMethod(name) has to find exactly one). Only simple types cross the plugin boundary.
/// Answers come from the registered <see cref="VirtualItemsFilter"/> (created by Jellyfin at the first MVC request) through
/// <see cref="GlobeLinkCore"/>. Before that, without the plugin, with a rejected Playnite folder, without a copy of Playnite's data and on
/// any error the answer is "", null or an empty array. Nothing is written, created or copied. No user is involved: the lists hold every
/// game the library holds, and who may see which game is up to the caller.
/// </summary>
public static class GlobeLink
{
    private static VirtualItemsFilter? _filter;

    // Part of the stamp, so a stamp from an earlier server run never equals a later one.
    private static readonly long StartTicks = DateTime.UtcNow.Ticks;

    /// <summary>True once the link answers: the plugin is loaded and its filter exists (made at the server start). False before that; a missing plugin shows as the missing type.</summary>
    public static bool Ready() => Plugin.Instance is not null && Volatile.Read(ref _filter) is not null;

    /// <summary>The id ("N") of the games library; "" while there is none, no plugin or no Playnite data.</summary>
    public static string GamesLibraryId() =>
        Answer(string.Empty, (filter, _) => filter.GamesLibrary() is Guid id ? id.ToString("N") : string.Empty, null);

    /// <summary>
    /// Every game of the library <paramref name="libraryId"/> (it must be the games library; else empty), each version and each twin as its own row, in library order (groups
    /// as the library shows them, in a group the main version first, then the other versions, then the twins). Row = [0 id "N", 1 name (the group's title as the tile shows it),
    /// 2 sort name, 3 added "yyyy-MM-ddTHH:mm:ss" or "", 4 release date "yyyy-MM-dd" or "", 5 community rating "0.0" (score / 10) or "", 6 official rating (Playnite's first age
    /// rating text) or "", 7 strictest rating score or "", 8 its sub-score or "" (always "" on Jellyfin 10.10), 9 id "N" of the group's main version, 10 "true" for the main
    /// version, 11 "true" for a twin, 12 "true" when the entry has its own cover]. All hidden and grouping settings act as they do for the library.
    /// </summary>
    public static string[][] Games(Guid libraryId) =>
        Answer(Array.Empty<string[]>(), (filter, data) => GlobeLinkCore.Games(data, filter.Images(), libraryId, filter.GamesLibrary(), filter.StrictestRating()), null);

    /// <summary>[path, tag] of the game's own file of <paramref name="type"/> ("Primary", "Logo", "Banner", "Thumb", "Art" or "Disc" (the first disc)); null for any other type, without a file or for any other id.</summary>
    public static string[]? Image(Guid id, string type) =>
        Answer(null, (filter, data) => GlobeLinkCore.Image(data, filter.Images(), id, type), id);

    /// <summary>All disc files of the game as [path, tag]: the plain names (disc, cdart, discart, the configured one) that exist, then the numbered ones; element 0 is <see cref="Image"/> of "Disc". Empty for any other id.</summary>
    public static string[][] Discs(Guid id) =>
        Answer(Array.Empty<string[]>(), (filter, data) => GlobeLinkCore.Discs(data, filter.Images(), id), id);

    /// <summary>The game's backdrops as [path, tag] in Jellyfin's order (element i = Backdrop/{i}, element 0 the main one); the same answer as ArtworkPlusLink.Backdrops. Empty for any other id.</summary>
    public static string[][] Backdrops(Guid id) =>
        Answer(Array.Empty<string[]>(), (filter, data) => ArtworkPlusLinkCore.Backdrops(data, filter.Images(), id), id);

    /// <summary>
    /// The collections (series with games in at least two groups) of the library <paramref name="libraryId"/> (it must be the games library; else empty) in the order the library lists them.
    /// Row = [0 id "N", 1 name, 2 sort name (the name), 3 id "N" of the game whose cover is the collection's poster (the Poster Order setting; "" when no game has a cover), 4 number of groups].
    /// The collection's own pictures (a folder per series) are <see cref="ArtworkPlusLink.CollectionImages"/>; its games are <see cref="CollectionGames"/>.
    /// </summary>
    public static string[][] Collections(Guid libraryId) =>
        Answer(Array.Empty<string[]>(), (filter, data) => GlobeLinkCore.Collections(data, libraryId, filter.GamesLibrary()), null);

    /// <summary>
    /// Every game of a collection (a series with games in at least two groups) that carries the series, the versions that are not the main one included (a version without the series is not listed, nor is a twin),
    /// in sort name order. Row = [0 id "N", 1 name (the group's title), 2 sort name, 3 release date "yyyy-MM-dd" or "", 4 id "N" of the group's main version, 5 "true" for the main version]. Empty for an id that is no collection.
    /// </summary>
    public static string[][] CollectionGames(Guid collectionId) =>
        Answer(Array.Empty<string[]>(), (_, data) => GlobeLinkCore.CollectionGames(data, collectionId), collectionId);

    /// <summary>
    /// The original file of a picture, as stored (never resized, so an animated picture stays animated): [path, tag, content type] for <paramref name="type"/> as <see cref="Image"/>
    /// takes it, or "Disc" / "Backdrop" with <paramref name="index"/> (0-based, as <see cref="Discs"/> and <see cref="Backdrops"/> list them; the index is ignored for the other types).
    /// The path comes from the plugin's own index of the game's files, never from a request; null for any other type, index or id.
    /// </summary>
    public static string[]? Original(Guid id, string type, int index) =>
        Answer(null, (filter, data) => GlobeLinkCore.Original(data, filter.Images(), id, type, index), id);

    /// <summary>
    /// An opaque stamp of the data the answers are made from: it changes after every sync of Playnite's data (Sync Metadata), after every end of Sync Artwork and after every save of the plugin's settings,
    /// and differs between server runs. It does not change when someone puts or removes a file in a game folder by hand: the file lists are read again every 10 seconds, so ask again after that time when
    /// such a change matters. Compare it for inequality only. "" without the plugin.
    /// </summary>
    public static string DataStamp()
    {
        try
        {
            return Plugin.Instance is null
                ? string.Empty
                : string.Join(".", StartTicks.ToString("x", CultureInfo.InvariantCulture), PlayniteSnapshot.Generation.ToString(CultureInfo.InvariantCulture),
                    Plugin.ConfigurationSaves.ToString(CultureInfo.InvariantCulture), ImageCache.Flushes.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Called by the filter's constructor.</summary>
    internal static void Register(VirtualItemsFilter filter) => Volatile.Write(ref _filter, filter);

    // Returns safe unless the plugin, the filter and an accepted Playnite folder exist. With an id, the game data is the one the filter resolves that id in; without one, the current game data.
    private static T Answer<T>(T safe, Func<VirtualItemsFilter, GameData, T> answer, Guid? id)
    {
        try
        {
            var filter = Volatile.Read(ref _filter);
            if (filter is null || Plugin.Instance is not Plugin plugin || PlayniteReader.ResolveFolder(plugin.Configuration.PlayniteFolder) is null)
            {
                return safe;
            }

            GameData data;
            if (id is Guid gameId)
            {
                filter.Find(gameId, out data);
            }
            else
            {
                data = filter.Data();
            }

            return answer(filter, data);
        }
        catch (Exception)
        {
            return safe;
        }
    }
}

/// <summary>
/// The answers of <see cref="GlobeLink"/> computed from given game data and image cache. Pure: it answers from the data it is given.
/// </summary>
public static class GlobeLinkCore
{
    private static string N(string id) => Guid.Parse(id).ToString("N");

    private static string Date(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>The rows of <see cref="GlobeLink.Games"/>; empty unless <paramref name="libraryId"/> is <paramref name="gamesLibrary"/>.</summary>
    public static string[][] Games(GameData data, ImageCache images, Guid libraryId, Guid? gamesLibrary, Func<GameEntry, string[]> rating)
    {
        if (libraryId == Guid.Empty || libraryId != gamesLibrary)
        {
            return Array.Empty<string[]>();
        }

        var rows = new List<string[]>();
        foreach (var group in data.Groups)
        {
            // A group or game that cannot be turned into a row (a bad id, a failing lookup) is left out; the other rows stay.
            try
            {
                var mainId = N(group.Main.Id);
                var twins = (group.Twins ?? new Dictionary<string, string>()).Keys
                    .Select(twin => data.Find(Guid.Parse(twin)) is { Kind: GameHitKind.Game } hit ? hit.Game : null).OfType<GameEntry>();
                foreach (var (game, isTwin) in group.Versions.Select(v => (v, false)).Concat(twins.Select(t => (t, true))))
                {
                    if (GameRow(data, images, group, game, isTwin, mainId, rating) is string[] row)
                    {
                        rows.Add(row);
                    }
                }
            }
            catch (Exception)
            {
                // the group is left out
            }
        }

        return rows.ToArray();
    }

    // One row of Games; null for a game Jellyfin cannot hold as an item (Image, Discs and Original refuse it too) or one that cannot be read.
    private static string[]? GameRow(GameData data, ImageCache images, GameGroup group, GameEntry game, bool isTwin, string mainId, Func<GameEntry, string[]> rating)
    {
        try
        {
            if (data.Find(Guid.Parse(game.Id)) is not { Kind: GameHitKind.Game })
            {
                return null;
            }

            string[] strictest;
            try
            {
                strictest = rating(game);
            }
            catch (Exception)
            {
                strictest = new[] { string.Empty, string.Empty };
            }

            return new[]
            {
                N(game.Id),
                game.Name,
                GameQuery.SortName(game),
                game.Added?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) ?? string.Empty,
                Date(game.ReleaseDate),
                game.CommunityScore is int score ? (score / 10f).ToString("0.0", CultureInfo.InvariantCulture) : string.Empty,
                GameQuery.OfficialRating(game) ?? string.Empty,
                strictest[0],
                strictest[1],
                mainId,
                (!isTwin && game.Id == group.Main.Id) ? "true" : "false",
                isTwin ? "true" : "false",
                VirtualItemsFilter.TaggedImage(images, group, game, ImageType.Primary) is not null ? "true" : "false"
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The game of <paramref name="id"/> (any version or twin) with its group; null for any other id.</summary>
    private static GameHit? Game(GameData data, Guid id) => ArtworkPlusLinkCore.Game(data, id);

    /// <summary>[path, tag] of the game's own file of the type; null for any other type or id.</summary>
    public static string[]? Image(GameData data, ImageCache images, Guid id, string? type)
    {
        ImageType? imageType = type switch
        {
            "Primary" => ImageType.Primary,
            "Logo" => ImageType.Logo,
            "Banner" => ImageType.Banner,
            "Thumb" => ImageType.Thumb,
            "Art" => ImageType.Art,
            "Disc" => ImageType.Disc,
            _ => null
        };
        return imageType is not null && Game(data, id) is GameHit hit && VirtualItemsFilter.TaggedImage(images, hit.Group!, hit.Game!, imageType.Value) is var (path, tag)
            ? new[] { path, tag }
            : null;
    }

    /// <summary>All disc files of the game as [path, tag]; only files with a tag.</summary>
    public static string[][] Discs(GameData data, ImageCache images, Guid id) => Game(data, id) is GameHit hit
        ? images.Discs(hit.Group!, hit.Game!).Select(path => (Path: path, Tag: ImageCache.Tag(path))).Where(d => d.Tag is not null).Select(d => new[] { d.Path, d.Tag! }).ToArray()
        : Array.Empty<string[]>();

    /// <summary>The rows of <see cref="GlobeLink.Collections"/>; empty unless <paramref name="libraryId"/> is <paramref name="gamesLibrary"/>.</summary>
    public static string[][] Collections(GameData data, Guid libraryId, Guid? gamesLibrary)
    {
        if (libraryId == Guid.Empty || libraryId != gamesLibrary)
        {
            return Array.Empty<string[]>();
        }

        return data.Collections.Select(collection =>
        {
            try
            {
                return new[]
                {
                    collection.Id.ToString("N"),
                    collection.Name,
                    collection.Name,
                    collection.Poster is null ? string.Empty : N(collection.Poster.Id),
                    collection.GroupCount.ToString(CultureInfo.InvariantCulture)
                };
            }
            catch (Exception)
            {
                return null;
            }
        }).OfType<string[]>().ToArray();
    }

    /// <summary>The rows of <see cref="GlobeLink.CollectionGames"/>.</summary>
    public static string[][] CollectionGames(GameData data, Guid id)
    {
        if (ArtworkPlusLinkCore.Collection(data, id) is not GameCollection collection)
        {
            return Array.Empty<string[]>();
        }

        return collection.Games.DistinctBy(g => g.Id).OrderBy(g => GameQuery.SortName(g), StringComparer.OrdinalIgnoreCase).Select(game =>
        {
            // a game that cannot be read (a bad id, no group) is left out; the other rows stay
            try
            {
                var group = data.GroupOf(game);
                return new[] { N(game.Id), game.Name, GameQuery.SortName(game), Date(game.ReleaseDate), N(group.Main.Id), game.Id == group.Main.Id ? "true" : "false" };
            }
            catch (Exception)
            {
                return null;
            }
        }).OfType<string[]>().ToArray();
    }

    /// <summary>[path, tag, content type] of the original file.</summary>
    public static string[]? Original(GameData data, ImageCache images, Guid id, string? type, int index)
    {
        string[]? file = type switch
        {
            "Backdrop" => ArtworkPlusLinkCore.Backdrops(data, images, id).ElementAtOrDefault(index),
            "Disc" => Discs(data, images, id).ElementAtOrDefault(index),
            _ => Image(data, images, id, type)
        };
        return file is null ? null : new[] { file[0], file[1], ContentType(file[0]) };
    }

    private static string ContentType(string path) => GamePictures.ContentType(path);
}
