using System.Buffers.Binary;

namespace Modules.Ocr.Infrastructure.Pdf;

/// <summary>
/// Reads the JPEG headers a PDF needs. The compressed data is embedded as it is (DCTDecode), so
/// pages keep the original quality and nothing is decoded on this server.
/// </summary>
internal static class JpegReader
{
    private const byte Marker = 0xFF;
    private const int MaxOrientation = 8;

    public static PdfImage? Read(ReadOnlyMemory<byte> jpeg)
    {
        var data = jpeg.Span;
        if (data.Length < 4 || data[0] != Marker || data[1] != 0xD8)
            return null;

        double? dpi = null;
        var orientation = 1;
        var adobe = false;
        var position = 2;
        while (position + 2 <= data.Length)
        {
            if (data[position] != Marker)
                return null;

            var marker = data[position + 1];
            if (marker == Marker)
            {
                position++; // fill byte
                continue;
            }

            position += 2;
            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
                continue; // markers without a length
            if (marker is 0xD9 or 0xDA)
                return null; // end of image, or scan data before any frame header
            if (position + 2 > data.Length)
                return null;

            int length = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            if (length < 2 || position + length > data.Length)
                return null;

            var segment = data.Slice(position + 2, length - 2);
            position += length;
            switch (marker)
            {
                case 0xE0:
                    dpi ??= JfifDpi(segment);
                    break;
                case 0xE1:
                    orientation = ExifOrientation(segment) ?? orientation;
                    break;
                case 0xEE:
                    adobe |= segment.StartsWith("Adobe"u8);
                    break;
                // Start of frame; C4 (Huffman tables), C8 (reserved) and CC (arithmetic conditioning) are not frames.
                case >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC:
                    return Frame(jpeg, segment, dpi, orientation, adobe);
            }
        }

        return null;
    }

    private static PdfImage? Frame(ReadOnlyMemory<byte> jpeg, ReadOnlySpan<byte> frame, double? dpi, int orientation, bool adobe)
    {
        if (frame.Length < 6 || frame[0] != 8)
            return null; // PDF readers only handle 8-bit samples

        int height = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);
        if (width == 0 || height == 0)
            return null; // height given later in a DNL segment: too rare to support

        var colorSpace = frame[5] switch
        {
            1 => "/DeviceGray",
            3 => "/DeviceRGB",
            4 => "/DeviceCMYK",
            _ => null,
        };
        if (colorSpace is null)
            return null;

        // Adobe applications write CMYK JPEGs with inverted values.
        var decode = frame[5] == 4 && adobe ? " /Decode [1 0 1 0 1 0 1 0]" : string.Empty;
        return new PdfImage(width, height, $"/ColorSpace {colorSpace} /BitsPerComponent 8 /Filter /DCTDecode{decode}", jpeg, dpi, orientation);
    }

    private static double? JfifDpi(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 12 || !segment.StartsWith("JFIF\0"u8))
            return null;

        int density = BinaryPrimitives.ReadUInt16BigEndian(segment[8..]);
        return segment[7] switch
        {
            1 => density,
            2 => density * 2.54,
            _ => null,
        };
    }

    /// <summary>Phone cameras store pictures sideways and record how to turn them in the EXIF orientation tag.</summary>
    private static int? ExifOrientation(ReadOnlySpan<byte> segment)
    {
        if (!segment.StartsWith("Exif\0\0"u8))
            return null;

        var tiff = segment[6..];
        if (tiff.Length < 8)
            return null;

        bool little;
        if (tiff.StartsWith("II"u8))
            little = true;
        else if (tiff.StartsWith("MM"u8))
            little = false;
        else
            return null;

        var ifd = ReadUInt32(tiff[4..], little);
        if (ifd > (uint)(tiff.Length - 2))
            return null;

        var entries = tiff[(int)ifd..];
        int count = ReadUInt16(entries, little);
        for (var i = 0; i < count; i++)
        {
            var offset = 2 + (i * 12);
            if (offset + 12 > entries.Length)
                return null;

            var entry = entries.Slice(offset, 12);
            const ushort OrientationTag = 0x0112;
            const ushort ShortType = 3;
            if (ReadUInt16(entry, little) == OrientationTag && ReadUInt16(entry[2..], little) == ShortType)
            {
                int value = ReadUInt16(entry[8..], little);
                return value is >= 1 and <= MaxOrientation ? value : null;
            }
        }

        return null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);
}
