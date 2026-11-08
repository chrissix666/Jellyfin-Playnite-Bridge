using System;
using System.Collections.Generic;
using System.IO;

namespace Jellyfin.Plugin.PlayniteBridge.Helpers;

/// <summary>
/// The mean brightness of a baseline JPEG without a decoder library: only the DC coefficients of the luminance blocks are read
/// (the AC codes are skipped), so a 640 px picture takes a few milliseconds. Per 8 x 8 block mean = DC x quantisation / 8 + 128;
/// 0 = black, 255 = white. Used to tell a black still frame of a Steam video from a real one.
/// Progressive, arithmetic-coded, 12-bit and damaged files give null.
/// </summary>
public static class JpegLuma
{
    /// <summary>The mean brightness (0 to 255) of the JPEG at <paramref name="path"/>; null when it cannot be read this way.</summary>
    public static double? Mean(string path)
    {
        try
        {
            return Mean(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed class Table
    {
        public readonly int[] MinCode = new int[17];
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValPtr = new int[17];
        public byte[] Values = Array.Empty<byte>();
    }

    private sealed record Component(int Id, int H, int V, int QuantTable);

    /// <summary>The mean brightness of the JPEG in <paramref name="data"/>; null when it cannot be read this way.</summary>
    public static double? Mean(ReadOnlySpan<byte> data)
    {
        try
        {
            return Decode(data.ToArray());
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static double? Decode(byte[] d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        var quant = new Dictionary<int, int>();
        var dc = new Dictionary<int, Table>();
        var ac = new Dictionary<int, Table>();
        var components = new List<Component>();
        var restart = 0;
        var width = 0;
        var height = 0;
        var at = 2;
        while (at + 4 <= d.Length)
        {
            if (d[at] != 0xFF)
            {
                return null;
            }

            var marker = d[at + 1];
            if (marker == 0xFF)
            {
                at++;
                continue;
            }

            var length = (d[at + 2] << 8) | d[at + 3];
            var body = at + 4;
            var end = at + 2 + length;
            if (end > d.Length || length < 2)
            {
                return null;
            }

            switch (marker)
            {
                case 0xDB:
                    for (var p = body; p < end;)
                    {
                        var precision = d[p] >> 4;
                        var id = d[p] & 15;
                        quant[id] = precision == 0 ? d[p + 1] : (d[p + 1] << 8) | d[p + 2];
                        p += 1 + (precision == 0 ? 64 : 128);
                    }

                    break;
                case 0xC0:
                case 0xC1:
                    if (d[body] != 8)
                    {
                        return null;
                    }

                    height = (d[body + 1] << 8) | d[body + 2];
                    width = (d[body + 3] << 8) | d[body + 4];
                    for (var i = 0; i < d[body + 5]; i++)
                    {
                        var q = body + 6 + (3 * i);
                        components.Add(new Component(d[q], d[q + 1] >> 4, d[q + 1] & 15, d[q + 2]));
                    }

                    break;
                case 0xC2:
                case 0xC3:
                case 0xC5:
                case 0xC6:
                case 0xC7:
                case 0xC9:
                case 0xCA:
                case 0xCB:
                case 0xCD:
                case 0xCE:
                case 0xCF:
                    return null;
                case 0xC4:
                    for (var p = body; p < end;)
                    {
                        var cls = d[p] >> 4;
                        var id = d[p] & 15;
                        var counts = new int[17];
                        var total = 0;
                        for (var i = 1; i <= 16; i++)
                        {
                            counts[i] = d[p + i];
                            total += counts[i];
                        }

                        var table = new Table { Values = d[(p + 17)..(p + 17 + total)] };
                        var code = 0;
                        var k = 0;
                        for (var len = 1; len <= 16; len++)
                        {
                            table.ValPtr[len] = k;
                            table.MinCode[len] = code;
                            code += counts[len];
                            k += counts[len];
                            table.MaxCode[len] = counts[len] == 0 ? -1 : code - 1;
                            code <<= 1;
                        }

                        (cls == 0 ? dc : ac)[id] = table;
                        p += 17 + total;
                    }

                    break;
                case 0xDD:
                    restart = (d[body] << 8) | d[body + 1];
                    break;
                case 0xDA:
                    return Scan(d, end, body, width, height, components, quant, dc, ac, restart);
            }

            at = end;
        }

        return null;
    }

    private static double? Scan(byte[] d, int dataStart, int header, int width, int height, List<Component> components, Dictionary<int, int> quant,
        Dictionary<int, Table> dc, Dictionary<int, Table> ac, int restart)
    {
        var count = d[header];
        if (width <= 0 || height <= 0 || components.Count == 0 || count != components.Count)
        {
            return null;
        }

        var tables = new (Component Component, Table Dc, Table Ac)[count];
        for (var i = 0; i < count; i++)
        {
            var id = d[header + 1 + (2 * i)];
            var component = components.Find(c => c.Id == id);
            var selector = d[header + 2 + (2 * i)];
            if (component is null || !dc.TryGetValue(selector >> 4, out var dcTable) || !ac.TryGetValue(selector & 15, out var acTable))
            {
                return null;
            }

            tables[i] = (component, dcTable, acTable);
        }

        if (tables[0].Component.H < 1 || tables[0].Component.V < 1 || !quant.TryGetValue(tables[0].Component.QuantTable, out var q0))
        {
            return null;
        }

        var hMax = components.Max(c => c.H);
        var vMax = components.Max(c => c.V);
        var mcusX = (width + (8 * hMax) - 1) / (8 * hMax);
        var mcusY = (height + (8 * vMax) - 1) / (8 * vMax);
        var reader = new BitReader(d, dataStart);
        var predictor = new int[count];
        double sum = 0;
        long blocks = 0;
        var single = count == 1;
        for (var mcu = 0; mcu < mcusX * mcusY; mcu++)
        {
            if (restart > 0 && mcu > 0 && mcu % restart == 0)
            {
                reader.Restart();
                Array.Clear(predictor);
            }

            for (var c = 0; c < count; c++)
            {
                var (component, dcTable, acTable) = tables[c];
                var n = single ? 1 : component.H * component.V;
                for (var b = 0; b < n; b++)
                {
                    var s = reader.Decode(dcTable);
                    predictor[c] += s == 0 ? 0 : reader.Extend(reader.Receive(s), s);
                    if (c == 0)
                    {
                        sum += Math.Clamp((predictor[c] * q0 / 8.0) + 128, 0, 255);
                        blocks++;
                    }

                    for (var k = 1; k < 64;)
                    {
                        var rs = reader.Decode(acTable);
                        var size = rs & 15;
                        if (size == 0)
                        {
                            if ((rs >> 4) == 15)
                            {
                                k += 16;
                                continue;
                            }

                            break;
                        }

                        k += (rs >> 4) + 1;
                        reader.Receive(size);
                    }
                }
            }
        }

        return blocks == 0 ? null : sum / blocks;
    }

    private sealed class BitReader
    {
        private readonly byte[] _d;
        private int _at;
        private int _bits;
        private int _count;
        private bool _marker;

        public BitReader(byte[] data, int start)
        {
            _d = data;
            _at = start;
        }

        private int Bit()
        {
            if (_count == 0)
            {
                var value = 0;
                if (!_marker && _at < _d.Length)
                {
                    value = _d[_at++];
                    if (value == 0xFF)
                    {
                        var next = _at < _d.Length ? _d[_at] : 0;
                        if (next == 0)
                        {
                            _at++;
                        }
                        else
                        {
                            // a marker (restart or end): zeros until Restart
                            _marker = true;
                            _at--;
                            value = 0;
                        }
                    }
                }
                else if (_at >= _d.Length)
                {
                    throw new InvalidDataException("end of data");
                }

                _bits = value;
                _count = 8;
            }

            _count--;
            return (_bits >> _count) & 1;
        }

        public int Receive(int n)
        {
            var v = 0;
            for (var i = 0; i < n; i++)
            {
                v = (v << 1) | Bit();
            }

            return v;
        }

        public int Extend(int v, int n) => v < (1 << (n - 1)) ? v - (1 << n) + 1 : v;

        public int Decode(Table t)
        {
            var code = 0;
            for (var len = 1; len <= 16; len++)
            {
                code = (code << 1) | Bit();
                if (t.MaxCode[len] >= 0 && code <= t.MaxCode[len] && code >= t.MinCode[len])
                {
                    return t.Values[t.ValPtr[len] + code - t.MinCode[len]];
                }
            }

            throw new InvalidDataException("bad code");
        }

        public void Restart()
        {
            _count = 0;
            _marker = false;
            // the next bytes: FF Dn
            while (_at + 1 < _d.Length && !(_d[_at] == 0xFF && _d[_at + 1] >= 0xD0 && _d[_at + 1] <= 0xD7))
            {
                _at++;
            }

            _at += 2;
        }
    }
}
