using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using Modules.Ocr.Infrastructure.Pdf;

namespace Modules.Ocr.Infrastructure.Tests;

public sealed partial class ImagePdfComposerTests
{
    private readonly ImagePdfComposer _composer = new(NullLogger<ImagePdfComposer>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Jpeg_is_embedded_unchanged_on_a_page_sized_from_its_resolution()
    {
        var jpeg = TestImages.Jpeg(600, 300, dpi: 200);

        var pdf = await ComposeAsync(new PageImage(OcrFileFormat.Jpeg, jpeg));

        var page = Assert.Single(pdf.Pages);
        Assert.Equal("0 0 216 108", page.MediaBox); // 600 × 300 px at 200 dpi
        Assert.Equal("216 0 0 108 0 0", page.Matrix);
        Assert.Contains("/Width 600 /Height 300 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", page.ImageDictionary, StringComparison.Ordinal);
        Assert.Equal(jpeg, page.ImageData);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(72)]
    [InlineData(5000)]
    public async Task Images_without_a_believable_resolution_are_sized_as_300_dpi_scans(int? dpi)
    {
        var pdf = await ComposeAsync(new PageImage(OcrFileFormat.Jpeg, TestImages.Jpeg(3000, 1500, dpi: dpi)));

        Assert.Equal("0 0 720 360", Assert.Single(pdf.Pages).MediaBox);
    }

    [Theory]
    [InlineData(1, true, "0 0 144 72", "144 0 0 72 0 0")]
    [InlineData(3, true, "0 0 144 72", "-144 0 0 -72 144 72")]
    [InlineData(6, true, "0 0 72 144", "0 -144 72 0 0 144")]
    [InlineData(6, false, "0 0 72 144", "0 -144 72 0 0 144")]
    [InlineData(8, false, "0 0 72 144", "0 144 -72 0 72 0")]
    public async Task Exif_orientation_of_phone_photos_is_applied_to_the_page(int orientation, bool littleEndian, string mediaBox, string matrix)
    {
        var jpeg = TestImages.Jpeg(600, 300, dpi: 300, orientation: orientation, littleEndianExif: littleEndian);

        var page = Assert.Single((await ComposeAsync(new PageImage(OcrFileFormat.Jpeg, jpeg))).Pages);

        Assert.Equal((mediaBox, matrix), (page.MediaBox, page.Matrix));
    }

    [Fact]
    public async Task Gray_and_adobe_cmyk_jpegs_get_the_right_color_space()
    {
        var pdf = await ComposeAsync(
            new PageImage(OcrFileFormat.Jpeg, TestImages.Jpeg(10, 10, components: 1)),
            new PageImage(OcrFileFormat.Jpeg, TestImages.Jpeg(10, 10, components: 4, adobe: true)));

        Assert.Contains("/ColorSpace /DeviceGray ", pdf.Pages[0].ImageDictionary, StringComparison.Ordinal);
        Assert.Contains("/ColorSpace /DeviceCMYK /BitsPerComponent 8 /Filter /DCTDecode /Decode [1 0 1 0 1 0 1 0]", pdf.Pages[1].ImageDictionary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PngColor.Gray, 8, "/ColorSpace /DeviceGray /BitsPerComponent 8", 1)]
    [InlineData(PngColor.Gray, 1, "/ColorSpace /DeviceGray /BitsPerComponent 1", 1)]
    [InlineData(PngColor.Rgb, 16, "/ColorSpace /DeviceRGB /BitsPerComponent 16", 3)]
    public async Task Png_without_alpha_is_embedded_as_it_is_with_the_png_predictor(PngColor color, int bitDepth, string colorEntries, int colors)
    {
        var png = TestImages.Png(9, 4, color, bitDepth, varyFilters: true, pixelsPerMetre: 7874); // 200 dpi

        var page = Assert.Single((await ComposeAsync(new PageImage(OcrFileFormat.Png, png))).Pages);

        Assert.Contains(colorEntries + " /Filter /FlateDecode", page.ImageDictionary, StringComparison.Ordinal);
        Assert.Contains($"/DecodeParms << /Predictor 15 /Colors {colors} /BitsPerComponent {bitDepth} /Columns 9 >>", page.ImageDictionary, StringComparison.Ordinal);
        Assert.Equal(TestImages.PngImageData(png), page.ImageData);
        Assert.Equal("0 0 3.24 1.44", page.MediaBox);
    }

    [Fact]
    public async Task Palette_png_keeps_its_palette()
    {
        var png = TestImages.Png(4, 2, PngColor.Palette, 2, palette: [0xFF, 0, 0, 0, 0xFF, 0, 0, 0, 0xFF]);

        var page = Assert.Single((await ComposeAsync(new PageImage(OcrFileFormat.Png, png))).Pages);

        Assert.Contains("/ColorSpace [/Indexed /DeviceRGB 2 <FF000000FF000000FF>] /BitsPerComponent 2", page.ImageDictionary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PngColor.RgbAlpha, 8)]
    [InlineData(PngColor.RgbAlpha, 16)]
    [InlineData(PngColor.GrayAlpha, 8)]
    [InlineData(PngColor.GrayAlpha, 16)]
    public async Task Png_with_alpha_is_flattened_onto_white(PngColor color, int bitDepth)
    {
        const int Width = 7;
        const int Height = 6;
        var png = TestImages.Png(Width, Height, color, bitDepth, varyFilters: true);

        var page = Assert.Single((await ComposeAsync(new PageImage(OcrFileFormat.Png, png))).Pages);

        var colors = color == PngColor.RgbAlpha ? 3 : 1;
        Assert.Contains($"/ColorSpace {(colors == 3 ? "/DeviceRGB" : "/DeviceGray")} /BitsPerComponent 8 /Filter /FlateDecode", page.ImageDictionary, StringComparison.Ordinal);
        Assert.DoesNotContain("/DecodeParms", page.ImageDictionary, StringComparison.Ordinal);
        Assert.Equal(TestImages.Flattened(Width, Height, color, bitDepth), Inflate(page.ImageData));
    }

    [Fact]
    public async Task Pages_keep_the_given_order_in_a_well_formed_file()
    {
        var pdf = await ComposeAsync(
            new PageImage(OcrFileFormat.Png, TestImages.Png(30, 10, PngColor.Rgb, 8)),
            new PageImage(OcrFileFormat.Jpeg, TestImages.Jpeg(20, 10)),
            new PageImage(OcrFileFormat.Png, TestImages.Png(10, 10, PngColor.RgbAlpha, 8)));

        Assert.Equal([30, 20, 10], pdf.Pages.Select(p => p.ImageWidth));
        Assert.Contains("/Type /Pages /Kids [3 0 R 6 0 R 9 0 R] /Count 3", pdf.Objects[2], StringComparison.Ordinal);
        Assert.Contains("/Type /Catalog /Pages 2 0 R", pdf.Objects[1], StringComparison.Ordinal);
    }

    public static TheoryData<OcrFileFormat, byte[]> UnsupportedImages => new()
    {
        { OcrFileFormat.Png, TestImages.Png(4, 4, PngColor.Rgb, 8, interlaced: true) },
        { OcrFileFormat.Png, TestImages.Png(4, 4, PngColor.Palette, 8) }, // no palette
        { OcrFileFormat.Png, TestImages.Png(4, 4, PngColor.RgbAlpha, 8, badFilter: true) },
        { OcrFileFormat.Png, TestImages.Png(4, 4, PngColor.RgbAlpha, 8)[..60] }, // cut off
        { OcrFileFormat.Png, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0] },
        { OcrFileFormat.Jpeg, TestImages.Jpeg(10, 10, precision: 12) },
        { OcrFileFormat.Jpeg, TestImages.Jpeg(10, 0) },
        { OcrFileFormat.Jpeg, TestImages.Jpeg(10, 10, dpi: 300)[..20] }, // cut off before the frame header
        { OcrFileFormat.Jpeg, [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0] },
        { OcrFileFormat.Pdf, "%PDF-1.4"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(UnsupportedImages))]
    public async Task Images_that_cannot_be_placed_on_a_page_are_reported(OcrFileFormat format, byte[] content)
    {
        using var output = new MemoryStream();

        var result = await _composer.ComposeAsync([new PageImage(OcrFileFormat.Png, TestImages.Png(2, 2, PngColor.Gray, 8)), new PageImage(format, content)], output, Ct);

        Assert.Equal(OcrErrors.ImageNotSupported, result.Error);
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private async Task<ParsedPdf> ComposeAsync(params PageImage[] pages)
    {
        using var output = new MemoryStream();
        var result = await _composer.ComposeAsync(pages, output, Ct);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return ParsedPdf.Read(output.ToArray());
    }

    /// <summary>Reads the file back through its cross-reference table, as a PDF reader would.</summary>
    private sealed partial record ParsedPdf(Dictionary<int, string> Objects, List<ParsedPage> Pages)
    {
        public static ParsedPdf Read(byte[] pdf)
        {
            var text = Encoding.Latin1.GetString(pdf);
            Assert.StartsWith("%PDF-1.4\n", text, StringComparison.Ordinal);
            Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);

            var startXref = text.LastIndexOf("startxref\n", StringComparison.Ordinal);
            var xref = int.Parse(text.AsSpan(startXref + 10, text.IndexOf('\n', startXref + 10) - startXref - 10), CultureInfo.InvariantCulture);
            var header = XrefHeader().Match(text, xref);
            Assert.True(header.Success && header.Index == xref, "startxref does not point at the xref table");

            var count = int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture);
            var entries = header.Index + header.Length;
            Assert.Equal("0000000000 65535 f \n", text.Substring(entries, 20));
            var objects = new Dictionary<int, string>();
            for (var number = 1; number < count; number++)
            {
                var entry = text.Substring(entries + (number * 20), 20);
                Assert.EndsWith(" 00000 n \n", entry, StringComparison.Ordinal);
                var offset = int.Parse(entry.AsSpan(0, 10), CultureInfo.InvariantCulture);
                var prefix = $"{number} 0 obj\n";
                Assert.Equal(prefix, text.Substring(offset, prefix.Length));
                var end = text.IndexOf("endobj\n", offset, StringComparison.Ordinal);
                objects[number] = text[(offset + prefix.Length)..end];
            }

            var pages = new List<ParsedPage>();
            foreach (Match kid in Kids().Matches(objects[2]))
            {
                var page = objects[int.Parse(kid.Groups[1].Value, CultureInfo.InvariantCulture)];
                var mediaBox = MediaBox().Match(page).Groups[1].Value;
                var contents = int.Parse(Contents().Match(page).Groups[1].Value, CultureInfo.InvariantCulture);
                var image = int.Parse(ImageRef().Match(page).Groups[1].Value, CultureInfo.InvariantCulture);
                var matrix = Matrix().Match(Stream(objects[contents])).Groups[1].Value;
                var imageObject = objects[image];
                var dictionary = imageObject[..imageObject.IndexOf("\nstream\n", StringComparison.Ordinal)];
                var width = int.Parse(Width().Match(dictionary).Groups[1].Value, CultureInfo.InvariantCulture);
                pages.Add(new ParsedPage(mediaBox, matrix, dictionary, Encoding.Latin1.GetBytes(Stream(imageObject)), width));
            }

            return new ParsedPdf(objects, pages);
        }

