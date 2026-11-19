using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Domain.UnitTests;

public sealed class OcrFileFormatsTests
{
    public static TheoryData<byte[], OcrFileFormat> Signatures => new()
    {
        { [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0], OcrFileFormat.Jpeg },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0], OcrFileFormat.Png },
        { "II*\0\0\0\0\0\0\0\0\0"u8.ToArray(), OcrFileFormat.Tiff },
        { "MM\0*\0\0\0\0\0\0\0\0"u8.ToArray(), OcrFileFormat.Tiff },
        { "RIFF\0\0\0\0WEBP"u8.ToArray(), OcrFileFormat.Webp },
        { "BM\0\0\0\0\0\0\0\0\0\0"u8.ToArray(), OcrFileFormat.Bmp },
        { "%PDF-1.7\n\0\0\0"u8.ToArray(), OcrFileFormat.Pdf },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Detect_recognises_every_format_the_service_accepts(byte[] header, OcrFileFormat expected) =>
        Assert.Equal(expected, OcrFileFormats.Detect(header));

    [Fact]
    public void Gif_is_not_accepted_by_the_service()
    {
        Assert.Null(OcrFileFormats.Detect("GIF89a\0\0\0\0\0\0"u8));
        Assert.Null(OcrFileFormats.FromFileName("a.gif"));
    }

    [Theory]
    [InlineData("scan.TIF", OcrFileFormat.Tiff)]
    [InlineData("scan.tiff", OcrFileFormat.Tiff)]
    [InlineData("doc.PDF", OcrFileFormat.Pdf)]
    [InlineData("photo.jpeg", OcrFileFormat.Jpeg)]
    public void FromFileName_maps_extensions_case_insensitively(string name, OcrFileFormat expected) =>
        Assert.Equal(expected, OcrFileFormats.FromFileName(name));

    [Fact]
    public void Supported_extensions_match_the_api_document() =>
        Assert.Equal(
            [".bmp", ".jpeg", ".jpg", ".pdf", ".png", ".tif", ".tiff", ".webp"],
            OcrFileFormats.SupportedExtensions.Order(StringComparer.Ordinal));
}
