using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.Features.Mockups.Approved;

/// <summary>The formats of a final document, in the order the pages offer them.</summary>
public enum FinalFileFormat
{
    Docx,
    Pdf,
    Txt,
}

internal sealed record DownloadFile(byte[] Content, string ContentType, string FileName);

internal static class ApprovedErrors
{
    public const string NotFinalCode = "documents.not_final";

    public const string NoTextCode = "documents.no_text";
    public const string PdfUnavailableCode = "documents.pdf_unavailable";
    public const string PdfFailedCode = "documents.pdf_failed";
    public const string ExportTooLargeCode = "documents.export_too_large";

    public static readonly Error NotFinal = Error.Conflict(NotFinalCode, "Only approved or archived documents have a final version.");

    public static readonly Error NoText = Error.Conflict(NoTextCode, "The document has no recognized text to download yet.");

    public static readonly Error PdfUnavailable = Error.Validation(PdfUnavailableCode, "PDF needs the document server, which is not configured.");

    public static readonly Error PdfFailed = Error.Failure(PdfFailedCode, "The document server could not make the PDF.");

    public static Error ExportTooLarge(int documents) => Error.Validation(ExportTooLargeCode,
        string.Create(CultureInfo.InvariantCulture,
            $"{documents} documents match; narrow the filters to {ApprovedDocumentFiles.MaxExportRows} or fewer to export them."));
}

