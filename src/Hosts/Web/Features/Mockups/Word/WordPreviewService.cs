using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Word;

/// <param name="Version">The Word version the pages show (always the latest).</param>
/// <param name="PageCount">Number of page images, served by <see cref="WordEditorEndpoints.PreviewPageUrl"/>.</param>
public sealed record WordPreview(WordVersion Version, int PageCount);

/// <summary>
/// Shows a document's Word file as page images rendered by the document server (the same engine as the
/// editor), next to the scanned original. Each version is rendered once and kept with its file.
/// </summary>
public sealed partial class WordPreviewService(
    WordEditorService editor,
    IWordFileStore files,
    DocumentServerClient server,
    WordLinks links,
    ILogger<WordPreviewService> logger)
{
    /// <summary>A4 at 150 dpi: sharp enough to read Arabic diacritics on a high-density screen.</summary>
    private const int PageWidth = 1240;
    private const int PageHeight = 1754;
    private const int MaxPages = 500;
    private const long MaxPageBytes = 20L * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public bool Enabled => editor.Enabled;

    public async Task<Result<WordPreview>> GetAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Enabled)
            return Error.Validation(WordErrors.EditorDisabled, "The Word editor is not configured.");
        if (!WordEditorService.HasWordFile(document))
            return Error.Conflict(WordErrors.NoWordFile, "The Word file is created when OCR finishes.");

        try
        {
            var version = await editor.CurrentVersionAsync(document, cancellationToken);
            if (await files.PreviewPageCountAsync(document.Id, version.Number, cancellationToken) is { } kept)
                return new WordPreview(version, kept);

            if (!await server.IsReadyAsync(cancellationToken))
                return Error.Failure(WordErrors.ServerNotReady, "The document server is not ready.");

            var pages = ReadPages(await server.ConvertAsync(ConversionRequest(document, version), cancellationToken));
            await files.SavePreviewAsync(document.Id, version.Number, pages, cancellationToken);
            return new WordPreview(version, pages.Count);
        }
        catch (Exception ex) when (ex is DocumentServerException or HttpRequestException or InvalidDataException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogPreviewFailed(logger, ex, document.Id);
            return Error.Failure(WordErrors.PreviewFailed, "The Word file could not be rendered.");
        }
    }

    /// <summary>
    /// The key only has to be unique per request: pages are kept here, and a fresh key keeps the document
    /// server from answering a retry with a failure it cached.
    /// </summary>
    private Dictionary<string, object?> ConversionRequest(DocumentRecord document, WordVersion version) => new()
    {
        ["filetype"] = "docx",
        ["key"] = string.Create(CultureInfo.InvariantCulture, $"{document.Id}-{version.Number}-{Guid.NewGuid():N}"),
        ["outputtype"] = "png",
        ["title"] = Path.GetFileNameWithoutExtension(document.FileName) + ".docx",
        ["url"] = links.FileUrl(document.Id, version.Number),
        ["thumbnail"] = new Dictionary<string, object?>
        {
            ["aspect"] = 1,
            ["first"] = false,
            ["width"] = PageWidth,
            ["height"] = PageHeight,
        },
    };

    /// <summary>
    /// All pages come back as a zip of <c>image1.png</c>, <c>image2.png</c>, … in no particular order;
    /// a one-page result may be a bare PNG. Entry names are only used to order pages, never as paths.
    /// </summary>
    internal static IReadOnlyList<byte[]> ReadPages(byte[] converted)
    {
        ArgumentNullException.ThrowIfNull(converted);
        if (IsPng(converted))
            return [converted];

        using var zip = new ZipArchive(new MemoryStream(converted, writable: false), ZipArchiveMode.Read);
        var entries = zip.Entries
            .Select(entry => (Entry: entry, Match: PageImageName().Match(entry.Name)))
            .Where(x => x.Match.Success)
            .OrderBy(x => int.Parse(x.Match.Groups[1].ValueSpan, CultureInfo.InvariantCulture))
            .Select(x => x.Entry)
            .ToList();
        if (entries.Count == 0)
            throw new InvalidDataException("The rendered Word file has no pages.");
        if (entries.Count > MaxPages)
            throw new InvalidDataException("The rendered Word file has too many pages.");

        var pages = new List<byte[]>(entries.Count);
        foreach (var entry in entries)
        {
            var page = ReadCapped(entry);
            if (!IsPng(page))
                throw new InvalidDataException("A rendered page is not a PNG image.");
            pages.Add(page);
        }

        return pages;
    }

    /// <summary>The size in the zip header is not trusted: reading stops past the limit.</summary>
    private static byte[] ReadCapped(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk)) > 0)
        {
            if (buffer.Length + read > MaxPageBytes)
                throw new InvalidDataException("A rendered page is larger than allowed.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool IsPng(byte[] content) => content.AsSpan().StartsWith(PngSignature);

    [GeneratedRegex(@"(\d{1,6})\.png$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PageImageName();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word preview: the Word file of {DocumentId} could not be rendered")]
    private static partial void LogPreviewFailed(ILogger logger, Exception exception, string documentId);
}
