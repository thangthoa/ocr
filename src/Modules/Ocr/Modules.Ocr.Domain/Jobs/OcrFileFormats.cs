namespace Modules.Ocr.Domain.Jobs;

/// <summary>The file types the OCR service accepts (API doc §3.1). GIF and SVG are not accepted.</summary>
public static class OcrFileFormats
{
    public const int SignatureLength = 12;

    private static readonly Dictionary<string, OcrFileFormat> FormatsByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = OcrFileFormat.Jpeg,
        [".jpeg"] = OcrFileFormat.Jpeg,
        [".png"] = OcrFileFormat.Png,
        [".tif"] = OcrFileFormat.Tiff,
        [".tiff"] = OcrFileFormat.Tiff,
        [".webp"] = OcrFileFormat.Webp,
        [".bmp"] = OcrFileFormat.Bmp,
        [".pdf"] = OcrFileFormat.Pdf,
    };

    public static IReadOnlyCollection<string> SupportedExtensions => FormatsByExtension.Keys;

    public static OcrFileFormat? FromFileName(string fileName) =>
        FormatsByExtension.TryGetValue(Path.GetExtension(fileName), out var format) ? format : null;

    public static string Extension(this OcrFileFormat format) => format switch
    {
        OcrFileFormat.Jpeg => ".jpg",
        OcrFileFormat.Png => ".png",
        OcrFileFormat.Tiff => ".tiff",
        OcrFileFormat.Webp => ".webp",
        OcrFileFormat.Bmp => ".bmp",
        OcrFileFormat.Pdf => ".pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    public static string ContentType(this OcrFileFormat format) => format switch
    {
        OcrFileFormat.Jpeg => "image/jpeg",
        OcrFileFormat.Png => "image/png",
        OcrFileFormat.Tiff => "image/tiff",
        OcrFileFormat.Webp => "image/webp",
        OcrFileFormat.Bmp => "image/bmp",
        OcrFileFormat.Pdf => "application/pdf",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };

    /// <summary>Identifies the format from the leading bytes; extension and client content type are not trusted.</summary>
    public static OcrFileFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return OcrFileFormat.Jpeg;
        if (header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            return OcrFileFormat.Png;
        if (header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8))
            return OcrFileFormat.Tiff;
        if (header.Length >= SignatureLength && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return OcrFileFormat.Webp;
        if (header.StartsWith("BM"u8))
            return OcrFileFormat.Bmp;
        if (header.StartsWith("%PDF-"u8))
            return OcrFileFormat.Pdf;
        return null;
    }
}