/// <summary>
/// Files people take away: the current version of a document in review, the final version of an
/// archived one, and the list of archived documents as CSV. Every download is written to the audit log.
/// </summary>
internal sealed partial class ApprovedDocumentFiles(
    DocumentCatalog catalog,
    IWordFileStore wordFiles,
    AuditLog audit,
    IStringLocalizer<MockupText> text,
    TimeProvider time,
    IOptions<WordEditorOptions> wordOptions,
    DocumentServerClient documentServer,
    WordLinks wordLinks,
    ILogger<ApprovedDocumentFiles> logger)
{
    public const string PdfContentType = "application/pdf";

    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Spreadsheet applications run a cell that starts like a formula (CSV injection).</summary>
    private static readonly SearchValues<char> FormulaStart = SearchValues.Create("=+-@\t\r");

    private static readonly SearchValues<char> NeedsQuotes = SearchValues.Create(",\"\r\n");

    /// <summary>The largest list the CSV export builds (the archive has about a million documents).</summary>
    public const int MaxExportRows = 50_000;

    /// <summary>PDF is made by the document server, so it is offered only when one is configured.</summary>
    public IReadOnlyList<FinalFileFormat> Formats =>
        wordOptions.Value.Enabled ? Enum.GetValues<FinalFileFormat>() : [FinalFileFormat.Docx, FinalFileFormat.Txt];

    /// <summary>The final text of an archived document (the Document Archive offers only these).</summary>
    public Task<Result<DownloadFile>> FinalAsync(string id, FinalFileFormat format, string user, CancellationToken cancellationToken) =>
        DownloadAsync(id, format, user, finalOnly: true, cancellationToken);

    /// <summary>The current text of a document in review, or the final text of an archived one (the document page).</summary>
    public Task<Result<DownloadFile>> CurrentAsync(string id, FinalFileFormat format, string user, CancellationToken cancellationToken) =>
        DownloadAsync(id, format, user, finalOnly: false, cancellationToken);

    /// <summary>A document has a text to download once OCR has recognized it: in review or archived.</summary>
    public static bool HasText(DocumentRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return WordEditorService.HasWordFile(document);
    }

    /// <summary>The current version as DOCX, for the document server to read (see <see cref="WordLinks.TextDocxUrl"/>).</summary>
    public static byte[] TextDocx(DocumentRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return DocxText.Create(document.PageTexts.Count > 0 ? document.PageTexts : [string.Empty]);
    }

    /// <summary>
    /// DOCX is the latest Word file when it holds the current text (saved from the Word editor, or the
    /// untouched OCR file); otherwise, for example after restoring an older version, a DOCX made from the
    /// current text. PDF is that same DOCX as the document server prints it (the engine of the Word editor,
    /// so Arabic looks the same). TXT separates pages with a form feed.
    /// </summary>
    private async Task<Result<DownloadFile>> DownloadAsync(string id, FinalFileFormat format, string user, bool finalOnly, CancellationToken cancellationToken)
    {
        var found = await catalog.GetAsync(id, cancellationToken);
        if (found.IsFailure)
            return found.Error!;

        var document = found.Value;
        if (finalOnly && !document.IsFinal)
            return ApprovedErrors.NotFinal;
        if (!HasText(document))
            return ApprovedErrors.NoText;

        var name = Path.GetFileNameWithoutExtension(document.FileName);
        var version = document.CurrentVersion?.Number ?? "1.0";
        DownloadFile file;
        if (format == FinalFileFormat.Pdf)
        {
            var pdf = await PdfAsync(document, cancellationToken);
            if (pdf.IsFailure)
                return pdf.Error!;
            file = new DownloadFile(pdf.Value, PdfContentType, $"{name}-v{version}.pdf");
        }
        else if (format == FinalFileFormat.Txt)
        {
            var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(string.Join("\n\f\n", document.PageTexts))).ToArray();
            file = new DownloadFile(content, "text/plain; charset=utf-8", $"{name}-v{version}.txt");
        }
        else if (await MatchingWordFileAsync(document, cancellationToken) is { } word)
        {
            await using var stream = await wordFiles.OpenAsync(document.Id, word.Number, cancellationToken)
                ?? throw new InvalidOperationException($"Word version {word.Number} of {document.Id} is listed but missing.");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            file = new DownloadFile(buffer.ToArray(), DocxContentType, string.Create(CultureInfo.InvariantCulture, $"{name}-word-v{word.Number}.docx"));
        }
        else
        {
            file = new DownloadFile(TextDocx(document), DocxContentType, $"{name}-v{version}.docx");
        }

        audit.Record(user, AuditAction.Downloaded, document.Id, document.FileName, version: version, detail: format.ToString().ToUpperInvariant());
        return file;
    }

    /// <summary>
    /// The approved documents matching the query as CSV (UTF-8 with a byte order mark, so spreadsheet
    /// applications show Arabic correctly), in the order of the Document Archive. Fails when the OCR jobs cannot be
    /// loaded, rather than exporting only the samples, and when more than <see cref="MaxExportRows"/> documents match.
    /// </summary>
    public async Task<Result<DownloadFile>> ExportAsync(DocumentQuery query, string user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var snapshot = await catalog.SnapshotAsync(cancellationToken);
        if (snapshot.List.JobsError is { } error)
            return error;

        var page = catalog.Query(snapshot, query with { Status = DocumentStatus.Archived }, DocumentOrder.NewestApproval, 0, MaxExportRows);
        if (page.Total > MaxExportRows)
            return ApprovedErrors.ExportTooLarge(page.Total);

        var documents = page.Items;
        var csv = new StringBuilder();
        AppendRow(csv,
        [
            text["Meta.Id"], text["Meta.FileName"], text["Meta.Title"], text["Meta.Reference"], text["Meta.DocumentDate"],
            text["Meta.Department"], text["Meta.Kind"], text["Meta.Confidentiality"], text["Meta.Keywords"], text["Meta.Pages"],
            text["Column.Version"], text["Meta.UploadedBy"], text["Meta.Uploaded"], text["Meta.ApprovedBy"], text["Meta.Approved"],
        ]);
        foreach (var document in documents)
        {
            var metadata = document.Metadata;
            AppendRow(csv,
            [
                document.Id,
                document.FileName,
                metadata.Title,
                metadata.Reference,
                metadata.DocumentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                metadata.Department is { } department ? text.Department(department) : null,
                metadata.Kind is { } kind ? text.Kind(kind) : null,
                text.Confidentiality(metadata.Confidentiality),
                string.Join("; ", metadata.Keywords),
                document.Pages > 0 ? document.Pages.ToString(CultureInfo.InvariantCulture) : null,
                document.CurrentVersion?.Number,
                document.UploadedBy,
                Timestamp(document.UploadedAtUtc),
                document.Approval?.By,
                Timestamp(document.Approval?.AtUtc),
            ]);
        }

        var fileName = "approved-documents-" + time.GetLocalNow().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".csv";
        audit.Record(user, AuditAction.Exported, detail: documents.Count.ToString(CultureInfo.InvariantCulture));
        return new DownloadFile(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", fileName);
    }

    /// <summary>A CSV field (RFC 4180), with formula-like values made inert by a leading apostrophe.</summary>
    internal static string CsvField(string? value)
    {
        value ??= string.Empty;
        if (value.Length > 0 && FormulaStart.Contains(value[0]))
            value = "'" + value;
        return value.AsSpan().ContainsAny(NeedsQuotes) ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> fields) =>
        csv.Append(string.Join(',', fields.Select(CsvField))).Append("\r\n");

    private static string? Timestamp(DateTime? utc) =>
        utc?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<Result<byte[]>> PdfAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        if (!wordOptions.Value.Enabled)
            return ApprovedErrors.PdfUnavailable;

        var word = await MatchingWordFileAsync(document, cancellationToken);
        var source = word is null ? wordLinks.TextDocxUrl(document.Id) : wordLinks.FileUrl(document.Id, word.Number);
        try
        {
            if (!await documentServer.IsReadyAsync(cancellationToken))
                return ApprovedErrors.PdfFailed;

            // A fresh key per request: the document server caches results by key, failures included.
            var pdf = await documentServer.ConvertAsync(new Dictionary<string, object?>
            {
                ["filetype"] = "docx",
                ["key"] = string.Create(CultureInfo.InvariantCulture, $"{document.Id}-final-{Guid.NewGuid():N}"),
                ["outputtype"] = "pdf",
                ["title"] = Path.GetFileNameWithoutExtension(document.FileName) + ".docx",
                ["url"] = source,
            }, cancellationToken);
            if (!pdf.AsSpan().StartsWith("%PDF-"u8))
                throw new DocumentServerException("The document server did not return a PDF.");
            return pdf;
        }
        catch (Exception ex) when (ex is DocumentServerException or HttpRequestException or System.Text.Json.JsonException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogPdfFailed(logger, ex, document.Id);
            return ApprovedErrors.PdfFailed;
        }
    }

    private async Task<WordVersion?> MatchingWordFileAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        var versions = await wordFiles.ListAsync(document.Id, cancellationToken);
        if (versions.Count == 0 || document.Versions.Count == 0)
            return null;

        // The version that last changed the text decides: an edit of the metadata only keeps the Word file.
        var index = document.Versions.Count - 1;
        while (index > 0 && document.Versions[index].PageTexts.SequenceEqual(document.Versions[index - 1].PageTexts, StringComparer.Ordinal))
            index--;
        var current = document.Versions[index];

        var latest = versions[^1];
        var matches = current.Change switch
        {
            DocumentVersionChange.EditedInWord or DocumentVersionChange.PagesChanged => current.WordVersion == latest.Number,
            DocumentVersionChange.OcrResult => latest.Source == WordVersionSource.Ocr,
            _ => false,
        };
        return matches ? latest : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archive: the PDF of {DocumentId} could not be made")]
    private static partial void LogPdfFailed(ILogger logger, Exception exception, string documentId);
}
