using System;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.PlayniteBridge.Configuration;
using Jellyfin.Plugin.PlayniteBridge.Filters;
using Jellyfin.Plugin.PlayniteBridge.Helpers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.PlayniteBridge;

/// <summary>
/// The read-only interface that ArtworkPlus finds by reflection. Names, signatures and meaning must stay stable, and there are no
/// overloads (Type.GetMethod(name) has to find exactly one). Only simple types cross the plugin boundary.
/// Answers come from the registered <see cref="VirtualItemsFilter"/> (created by Jellyfin at the first MVC request) through
/// <see cref="ArtworkPlusLinkCore"/>. Before that, without the plugin, with a rejected Playnite folder, without a copy of Playnite's
/// data and on any error the answer is false, null or an empty array. Nothing is written, created or copied.
/// </summary>
public static class ArtworkPlusLink
{
    private static VirtualItemsFilter? _filter;

    /// <summary>True for a game id (any version or twin) of the game data; false for any other id.</summary>
    public static bool IsGame(Guid id) =>
        Answer(false, (_, data) => ArtworkPlusLinkCore.IsGame(data, id), id);

    /// <summary>The full path of the existing game folder of the game itself (its own, never another version's); null without one, with a refused cache folder or for any other id.</summary>
    public static string? GameFolder(Guid id) =>
        Answer(null, (filter, data) => ArtworkPlusLinkCore.GameFolder(data, filter.Images(), id), id);

    /// <summary>[path, tag] of the game's "Logo" or "Art" file as its ImageTags carry it; null for any other type, without a file or for any other id.</summary>
    public static string[]? Image(Guid id, string type) =>
        Answer(null, (filter, data) => ArtworkPlusLinkCore.Image(data, filter.Images(), id, type), id);

    /// <summary>The base file names (no extension) the settings choose for a type, in priority; only "CharacterArt" for now, ["characterart"] by default; empty for any other type. Numbered files (name1, name2 ...) follow the same name.</summary>
    public static string[] FileNames(string type)
    {
        var config = Plugin.Instance?.Configuration;
        return config is not null && string.Equals(type, "CharacterArt", StringComparison.OrdinalIgnoreCase) ? new[] { CharacterArtFiles.Name(config) } : Array.Empty<string>();
    }

    /// <summary>The game's backdrops as [path, tag] in the order of its BackdropImageTags (index i = Backdrop/{i}); empty for any other id.</summary>
    public static string[][] Backdrops(Guid id) =>
        Answer(Array.Empty<string[]>(), (filter, data) => ArtworkPlusLinkCore.Backdrops(data, filter.Images(), id), id);

    /// <summary>True when the user may open the game as the filter answers that user's detail request; false for any other id or user.</summary>
    public static bool IsVisibleTo(Guid id, Guid userId) =>
        Answer(false, (filter, data) => ArtworkPlusLinkCore.IsVisibleTo(data, id, userId, filter.VisibleUser,
            Plugin.Instance!.Configuration.ApplyParentalControls ? filter.ParentalDataFor : null), id);

    /// <summary>True for the id of a collection (a series with games in at least two groups) of the current game data; false for a game, a library or any other id.</summary>
    public static bool IsCollection(Guid id) =>
        Answer(false, (_, data) => ArtworkPlusLinkCore.IsCollection(data, id), id);

    /// <summary>
    /// The games of a collection the user may see, the main version of each group once, as [id ("N"), release date "yyyy-MM-dd" or "" without one, sort name] in sort
    /// name order (each game independent: its backdrops are <see cref="Backdrops"/> of that id). Parental control applies (as <see cref="IsVisibleTo"/>); empty for an id that
    /// is no collection, a user who may not see the games library or any error.
    /// </summary>
    public static string[][] CollectionMembers(Guid collectionId, Guid userId) =>
        Answer(Array.Empty<string[]>(), (filter, data) => ArtworkPlusLinkCore.CollectionMembers(data, collectionId, userId, filter.VisibleUser,
            Plugin.Instance!.Configuration.ApplyParentalControls ? filter.ParentalDataFor : null), collectionId);