        /// <summary>The stream bytes, taken by the declared /Length.</summary>
        private static string Stream(string obj)
        {
            var length = int.Parse(Length().Match(obj).Groups[1].Value, CultureInfo.InvariantCulture);
            var start = obj.IndexOf("\nstream\n", StringComparison.Ordinal) + 8;
            Assert.Equal("\nendstream\n", obj.Substring(start + length, 11));
            return obj.Substring(start, length);
        }

        [GeneratedRegex(@"xref\n0 (\d+)\n")]
        private static partial Regex XrefHeader();

        [GeneratedRegex(@"(\d+) 0 R")]
        private static partial Regex Kids();

        [GeneratedRegex(@"/MediaBox \[([^\]]+)\]")]
        private static partial Regex MediaBox();

        [GeneratedRegex(@"/Contents (\d+) 0 R")]
        private static partial Regex Contents();

        [GeneratedRegex(@"/Im0 (\d+) 0 R")]
        private static partial Regex ImageRef();

        [GeneratedRegex(@"^q (.+) cm /Im0 Do Q\n$")]
        private static partial Regex Matrix();

        [GeneratedRegex(@"/Width (\d+)")]
        private static partial Regex Width();

        [GeneratedRegex(@"/Length (\d+)")]
        private static partial Regex Length();
    }

    private sealed record ParsedPage(string MediaBox, string Matrix, string ImageDictionary, byte[] ImageData, int ImageWidth);
}
