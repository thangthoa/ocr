using System.Globalization;
using Modules.Ocr.Application.Features.DeleteOcrPage;
using Modules.Ocr.Application.Features.InsertOcrPages;
using Modules.Ocr.Application.Features.ReplaceOcrPage;
using Modules.Ocr.Application.Features.UploadOcrPages;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Mockups.Data;

/// <summary>
/// Inserts, replaces and deletes pages of a document's OCR job (through the OCR module's use cases) and writes the
/// activity log. The document's text follows when the job is done again (<see cref="DocumentStore.FromJob"/>).
/// </summary>
public sealed class DocumentPageEditor(
    InsertOcrPagesHandler insertPages,
    ReplaceOcrPageHandler replacePage,
    DeleteOcrPageHandler deletePage,
    DocumentStore store,
    AuditLog audit)
{
    /// <param name="position">The page number the first new page gets (one more than the page count adds them at the end).</param>
    public async Task<Result<DocumentRecord>> InsertAsync(string id, IReadOnlyList<OcrPageFile> files, int position, string author, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var result = await insertPages.HandleAsync(new InsertOcrPagesCommand(id, files, position), cancellationToken);
        if (result.IsFailure)
            return result.Error!;

        audit.Record(author, AuditAction.PagesInserted, id, result.Value.FileName, version: Number(position), detail: Number(files.Count));
        return store.FromJob(result.Value);
    }

    public async Task<Result<DocumentRecord>> ReplaceAsync(string id, int pageNumber, OcrPageFile file, string author, CancellationToken cancellationToken)
    {
        var result = await replacePage.HandleAsync(new ReplaceOcrPageCommand(id, pageNumber, file), cancellationToken);
        if (result.IsFailure)
            return result.Error!;

        audit.Record(author, AuditAction.PageReplaced, id, result.Value.FileName, version: Number(pageNumber));
        return store.FromJob(result.Value);
    }

    public async Task<Result<DocumentRecord>> DeleteAsync(string id, int pageNumber, string author, CancellationToken cancellationToken)
    {
        var result = await deletePage.HandleAsync(new DeleteOcrPageCommand(id, pageNumber), cancellationToken);
        if (result.IsFailure)
            return result.Error!;

        audit.Record(author, AuditAction.PageDeleted, id, result.Value.FileName, version: Number(pageNumber));
        return store.FromJob(result.Value);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
