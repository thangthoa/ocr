using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;

namespace Modules.Ocr.Infrastructure.Pdf;

/// <summary>
/// Turns a PNG into a PDF image. PDF's Flate filter with the PNG predictor reads PNG pixel data
/// directly, so gray, RGB and palette images are embedded without decoding. PDF has no alpha
/// channel in the image itself: images with alpha are decoded row by row and flattened onto white.
/// Interlaced PNGs are not supported.
/// </summary>
internal static class PngReader
{
    /// <summary>Bounds the work of flattening an image with alpha (a small file can hold a huge image).</summary>
    public const long MaxFlattenedPixels = 80_000_000;

    /// <summary>JPEG's own limit; keeps the row buffers small.</summary>
    public const int MaxFlattenedWidth = 65_535;

    private const int MaxPaletteEntries = 256;
    private const byte Opaque = 255;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static PdfImage? Read(ReadOnlyMemory<byte> png, CancellationToken cancellationToken)
    {
        var data = png.Span;
        if (!data.StartsWith(Signature))
            return null;

        Header? header = null;
        ReadOnlySpan<byte> palette = default;
        double? dpi = null;
        using var compressed = new MemoryStream();
        var position = Signature.Length;
        while (position + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            if (length > (uint)(data.Length - position - 12))
                return null;

            var type = data.Slice(position + 4, 4);
            var chunk = data.Slice(position + 8, (int)length);
            position += 12 + (int)length;
            if (type.SequenceEqual("IHDR"u8))
                header = Header.Read(chunk);
            else if (type.SequenceEqual("PLTE"u8))
                palette = chunk;
            else if (type.SequenceEqual("IDAT"u8))
                compressed.Write(chunk);
            else if (type.SequenceEqual("pHYs"u8))
                dpi = Dpi(chunk);
            else if (type.SequenceEqual("IEND"u8))
                break;
        }

        if (header is not { } h || compressed.Length == 0 || h.Interlaced)
            return null;

        return h.ColorType switch
        {
            Header.Gray => Predicted(h, "/DeviceGray", colors: 1, compressed.ToArray(), dpi),
            Header.Rgb => Predicted(h, "/DeviceRGB", colors: 3, compressed.ToArray(), dpi),
            Header.Palette => IndexedColorSpace(palette) is { } indexed ? Predicted(h, indexed, colors: 1, compressed.ToArray(), dpi) : null,
            Header.GrayAlpha or Header.RgbAlpha => Flatten(h, compressed, dpi, cancellationToken),
            _ => null,
        };
    }

    private static PdfImage Predicted(Header header, string colorSpace, int colors, byte[] data, double? dpi)
    {
        var entries = string.Create(CultureInfo.InvariantCulture,
            $"/ColorSpace {colorSpace} /BitsPerComponent {header.BitDepth} /Filter /FlateDecode " +
            $"/DecodeParms << /Predictor 15 /Colors {colors} /BitsPerComponent {header.BitDepth} /Columns {header.Width} >>");
        return new PdfImage(header.Width, header.Height, entries, data, dpi);
    }

    private static string? IndexedColorSpace(ReadOnlySpan<byte> palette)
    {
        var entries = palette.Length / 3;
        if (entries is 0 or > MaxPaletteEntries || palette.Length % 3 != 0)
            return null;

        return string.Create(CultureInfo.InvariantCulture, $"[/Indexed /DeviceRGB {entries - 1} <{Convert.ToHexString(palette)}>]");
    }

    /// <summary>Decodes rows one at a time, blends each pixel onto white and compresses the result again.</summary>
    private static PdfImage? Flatten(Header header, MemoryStream compressed, double? dpi, CancellationToken cancellationToken)
    {
        if (header.Width > MaxFlattenedWidth || (long)header.Width * header.Height > MaxFlattenedPixels)
            return null;

        var colors = header.ColorType == Header.RgbAlpha ? 3 : 1;
        var sampleBytes = header.BitDepth / 8;
        var pixelBytes = (colors + 1) * sampleBytes;
        var row = new byte[header.Width * pixelBytes];
        var previous = new byte[row.Length];
        var output = new byte[header.Width * colors];

        compressed.Position = 0;
        using var flattened = new MemoryStream();
        using (var inflate = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true))
        using (var deflate = new ZLibStream(flattened, CompressionLevel.Optimal, leaveOpen: true))
        {
            Span<byte> filter = stackalloc byte[1];
            for (var y = 0; y < header.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    inflate.ReadExactly(filter);
                    inflate.ReadExactly(row);
                }
                catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException)
                {
                    return null; // truncated or corrupt image data
                }

