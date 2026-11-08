using System;
using System.IO;
using System.IO.Compression;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// Turns a Windows icon (.ico) into a PNG without a graphics library. The largest frame (by size, then bit depth) is used:
/// a PNG frame is passed on unchanged, an uncompressed BMP frame of 1, 4, 8, 24 or 32 bits per pixel is decoded
/// (palette, alpha of a 32 bit frame, else the AND mask) and written as an 8 bit RGBA PNG. Anything else, and damaged icons, give null.
/// </summary>
public static class IcoConverter
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly record struct Frame(int Width, int Height, int Bits, long Size, long Offset);

    /// <summary>
    /// True when the file is an icon: its first four bytes are the icon directory header (00 00 01 00), or it has the extension .ico and
    /// shows no other image format (so a PNG named .ico is no icon here).
    /// </summary>
    public static bool IsIco(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = new byte[4];
            if (stream.ReadAtLeast(head, head.Length, false) == head.Length && head[0] == 0 && head[1] == 0 && head[2] == 1 && head[3] == 0)
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) && ImageCache.ContentExtension(path) is null;
    }

    /// <summary>The size of the largest frame as the icon directory states it; null when the file cannot be read or has no valid directory.</summary>
    public static (int Width, int Height)? LargestSize(string path)
    {
        try
        {
            return LargestSize(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The size of the largest frame as the icon directory states it; null without a valid directory.</summary>
    public static (int Width, int Height)? LargestSize(byte[] data) => Largest(data) is { } frame ? (frame.Width, frame.Height) : null;

    /// <summary>The PNG of the largest frame of an icon; null when the data are no icon, damaged or of a kind that is not decoded.</summary>
    public static byte[]? ToPng(byte[] data)
    {
        if (Largest(data) is not { } frame || frame.Size <= 0 || frame.Offset + frame.Size > data.Length)
        {
            return null;
        }

        var bytes = data.AsSpan((int)frame.Offset, (int)frame.Size);
        return bytes.Length >= 33 && bytes.StartsWith(PngSignature) ? bytes.ToArray() : FromDib(bytes);
    }

    private static Frame? Largest(byte[] data)
    {
        if (data.Length < 6 || data[0] != 0 || data[1] != 0 || data[2] != 1 || data[3] != 0)
        {
            return null;
        }

        int count = data[4] | (data[5] << 8);
        if (count == 0 || data.Length < 6 + (16 * count))
        {
            return null;
        }

        Frame? best = null;
        for (var i = 0; i < count; i++)
        {
            var p = 6 + (16 * i);
            var frame = new Frame(data[p] == 0 ? 256 : data[p], data[p + 1] == 0 ? 256 : data[p + 1], data[p + 6] | (data[p + 7] << 8),
                BitConverter.ToUInt32(data, p + 8), BitConverter.ToUInt32(data, p + 12));
            if (best is not { } known || frame.Width * frame.Height > known.Width * known.Height
                || (frame.Width * frame.Height == known.Width * known.Height && frame.Bits > known.Bits))
            {
                best = frame;
            }
        }

        return best;
    }

    private static byte[]? FromDib(ReadOnlySpan<byte> f)
    {
        if (f.Length < 40)
        {
            return null;
        }

        var header = (long)BitConverter.ToUInt32(f.Slice(0, 4));
        var width = BitConverter.ToInt32(f.Slice(4, 4));
        var rawHeight = BitConverter.ToInt32(f.Slice(8, 4));
        int bits = BitConverter.ToUInt16(f.Slice(14, 2));
        var compression = BitConverter.ToUInt32(f.Slice(16, 4));
        var used = BitConverter.ToUInt32(f.Slice(32, 4));
        var absHeight = Math.Abs((long)rawHeight);
        if (header < 40 || header > f.Length || compression != 0 || bits is not (1 or 4 or 8 or 24 or 32) || width < 1 || width > 4096 || absHeight < 1 || absHeight > 8192)
        {
            return null;
        }

        // An icon's DIB holds the colour bitmap and the 1 bit AND mask, so its height is doubled.
        var doubled = absHeight != width;
        var height = (int)(doubled ? absHeight / 2 : absHeight);
        if (height < 1)
        {
            return null;
        }

        var colors = bits <= 8 ? (used != 0 ? (int)Math.Min(used, 1u << bits) : 1 << bits) : 0;
        var pixels = header + (4L * colors);
        var stride = (((long)width * bits) + 31) / 32 * 4;
        var maskStart = pixels + (stride * height);
        if (maskStart > f.Length)
        {
            return null;
        }

        var maskStride = (((long)width) + 31) / 32 * 4;
        var hasMask = doubled && maskStart + (maskStride * height) <= f.Length;
        var topDown = rawHeight < 0;
        var rgba = new byte[width * height * 4];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var row = topDown ? y : height - 1 - y;
            var line = pixels + (row * stride);
            var maskLine = maskStart + (row * maskStride);
            for (var x = 0; x < width; x++)
            {
                byte b, g, r, a = 255;
                if (bits == 32)
                {
                    var p = (int)(line + (4L * x));
                    (b, g, r, a) = (f[p], f[p + 1], f[p + 2], f[p + 3]);
                    anyAlpha |= a != 0;
                }
                else if (bits == 24)
                {
                    var p = (int)(line + (3L * x));
                    (b, g, r) = (f[p], f[p + 1], f[p + 2]);
                }
                else
                {
                    var bitPos = x * bits;
                    var index = (f[(int)(line + (bitPos / 8))] >> (8 - bits - (bitPos % 8))) & ((1 << bits) - 1);
                    if (index < colors)
                    {
                        var p = (int)(header + (4L * index));
                        (b, g, r) = (f[p], f[p + 1], f[p + 2]);
                    }
                    else
                    {
                        (b, g, r) = ((byte)0, (byte)0, (byte)0);
                    }
                }

                if (bits != 32 && hasMask && ((f[(int)(maskLine + (x / 8))] >> (7 - (x % 8))) & 1) == 1)
                {
                    a = 0;
                }

                var o = ((y * width) + x) * 4;
                (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (r, g, b, a);
            }
        }

        if (bits == 32 && !anyAlpha)
        {
            // A 32 bit frame without any alpha (older icons): opaque, except where the AND mask says transparent.
            for (var y = 0; y < height; y++)
            {
                var maskLine = maskStart + ((topDown ? y : height - 1 - y) * maskStride);
                for (var x = 0; x < width; x++)
                {
                    var masked = hasMask && ((f[(int)(maskLine + (x / 8))] >> (7 - (x % 8))) & 1) == 1;
                    rgba[((y * width) + x) * 4 + 3] = masked ? (byte)0 : (byte)255;
                }
            }
        }

        return EncodePng(rgba, width, height);
    }

    /// <summary>An 8 bit RGBA PNG (filter 0 on every row) of <paramref name="rgba"/>.</summary>
    private static byte[] EncodePng(byte[] rgba, int width, int height)
    {
        using var png = new MemoryStream();
        png.Write(PngSignature);
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        (ihdr[8], ihdr[9]) = (8, 6);
        WriteChunk(png, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba, y * width * 4, width * 4);
            }
        }

        WriteChunk(png, "IDAT", raw.ToArray());
        WriteChunk(png, "IEND", Array.Empty<byte>());
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type, 0, 4, typed, 0);
        data.CopyTo(typed, 4);
        var number = new byte[4];
        WriteBigEndian(number, 0, (uint)data.Length);
        stream.Write(number);
        stream.Write(typed);
        var crc = uint.MaxValue;
        foreach (var b in typed)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        WriteBigEndian(number, 0, crc ^ uint.MaxValue);
        stream.Write(number);
    }

    private static void WriteBigEndian(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
