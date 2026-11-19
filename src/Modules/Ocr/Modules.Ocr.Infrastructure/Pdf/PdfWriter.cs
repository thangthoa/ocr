using System.Globalization;
using System.Text;
using static System.FormattableString;

namespace Modules.Ocr.Infrastructure.Pdf;

/// <summary>
/// Writes a minimal PDF 1.4 file with one image per page: the catalog, the page tree, then for each
/// image its page, content stream and image object, and finally the cross-reference table.
/// </summary>
internal sealed class PdfWriter(Stream output)
{
    private const double PointsPerInch = 72;
    private const int FirstPageObject = 3;
    private const int ObjectsPerPage = 3;

    private long _position;

    /// <param name="pages">Each image with the resolution its page is sized for.</param>
    public async Task WriteAsync(IReadOnlyList<(PdfImage Image, double Dpi)> pages, CancellationToken cancellationToken)
    {
        var offsets = new long[FirstPageObject + (pages.Count * ObjectsPerPage)];

        // The comment of high bytes marks the file as binary for tools that guess (PDF spec 7.5.2).
        await WriteAsync("%PDF-1.4\n%âãÏÓ\n", cancellationToken);

        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => Invariant($"{PageObject(i)} 0 R")));
        await ObjectAsync(offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>", cancellationToken);
        await ObjectAsync(offsets, 2, Invariant($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"), cancellationToken);

        for (var i = 0; i < pages.Count; i++)
        {
            var (image, dpi) = pages[i];
            var (width, height, matrix) = Place(image, dpi);
            var page = PageObject(i);
            await ObjectAsync(offsets, page,
                Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width:0.###} {height:0.###}] ") +
                Invariant($"/Resources << /XObject << /Im0 {page + 2} 0 R >> >> /Contents {page + 1} 0 R >>"),
                cancellationToken);
            await StreamObjectAsync(offsets, page + 1, string.Empty, Encoding.Latin1.GetBytes($"q {matrix} cm /Im0 Do Q\n"), cancellationToken);
            await StreamObjectAsync(offsets, page + 2,
                Invariant($"/Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} {image.Entries}"),
                image.Data, cancellationToken);
        }

        var crossReference = _position;
        var table = new StringBuilder(Invariant($"xref\n0 {offsets.Length}\n0000000000 65535 f \n"));
        for (var number = 1; number < offsets.Length; number++)
            table.Append(Invariant($"{offsets[number]:D10} 00000 n \n"));
        table.Append(Invariant($"trailer\n<< /Size {offsets.Length} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n"));
        await WriteAsync(table.ToString(), cancellationToken);
    }

    private static int PageObject(int index) => FirstPageObject + (index * ObjectsPerPage);

    /// <summary>
    /// The page size in points and the matrix that draws the image on it. The image is drawn in a
    /// unit square; the matrix scales it to the page and applies the EXIF orientation.
    /// </summary>
    private static (double Width, double Height, string Matrix) Place(PdfImage image, double dpi)
    {
        var scale = PointsPerInch / dpi;
        var storedWidth = image.Width * scale;
        var storedHeight = image.Height * scale;

        // Orientations 5–8 turn the picture a quarter: the page is as wide as the stored image is tall.
        var (w, h) = image.Orientation >= 5 ? (storedHeight, storedWidth) : (storedWidth, storedHeight);
        double[] matrix = image.Orientation switch
        {
            2 => [-w, 0, 0, h, w, 0],  // mirrored left to right
            3 => [-w, 0, 0, -h, w, h], // upside down
            4 => [w, 0, 0, -h, 0, h],  // mirrored top to bottom
            5 => [0, -h, -w, 0, w, h], // transposed
            6 => [0, -h, w, 0, 0, h],  // turned a quarter clockwise
            7 => [0, h, w, 0, 0, 0],   // transversed
            8 => [0, h, -w, 0, w, 0],  // turned a quarter anticlockwise
            _ => [w, 0, 0, h, 0, 0],
        };
        return (w, h, string.Join(' ', matrix.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture))));
    }

    private async Task ObjectAsync(long[] offsets, int number, string body, CancellationToken cancellationToken)
    {
        offsets[number] = _position;
        await WriteAsync(Invariant($"{number} 0 obj\n{body}\nendobj\n"), cancellationToken);
    }

    private async Task StreamObjectAsync(long[] offsets, int number, string entries, ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
    {
        offsets[number] = _position;
        var dictionary = entries.Length == 0 ? Invariant($"<< /Length {data.Length} >>") : Invariant($"<< {entries} /Length {data.Length} >>");
        await WriteAsync(Invariant($"{number} 0 obj\n{dictionary}\nstream\n"), cancellationToken);
        await output.WriteAsync(data, cancellationToken);
        _position += data.Length;
        await WriteAsync("\nendstream\nendobj\n", cancellationToken);
    }

    private async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        await output.WriteAsync(bytes, cancellationToken);
        _position += bytes.Length;
    }
}