                if (!Unfilter(filter[0], row, previous, pixelBytes))
                    return null;

                for (var x = 0; x < header.Width; x++)
                {
                    var pixel = row.AsSpan(x * pixelBytes, pixelBytes);
                    // For 16-bit samples the high byte is enough for a scan going to OCR.
                    var alpha = pixel[colors * sampleBytes];
                    for (var c = 0; c < colors; c++)
                        output[(x * colors) + c] = OnWhite(pixel[c * sampleBytes], alpha);
                }

                deflate.Write(output);
                (row, previous) = (previous, row);
            }
        }

        var colorSpace = colors == 3 ? "/DeviceRGB" : "/DeviceGray";
        return new PdfImage(header.Width, header.Height, $"/ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /FlateDecode", flattened.ToArray(), dpi);
    }

    private static byte OnWhite(byte value, byte alpha) =>
        (byte)(((value * alpha) + (Opaque * (Opaque - alpha)) + (Opaque / 2)) / Opaque);

    /// <summary>Reverses the PNG row filter in place (PNG spec §9).</summary>
    private static bool Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int pixelBytes)
    {
        switch (filter)
        {
            case 0:
                return true;
            case 1:
                for (var i = pixelBytes; i < row.Length; i++)
                    row[i] += row[i - pixelBytes];
                return true;
            case 2:
                for (var i = 0; i < row.Length; i++)
                    row[i] += previous[i];
                return true;
            case 3:
                for (var i = 0; i < row.Length; i++)
                    row[i] += (byte)(((i >= pixelBytes ? row[i - pixelBytes] : 0) + previous[i]) / 2);
                return true;
            case 4:
                for (var i = 0; i < row.Length; i++)
                {
                    var left = i >= pixelBytes ? row[i - pixelBytes] : (byte)0;
                    var upperLeft = i >= pixelBytes ? previous[i - pixelBytes] : (byte)0;
                    row[i] += Paeth(left, previous[i], upperLeft);
                }

                return true;
            default:
                return false;
        }
    }

    private static byte Paeth(byte left, byte up, byte upperLeft)
    {
        var estimate = left + up - upperLeft;
        var toLeft = Math.Abs(estimate - left);
        var toUp = Math.Abs(estimate - up);
        var toUpperLeft = Math.Abs(estimate - upperLeft);
        if (toLeft <= toUp && toLeft <= toUpperLeft)
            return left;
        return toUp <= toUpperLeft ? up : upperLeft;
    }

    private static double? Dpi(ReadOnlySpan<byte> chunk)
    {
        const byte PerMetre = 1;
        const double MetresPerInch = 0.0254;
        if (chunk.Length < 9 || chunk[8] != PerMetre)
            return null;

        return BinaryPrimitives.ReadUInt32BigEndian(chunk) * MetresPerInch;
    }

    private readonly record struct Header(int Width, int Height, int BitDepth, int ColorType, bool Interlaced)
    {
        public const int Gray = 0;
        public const int Rgb = 2;
        public const int Palette = 3;
        public const int GrayAlpha = 4;
        public const int RgbAlpha = 6;

        public static Header? Read(ReadOnlySpan<byte> chunk)
        {
            if (chunk.Length < 13)
                return null;

            var width = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            var height = BinaryPrimitives.ReadUInt32BigEndian(chunk[4..]);
            int bitDepth = chunk[8];
            int colorType = chunk[9];
            if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue || chunk[10] != 0 || chunk[11] != 0 || chunk[12] > 1)
                return null;

            var validDepth = colorType switch
            {
                Gray => bitDepth is 1 or 2 or 4 or 8 or 16,
                Palette => bitDepth is 1 or 2 or 4 or 8,
                Rgb or GrayAlpha or RgbAlpha => bitDepth is 8 or 16,
                _ => false,
            };
            return validDepth ? new Header((int)width, (int)height, bitDepth, colorType, chunk[12] == 1) : null;
        }
    }
}
