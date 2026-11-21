using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Common;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.DeleteOcrPage;

/// <param name="PageNumber">From 1.</param>
public sealed record DeleteOcrPageCommand(string JobId, int PageNumber);

/// <summary>
/// Removes one page of a job, for example a page scanned twice. Nothing is read again: later pages move up and
/// the text is updated at once; the Word file is rebuilt in the background. A queued job will not read the page.
/// </summary>
public sealed partial class DeleteOcrPageHandler(
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<DeleteOcrPageHandler> logger)
{
    public async Task<Result<OcrJob>> HandleAsync(DeleteOcrPageCommand command, CancellationToken cancellationToken)
    {
        if (!OcrJobId.TryParse(command.JobId, out var id))
            return OcrErrors.NotFound;

        var current = await PageEditing.LoadEditableAsync(api, id, cancellationToken);
        if (current.IsFailure)
            return current.Error!;
        if (!current.Value.HasPage(command.PageNumber))
            return OcrErrors.PageNotFound;
        if (!current.Value.CanDeletePage)
            return OcrErrors.LastPage;

        var result = await api.DeletePageAsync(id, command.PageNumber, cancellationToken);
        if (result.IsFailure)
        {
            LogRejected(id.Value, command.PageNumber, result.Error!.Code);
            return PageEditing.FromService(result.Error);
        }

        await PageEditing.TrackAsync(current.Value, result.Value, registry, notifier, cancellationToken);
        LogDeleted(id.Value, command.PageNumber, result.Value.PagesTotal);
        return result.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: deleted page {PageNumber} of job {JobId}; {PagesTotal} pages left")]
    private partial void LogDeleted(string jobId, int pageNumber, int pagesTotal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: the service did not delete page {PageNumber} of job {JobId} ({ErrorCode})")]
    private partial void LogRejected(string jobId, int pageNumber, string errorCode);
}
