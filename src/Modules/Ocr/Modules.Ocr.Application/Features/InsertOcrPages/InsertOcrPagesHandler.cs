using FluentValidation;
using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Common;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.InsertOcrPages;

/// <summary>
/// Adds pages to a job, for example a page that was forgotten when the document was scanned. The service reads
/// only the new pages and rebuilds the text and the Word file in the new page order (API doc §3.8).
/// </summary>
public sealed partial class InsertOcrPagesHandler(
    IValidator<InsertOcrPagesCommand> validator,
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<InsertOcrPagesHandler> logger)
{
    public async Task<Result<OcrJob>> HandleAsync(InsertOcrPagesCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();
        if (!OcrJobId.TryParse(command.JobId, out var id))
            return OcrErrors.NotFound;

        var current = await PageEditing.LoadEditableAsync(api, id, cancellationToken);
        if (current.IsFailure)
            return current.Error!;
        var job = current.Value;
        if (command.Position is { } position && !job.CanInsertAt(position))
            return OcrErrors.PageNotFound;

        // Each file adds at least one page; a PDF may add more, which only the service can count.
        if (job.PagesTotal + command.Files.Count > OcrRules.MaxPagesPerDocument)
            return Error.Validation(OcrErrorCodes.TooManyPages, $"A document can have at most {OcrRules.MaxPagesPerDocument} pages.");

        var uploads = new List<OcrUpload>(command.Files.Count);
        var allowance = OcrRules.MaxSizeBytes;
        foreach (var file in command.Files)
        {
            var read = await UploadedFiles.ReadAsync(file.Content, file.FileName, allowance, cancellationToken);
            if (read.IsFailure)
                return read.Error!;
            allowance -= read.Value.Content.Length;
            uploads.Add(read.Value);
        }

        var result = await api.InsertPagesAsync(id, uploads, command.Position, cancellationToken);
        if (result.IsFailure)
        {
            LogRejected(id.Value, result.Error!.Code);
            return PageEditing.FromService(result.Error);
        }

        await PageEditing.TrackAsync(current.Value, result.Value, registry, notifier, cancellationToken);
        LogInserted(id.Value, uploads.Count, command.Position, result.Value.PagesTotal);
        return result.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: inserted {FileCount} file(s) into job {JobId} at page {Position}; it now has {PagesTotal} pages")]
    private partial void LogInserted(string jobId, int fileCount, int? position, int pagesTotal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: the service did not insert pages into job {JobId} ({ErrorCode})")]
    private partial void LogRejected(string jobId, string errorCode);
}
