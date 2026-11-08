using System;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Ids of the virtual collections (Playnite series), genres, publishers, filter entries, local extras and Steam Special Features: name-based GUIDs (RFC 4122 version 5, SHA-1) from a fixed
/// namespace, so they never equal the Playnite GUID they come from and stay the same on every call.
/// </summary>
public static class VirtualIds
{
    public static readonly Guid Namespace = Guid.Parse("5c4623d8-8e56-4e52-8700-bd960a57830a");

    public static Guid Series(Guid playniteId) => Derive("series:" + playniteId.ToString("D"));

    public static Guid Genre(Guid playniteId) => Derive("genre:" + playniteId.ToString("D"));

    /// <summary>A company as publisher (studio id); as developer it keeps Playnite's GUID (person id).</summary>
    public static Guid Publisher(Guid companyId) => Derive("publisher:" + companyId.ToString("D"));

    /// <summary>A filter entry of Jellyfin's list.html filter menu, sent back as a genre id (GameQuery pseudo value).</summary>
    public static Guid Filter(string pseudoValue) => Derive("filter:" + pseudoValue);

    /// <summary>A local extra file: "&lt;game folder name&gt;/&lt;path in its game folder&gt;", lower case with "/" (GameExtras).</summary>
    public static Guid Extra(string key) => Derive("extra:" + key);

    /// <summary>A Steam Special Feature: "&lt;AppID&gt;/&lt;movie key&gt;" (SteamVideos.MovieKey).</summary>
    public static Guid SteamVideo(string appId, string movieKey) => Derive("steamvideo:" + appId + "/" + movieKey);

    /// <summary>
    /// True when <paramref name="id"/> has the version 5 / variant bits every <see cref="Derive"/> id carries. A negative filter only (about 63 of 64 foreign
    /// ids fail it, so they cost no index build and no disk); it proves nothing, ownership is the exact map lookup of <see cref="OwnIds"/>.
    /// </summary>
    public static bool IsDerived(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[6] & 0xF0) == 0x50 && (bytes[8] & 0xC0) == 0x80;
    }

    private static Guid Derive(string name)
    {
        var input = new byte[16 + Encoding.UTF8.GetByteCount(name)];
        Namespace.TryWriteBytes(input, bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, 0, name.Length, input, 16);
        var bytes = SHA1.HashData(input).AsSpan(0, 16).ToArray();
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }
}
