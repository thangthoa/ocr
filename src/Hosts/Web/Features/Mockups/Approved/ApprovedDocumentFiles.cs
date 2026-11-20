using System.Buffers;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Localization;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.Features.Mockups.Approved;

public enum FinalFileFormat
{
    Docx,
    Txt,
}

internal sealed record DownloadFile(byte[] Content, string ContentType, string FileName);

internal static class ApprovedErrors
{
    public const string NotFinalCode = "documents.not_final";

    public static readonly Error NotFinal = Error.Conflict(NotFinalCode, "Only approved or archived documents have a final version.");
}

/// <summary>
/// Files people take away from approved documents: the final version of one document and the list
/// of approved documents as CSV. Every download is written to the audit log.
/// </summary>
internal sealed class ApprovedDocumentFiles(
    DocumentCatalog catalog,
    IWordFileStore wordFiles,
    AuditLog audit,
    IStringLocalizer<MockupText> text,
    TimeProvider time)
{
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Spreadsheet applications run a cell that starts like a formula (CSV injection).</summary>
    private static readonly SearchValues<char> FormulaStart = SearchValues.Create("=+-@\t\r");

    private static readonly SearchValues<char> NeedsQuotes = SearchValues.Create(",\"\r\n");

    /// <summary>Archived (approved) documents matching the query: real ones before samples, then the newest approval first.</summary>
    public static IReadOnlyList<DocumentRecord> Approved(IEnumerable<DocumentRecord> documents, DocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return DocumentStore.Filter(documents, query with { Status = DocumentStatus.Archived })
            .OrderBy(d => d.IsSample)
            .ThenByDescending(d => d.Approval?.AtUtc ?? DateTime.MinValue)
            .ToList();
    }

    /// <summary>
    /// The final text of an approved (or archived) document. DOCX is the latest Word file when it holds
    /// the current text (saved from the Word editor, or the untouched OCR file); otherwise, for example
    /// after an edit in the text editor, a DOCX made from the current text. TXT separates pages with a
    /// form feed.
    /// </summary>
    public async Task<Result<DownloadFile>> FinalAsync(string id, FinalFileFormat format, string user, CancellationToken cancellationToken)
    {
        var found = await catalog.GetAsync(id, cancellationToken);
        if (found.IsFailure)
            return found.Error!;

        var document = found.Value;
        if (!document.IsFinal)
            return ApprovedErrors.NotFinal;

        var name = Path.GetFileNameWithoutExtension(document.FileName);
        var version = document.CurrentVersion?.Number ?? "1.0";
        DownloadFile file;
        if (format == FinalFileFormat.Txt)
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
            file = new DownloadFile(DocxText.Create(document.PageTexts.Count > 0 ? document.PageTexts : [string.Empty]), DocxContentType, $"{name}-v{version}.docx");
        }

        audit.Record(user, AuditAction.Downloaded, document.Id, document.FileName, version: version, detail: format.ToString().ToUpperInvariant());
        return file;
    }

    /// <summary>
    /// The approved documents matching the query as CSV (UTF-8 with a byte order mark, so spreadsheet
    /// applications show Arabic correctly). Fails when the OCR jobs cannot be loaded, rather than
    /// exporting only the samples.
    /// </summary>
    public async Task<Result<DownloadFile>> ExportAsync(DocumentQuery query, string user, CancellationToken cancellationToken)
    {
        var list = await catalog.ListAsync(cancellationToken);
        if (list.JobsError is { } error)
            return error;

        var documents = Approved(list.Documents, query);
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

    private async Task<WordVersion?> MatchingWordFileAsync(DocumentRecord document, CancellationToken cancellationToken)
    {
        var versions = await wordFiles.ListAsync(document.Id, cancellationToken);
        if (versions.Count == 0 || document.CurrentVersion is not { } current)
            return null;

        var latest = versions[^1];
        var matches = current.Change switch
        {
            DocumentVersionChange.EditedInWord => current.WordVersion == latest.Number,
            DocumentVersionChange.OcrResult => latest.Source == WordVersionSource.Ocr,
            _ => false,
        };
        return matches ? latest : null;
    }
}
