using System.Buffers.Binary;
using System.IO.Compression;

namespace Modules.Ocr.Infrastructure.Tests;

public enum PngColor
{
    Gray = 0,
    Rgb = 2,
    Palette = 3,
    GrayAlpha = 4,
    RgbAlpha = 6,
}

/// <summary>Builds small JPEG headers and real PNG files, so the PDF composer is tested on the actual formats.</summary>
internal static class TestImages
{
    /// <summary>A JPEG with the given headers; the scan data is a stand-in (the composer never decodes it).</summary>
    public static byte[] Jpeg(int width, int height, int components = 3, int? dpi = null, int? orientation = null,
        bool littleEndianExif = true, bool adobe = false, int precision = 8)
    {
        using var output = new MemoryStream();
        output.Write([0xFF, 0xD8]);
        if (dpi is { } d)
            Segment(output, 0xE0, [.. "JFIF\0"u8, 1, 1, 1, (byte)(d >> 8), (byte)d, (byte)(d >> 8), (byte)d, 0, 0]);
        if (orientation is { } o)
            Segment(output, 0xE1, Exif(o, littleEndianExif));
        if (adobe)
            Segment(output, 0xEE, [.. "Adobe"u8, 0, 100, 0, 0, 0, 0, 2]);

        byte[] frame = [(byte)precision, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, (byte)components];
        Segment(output, 0xC0, [.. frame, .. Enumerable.Range(1, components).SelectMany(c => new byte[] { (byte)c, 0x11, 0 })]);
        output.Write([0xFF, 0xDA, 0, 2, 0x12, 0x34, 0xFF, 0xD9]);
        return output.ToArray();
    }

    /// <summary>
    /// A PNG whose pixel values follow a fixed pattern (see <see cref="Sample"/>). With
    /// <paramref name="varyFilters"/> every row filter type is used, so unfiltering is exercised.
    /// </summary>
    public static byte[] Png(int width, int height, PngColor color, int bitDepth, bool varyFilters = false, byte[]? palette = null,
        uint? pixelsPerMetre = null, bool interlaced = false, bool badFilter = false)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = (byte)bitDepth;
        header[9] = (byte)color;
        header[12] = interlaced ? (byte)1 : (byte)0;
        Chunk(output, "IHDR", header);
        if (palette is not null)
            Chunk(output, "PLTE", palette);
        if (pixelsPerMetre is { } ppm)
        {
            var phys = new byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys, ppm);
            BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4), ppm);
            phys[8] = 1;
            Chunk(output, "pHYs", phys);
        }

        var data = Filtered(width, height, color, bitDepth, varyFilters, badFilter);
        // Split the data over two IDAT chunks, as encoders do for large images.
        Chunk(output, "IDAT", data[..(data.Length / 2)]);
        Chunk(output, "IDAT", data[(data.Length / 2)..]);
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>The concatenated IDAT data of a PNG.</summary>
    public static byte[] PngImageData(byte[] png)
    {
        using var data = new MemoryStream();
        var position = 8;
        while (position < png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(position));
            if (png.AsSpan(position + 4, 4).SequenceEqual("IDAT"u8))
                data.Write(png, position + 8, length);
            position += 12 + length;
        }

        return data.ToArray();
    }

    /// <summary>The 8-bit gray or RGB pixels expected after blending the image onto white.</summary>
    public static byte[] Flattened(int width, int height, PngColor color, int bitDepth)
    {
        var colors = color == PngColor.RgbAlpha ? 3 : 1;
        var bytes = bitDepth / 8;
        var result = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            var row = RawRow(width, y, color, bitDepth);
            for (var x = 0; x < width; x++)
            {
                var pixel = row.AsSpan(x * (colors + 1) * bytes);
                int alpha = pixel[colors * bytes];
                for (var c = 0; c < colors; c++)
                    result.Add((byte)(((pixel[c * bytes] * alpha) + (255 * (255 - alpha)) + 127) / 255));
            }
        }

        return [.. result];
    }

    private static byte Sample(int x, int y, int channel) => (byte)((x * 37) + (y * 91) + (channel * 53) + 11);

    /// <summary>Unfiltered row bytes. For alpha images the alpha sample varies from transparent to opaque.</summary>
    private static byte[] RawRow(int width, int y, PngColor color, int bitDepth)
    {
        var channels = color switch
        {
            PngColor.Rgb => 3,
            PngColor.GrayAlpha => 2,
            PngColor.RgbAlpha => 4,
            _ => 1,
        };
        var row = new byte[((width * channels * bitDepth) + 7) / 8];
        if (bitDepth < 8)
        {
            for (var i = 0; i < row.Length; i++)
                row[i] = Sample(i, y, 0);
            return row;
        }

        var bytes = bitDepth / 8;
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < channels; c++)
            {
                var isAlpha = color is PngColor.GrayAlpha or PngColor.RgbAlpha && c == channels - 1;
                var value = isAlpha ? (byte)(((x + y) * 50) % 256) : Sample(x, y, c);
                for (var b = 0; b < bytes; b++)
                    row[(((x * channels) + c) * bytes) + b] = b == 0 ? value : (byte)(value ^ 0x5A);
            }
        }

        return row;
    }

    private static byte[] Filtered(int width, int height, PngColor color, int bitDepth, bool varyFilters, bool badFilter)
    {
        var channels = color switch
        {
            PngColor.Rgb => 3,
            PngColor.GrayAlpha => 2,
            PngColor.RgbAlpha => 4,
            _ => 1,
        };
        var pixelBytes = Math.Max(1, channels * bitDepth / 8);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var previous = new byte[RawRow(width, 0, color, bitDepth).Length];
            for (var y = 0; y < height; y++)
            {
                var raw = RawRow(width, y, color, bitDepth);
                var filter = badFilter ? (byte)9 : varyFilters ? (byte)(y % 5) : (byte)0;
                zlib.WriteByte(filter);
                zlib.Write(Filter(filter, raw, previous, pixelBytes));
                previous = raw;
            }
        }

        return compressed.ToArray();
    }

    private static byte[] Filter(byte filter, byte[] raw, byte[] previous, int pixelBytes)
    {
        var result = new byte[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            int left = i >= pixelBytes ? raw[i - pixelBytes] : 0;
            int up = previous[i];
            int upperLeft = i >= pixelBytes ? previous[i - pixelBytes] : 0;
            var predicted = filter switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upperLeft),
                _ => 0,
            };
            result[i] = (byte)(raw[i] - predicted);
        }

        return result;
    }

    private static int Paeth(int left, int up, int upperLeft)
    {
        var estimate = left + up - upperLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toUp && toLeft <= toUpperLeft)
            return left;
        return toUp <= toUpperLeft ? up : upperLeft;
    }

    private static byte[] Exif(int orientation, bool littleEndian)
    {
        // TIFF header, then IFD0 with a single entry: tag 0x0112 (Orientation), type SHORT, count 1.
        byte[] tiff = littleEndian
            ? [.. "II"u8, 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0]
            : [.. "MM"u8, 0, 42, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0, 0, 0, 0, 0];
        return [.. "Exif\0\0"u8, .. tiff];
    }

    private static void Segment(MemoryStream output, byte marker, byte[] data)
    {
        output.Write([0xFF, marker, (byte)((data.Length + 2) >> 8), (byte)(data.Length + 2)]);
        output.Write(data);
    }

    /// <summary>Writes a chunk; the CRC is left as zero because the composer does not check it.</summary>
    private static void Chunk(MemoryStream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        output.Write(System.Text.Encoding.ASCII.GetBytes(type));
        output.Write(data);
        output.Write([0, 0, 0, 0]);
    }
}
