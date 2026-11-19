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

    [Theory]
    [InlineData("invoice", OcrFileFormat.Pdf, "invoice.pdf")]
    [InlineData("invoice.PDF", OcrFileFormat.Pdf, "invoice.PDF")]
    [InlineData("scan.JPEG", OcrFileFormat.Jpeg, "scan.JPEG")]
    [InlineData("scan.png", OcrFileFormat.Jpeg, "scan.png.jpg")]
    [InlineData("v1.2", OcrFileFormat.Png, "v1.2.png")]
    public void WithExtension_adds_the_extension_only_when_missing(string name, OcrFileFormat format, string expected) =>
        Assert.Equal(expected, OcrFileFormats.WithExtension(name, format));

    [Fact]
    public void Only_jpeg_and_png_images_can_be_combined_into_pages() =>
        Assert.Equal([".jpeg", ".jpg", ".png"], OcrFileFormats.PageExtensions.Order(StringComparer.Ordinal));

    [Theory]
    [InlineData("Hợp đồng 2026", true)]
    [InlineData("عقد رقم 7", true)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("what?", false)]
    [InlineData("line\nbreak", false)]
    public void File_names_must_not_contain_path_or_reserved_characters(string name, bool expected) =>
        Assert.Equal(expected, OcrRules.IsValidFileName(name));
}