    /// <summary>
    /// The own pictures of a collection (folder CollectionPicturesFolder, one sub folder per series): <paramref name="type"/> "Backdrop" = backdrop, backdrop1 ... as
    /// [path, tag] in that order (index i = Backdrop/{i}), "Poster" = at most one [path, tag]; empty without the folder, without files, for any other type or id. The path never comes from a request.
    /// </summary>
    public static string[][] CollectionImages(Guid collectionId, string type) =>
        Answer(Array.Empty<string[]>(), (filter, data) => ArtworkPlusLinkCore.CollectionImages(data, collectionId, type, filter.OwnPictures), collectionId);

    /// <summary>
    /// One entry per games library tile the user may see that has a backdrop, as [id ("N"), path, tag, favourite ("true"/"false")]: per group of
    /// the current game data in its order, the main version's first backdrop as Backdrops(id) gives it (Backdrop/0) and its favourite flag
    /// (UserData.IsFavorite of the tile); ArtworkPlus picks, shuffles and limits as Jellyfin's autoBackdrops. Empty unless
    /// <paramref name="libraryId"/> is the games library and the user may see it.
    /// </summary>
    public static string[][] LibraryBackdrops(Guid libraryId, Guid userId) =>
        Answer(Array.Empty<string[]>(), (filter, data) => ArtworkPlusLinkCore.LibraryBackdrops(data, filter.Images(), libraryId, filter.GamesLibrary(), userId,
            filter.VisibleUser, Plugin.Instance!.Configuration.ApplyParentalControls ? filter.ParentalDataFor : null), null);

/// <summary>
/// Artwork on Version Change: the version ("N") whose picture of <paramref name="type"/> ("Backdrop", "Logo", "Clearart" or
/// "Characterart") the detail page shows for the picked version <paramref name="pickedId"/> when the page was opened with
/// <paramref name="firstId"/> (history.state.pbFirst). Null = no picture. Same rule as the web client: with Follows Version off the
/// first version's picture is shown; with Follows Version on, Keep When Version Has None on and no picture on the picked version, also the
/// first version's; otherwise the picked one's. A first id that is the picked one, or no version of its game, counts as the picked one.
/// The result is then made canonical: the first version (the first id's, then the game's version order) whose picture has the same
/// content, so identical copies in two game folders answer with one id. Only applies to a game with more than one version.
/// See <see cref="ArtworkPlusLinkCore.VersionImageId"/>.
/// </summary>
    public static string? VersionImageId(Guid pickedId, Guid firstId, string type) =>
        Answer(null, (filter, data) => ArtworkPlusLinkCore.VersionImageId(data, Plugin.Instance!.Configuration, pickedId, firstId, type,
            (group, game, kind) => ArtworkPlusLinkCore.HasPicture(filter.Images(), Plugin.Instance!.Configuration, group, game, kind),
            (group, game, kind) => ArtworkPlusLinkCore.ContentKey(filter.Images(), Plugin.Instance!.Configuration, group, game, kind),
            (group, game, kind) => ArtworkPlusLinkCore.ContentLength(filter.Images(), Plugin.Instance!.Configuration, group, game, kind)), pickedId);

/// <summary>
/// True while ArtworkPlus is responsible for the artwork of games, i.e. Settings From (Artwork tab, Display) is ArtworkPlus.
/// False means Playnite Bridge draws it and ArtworkPlus should answer "not responsible" for games. Also true when the plugin's
/// configuration is not available, so ArtworkPlus keeps drawing as before.
/// </summary>
    public static bool ArtworkPlusDrawsGames()
    {
        try
        {
            return Plugin.Instance?.Configuration is not Configuration.PluginConfiguration config || config.ArtworkSettingsFrom == ArtworkSettingsSource.ArtworkPlus;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Called by the filter's constructor.</summary>
    internal static void Register(VirtualItemsFilter filter) => Volatile.Write(ref _filter, filter);

// Returns safe unless the plugin, the filter and an accepted Playnite folder exist. With an id, the game data is the one the filter
// resolves that id in; without one, the current game data.
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
/// The answers of <see cref="ArtworkPlusLink"/> computed from given game data, image cache and user checks. Pure: it answers from the
/// data it is given and handles no requests. Logo, Art and backdrops come from the same methods as the game's own item.
/// </summary>
public static class ArtworkPlusLinkCore
{
    /// <summary>The game hit of <paramref name="id"/> in <paramref name="data"/>; null for Guid.Empty and any id that is no game.</summary>
    public static GameHit? Game(GameData data, Guid id) => id != Guid.Empty && data.Find(id) is { Kind: GameHitKind.Game } hit ? hit : null;

    public static bool IsGame(GameData data, Guid id) => Game(data, id) is not null;

    public static string? GameFolder(GameData data, ImageCache images, Guid id) => Game(data, id) is GameHit hit ? images.GameFolder(hit.Game!) : null;

    /// <summary>[path, tag] of the game's own Logo ("Logo") or Art ("Art", ordinal) file (VirtualItemsFilter.TaggedImage); null otherwise.</summary>
    public static string[]? Image(GameData data, ImageCache images, Guid id, string? type)
    {
        ImageType? imageType = type switch
        {
            "Logo" => ImageType.Logo,
            "Art" => ImageType.Art,
            _ => null
        };
        return imageType is not null && Game(data, id) is GameHit hit && VirtualItemsFilter.TaggedImage(images, hit.Group!, hit.Game!, imageType.Value) is var (path, tag)
            ? new[] { path, tag }
            : null;
    }

    /// <summary>The backdrops of the hit's version (a twin's own for Playnite's background) as [path, tag] (VirtualItemsFilter.TaggedBackdrops); empty otherwise.</summary>
    public static string[][] Backdrops(GameData data, ImageCache images, Guid id) => Game(data, id) is GameHit hit
        ? VirtualItemsFilter.TaggedBackdrops(images, hit.Group!, hit.Game!).Select(b => new[] { b.Path, b.Tag }).ToArray()
        : Array.Empty<string[]>();

    /// <summary>The collection hit of <paramref name="id"/> in <paramref name="data"/>; null for Guid.Empty and any id that is no collection.</summary>
    public static GameCollection? Collection(GameData data, Guid id) => id != Guid.Empty && data.Find(id) is { Kind: GameHitKind.Collection } hit ? hit.Collection : null;

    public static bool IsCollection(GameData data, Guid id) => Collection(data, id) is not null;

    /// <summary>
    /// [id ("N"), release date "yyyy-MM-dd" or "", sort name] of each game of the collection the user may see: the main version of each group once, by sort name. The
    /// user must exist and may see the games library (<paramref name="visibleUser"/>); with <paramref name="parental"/> (ApplyParentalControls) only the games that user's
    /// parental controls leave count, and a collection that has none left answers empty. Empty for Guid.Empty as user or a non-collection.
    /// </summary>
    public static string[][] CollectionMembers(GameData data, Guid id, Guid userId, Func<Guid, User?> visibleUser, Func<User, GameData, GameData>? parental)
    {
        if (userId == Guid.Empty || Collection(data, id) is null || visibleUser(userId) is not User user)
        {
            return Array.Empty<string[]>();
        }

        var seen = parental is null ? data : parental(user, data);
        return Collection(seen, id) is GameCollection collection
            ? VirtualItemsFilter.CollectionMains(seen, collection)
                .Select(g => new[] { Guid.Parse(g.Id).ToString("N"), g.ReleaseDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, GameQuery.SortName(g) })
                .ToArray()
            : Array.Empty<string[]>();
    }

    /// <summary>[path, tag] of the own backdrops ("Backdrop", in order; a file gone since the listing keeps its place with an empty tag) or the own poster ("Poster") of a collection; empty otherwise.</summary>
    public static string[][] CollectionImages(GameData data, Guid id, string? type, Func<GameCollection, CollectionPicture> own)
    {
        if (Collection(data, id) is not GameCollection collection || type is not ("Backdrop" or "Poster"))
        {
            return Array.Empty<string[]>();
        }

        var pictures = own(collection);
        return type == "Poster"
            ? pictures.Poster is string poster && CollectionPictures.Tag(poster) is string tag ? new[] { new[] { poster, tag } } : Array.Empty<string[]>()
            : pictures.Backdrops.Select(b => new[] { b, CollectionPictures.Tag(b) ?? string.Empty }).ToArray();
    }

    /// <summary>
    /// The version whose picture of <paramref name="type"/> is shown for <paramref name="pickedId"/> opened with <paramref name="firstId"/>
    /// (<see cref="ArtworkPlusLink.VersionImageId"/>), as "N"; null for no picture, an unknown type or a picked id that is no game.
    /// <paramref name="has"/>: whether a version (with its library group) has a picture of the type.
    /// </summary>
    public static string? VersionImageId(GameData data, PluginConfiguration config, Guid pickedId, Guid firstId, string? type, Func<GameGroup, GameEntry, string, bool> has,
        Func<GameGroup, GameEntry, string, string?>? contentKey = null, Func<GameGroup, GameEntry, string, string?>? contentLength = null)
    {
        if (type is not ("Backdrop" or "Logo" or "Clearart" or "Characterart") || Game(data, pickedId) is not GameHit picked)
        {
            return null;
        }

        var versions = data.DetailGroupOf(picked.Game!.Id) ?? picked.Group!;
        var first = firstId != pickedId && Game(data, firstId) is GameHit hit
            && (versions.Versions.Any(v => v.Id == hit.Game!.Id) || (versions.Twins?.ContainsKey(hit.Game!.Id) ?? false)) ? hit : null;
        var follow = (bool)typeof(PluginConfiguration).GetProperty("VersionFollow" + type)!.GetValue(config)!;
        var keep = (bool)typeof(PluginConfiguration).GetProperty("VersionKeep" + type)!.GetValue(config)!;
        var pickedHas = has(picked.Group!, picked.Game!, type);
        var shown = first is not null && UsesFirst(follow, keep, pickedHas) ? first : picked;
        var shownHas = ReferenceEquals(shown, picked) ? pickedHas : has(shown.Group!, shown.Game!, type);
        if (!shownHas)
        {
            return null;
        }

        var shownId = Guid.Parse(shown.Game!.Id).ToString("N");
        if (contentKey is null || versions.Versions.Count < 2)
        {
            return shownId;
        }

// Canonical id: the first candidate (the first id's version, then the game's versions in order) whose picture has the shown one's content.
// Candidates come from the same game data the image endpoint resolves ids in: all versions of the detail group, including ones the
// asking user may not see. Files of different size are never equal, so only candidates with the shown picture's size are hashed.
// If the shown picture cannot be hashed its own id is kept; a candidate that cannot be hashed is skipped.
        var candidates = new List<GameHit>();
        if (first is not null)
        {
            candidates.Add(first);
        }

        candidates.AddRange(versions.Versions.Select(v => Game(data, Guid.Parse(v.Id))).OfType<GameHit>());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? shownLength = null;
        var shownLengthKnown = false;
        string? shownKey = null;
        var shownKeyKnown = false;
        foreach (var candidate in candidates)
        {
            if (!seen.Add(candidate.Game!.Id))
            {
                continue;
            }

            if (candidate.Game.Id == shown.Game.Id)
            {
                break;
            }

            if (!has(candidate.Group!, candidate.Game, type))
            {
                continue;
            }

            if (contentLength is not null)
            {
                if (!shownLengthKnown)
                {
                    shownLength = contentLength(shown.Group!, shown.Game!, type);
                    shownLengthKnown = true;
                }

                if (shownLength is not null && contentLength(candidate.Group!, candidate.Game, type) != shownLength)
                {
                    continue;
                }
            }

            if (!shownKeyKnown)
            {
                shownKey = contentKey(shown.Group!, shown.Game!, type);
                shownKeyKnown = true;
            }

            if (shownKey is null)
            {
                return shownId;
            }

            if (contentKey(candidate.Group!, candidate.Game, type) == shownKey)
            {
                return Guid.Parse(candidate.Game.Id).ToString("N");
            }
        }

        return shownId;
    }

    /// <summary>
    /// The content key of a version's picture of <paramref name="type"/> (as <see cref="HasPicture"/> sees it): the <see cref="ImageCache.ContentKey"/>s of
    /// its files in order joined by "|" (one file for Logo and Clearart, all backdrops for Backdrop, all character art files for Characterart);
    /// null without a picture or when any file cannot be hashed.
    /// </summary>
    public static string? ContentKey(ImageCache images, PluginConfiguration config, GameGroup group, GameEntry game, string type)
    {
        var files = PictureFiles(images, config, group, game, type);
        if (files.Count == 0)
        {
            return null;
        }

        var keys = files.Select(ImageCache.ContentKey).ToList();
        return keys.Any(k => k is null) ? null : string.Join("|", keys);
    }

    /// <summary>
    /// The sizes in bytes of the files <see cref="ContentKey"/> hashes, in order joined by "|" (nothing is read): equal content has equal sizes, so a
    /// different value means a different picture. Null without a picture or when any file is missing.
    /// </summary>
    public static string? ContentLength(ImageCache images, PluginConfiguration config, GameGroup group, GameEntry game, string type)
    {
        var files = PictureFiles(images, config, group, game, type);
        if (files.Count == 0)
        {
            return null;
        }

        try
        {
            var lengths = files.Select(f => new FileInfo(f) is { Exists: true } info ? info.Length : -1L).ToList();
            return lengths.Any(l => l < 0) ? null : string.Join("|", lengths);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> PictureFiles(ImageCache images, PluginConfiguration config, GameGroup group, GameEntry game, string type) => type switch
    {
        "Backdrop" => VirtualItemsFilter.TaggedBackdrops(images, group, game).Select(b => b.Path).ToList(),
        "Logo" => VirtualItemsFilter.TaggedImage(images, group, game, ImageType.Logo) is var (logo, _) ? new[] { logo } : Array.Empty<string>(),
        "Clearart" => VirtualItemsFilter.TaggedImage(images, group, game, ImageType.Art) is var (art, _) ? new[] { art } : Array.Empty<string>(),
        "Characterart" => CharacterArtFiles.List(images.GameFolder(game), CharacterArtFiles.Name(config)),
        _ => Array.Empty<string>()
    };

    /// <summary>True when the first version's picture is shown: Follows Version is off, or Keep When Version Has None is on and the picked version has none.</summary>
    public static bool UsesFirst(bool follow, bool keep, bool pickedHas) => !follow || (keep && !pickedHas);

    /// <summary>
    /// Whether a version has a picture of a type as its item and the characterart list show it: its backdrops, Logo, Art (Clearart), and for
    /// Characterart the files of its own game folder.
    /// </summary>
    public static bool HasPicture(ImageCache images, PluginConfiguration config, GameGroup group, GameEntry game, string type) => type switch
    {
        "Backdrop" => VirtualItemsFilter.TaggedBackdrops(images, group, game).Count > 0,
        "Logo" => VirtualItemsFilter.TaggedImage(images, group, game, ImageType.Logo) is not null,
        "Clearart" => VirtualItemsFilter.TaggedImage(images, group, game, ImageType.Art) is not null,
        "Characterart" => CharacterArtFiles.List(images.GameFolder(game), CharacterArtFiles.Name(config)).Count > 0,
        _ => false
    };

    /// <summary>
    /// True when <paramref name="id"/> is a game of <paramref name="data"/>, <paramref name="visibleUser"/> gives the user of
    /// <paramref name="userId"/> (it exists and may see the games library), and, while <paramref name="parental"/> is given (ApplyParentalControls),
    /// the id is a game of the data that user's parental controls leave; false for Guid.Empty as user.
    /// </summary>
    public static bool IsVisibleTo(GameData data, Guid id, Guid userId, Func<Guid, User?> visibleUser, Func<User, GameData, GameData>? parental) =>
        userId != Guid.Empty && IsGame(data, id) && visibleUser(userId) is User user && (parental is null || IsGame(parental(user, data), id));

    /// <summary>
    /// For each group of the game data the user may see (as <see cref="IsVisibleTo"/>), in order, the main version's first backdrop
    /// (VirtualItemsFilter.TaggedBackdrops) as [id ("N"), path, tag, favourite]; groups without a backdrop are left out; empty unless
    /// <paramref name="libraryId"/> is <paramref name="gamesLibrary"/>, and for Guid.Empty as library or user.
    /// </summary>
    public static string[][] LibraryBackdrops(GameData data, ImageCache images, Guid libraryId, Guid? gamesLibrary, Guid userId, Func<Guid, User?> visibleUser,
        Func<User, GameData, GameData>? parental)
    {
        if (libraryId == Guid.Empty || libraryId != gamesLibrary || userId == Guid.Empty || visibleUser(userId) is not User user)
        {
            return Array.Empty<string[]>();
        }

        return (parental is null ? data : parental(user, data)).Groups
            .SelectMany(group => VirtualItemsFilter.TaggedBackdrops(images, group, group.Main).Take(1)
                .Select(b => new[] { Guid.Parse(group.Main.Id).ToString("N"), b.Path, b.Tag, group.Main.Favorite ? "true" : "false" }))
            .ToArray();
    }
}
