using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.DeleteOcrJob;

public sealed record DeleteOcrJobCommand(string JobId);

/// <summary>
/// The service has no delete operation: a queued job is cancelled there, and the job is removed
/// from this application's list. A running or finished job stays on the service.
/// </summary>
public sealed partial class DeleteOcrJobHandler(
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<DeleteOcrJobHandler> logger)
{
    public async Task<Result> HandleAsync(DeleteOcrJobCommand command, CancellationToken cancellationToken)
    {
        if (!OcrJobId.TryParse(command.JobId, out var id))
            return OcrErrors.NotFound;

        var current = await api.GetAsync(id, cancellationToken);
        if (current.IsFailure && current.Error!.Type != ErrorType.NotFound)
            return OcrErrors.ServiceUnavailable(current.Error);

        if (current.IsSuccess && current.Value.CanCancel)
        {
            var cancel = await api.CancelAsync(id, cancellationToken);
            // not_cancellable means it started meanwhile; it can no longer be stopped.
            if (cancel.IsFailure && cancel.Error!.Type == ErrorType.Failure)
                return OcrErrors.ServiceUnavailable(cancel.Error);
            if (cancel.IsSuccess)
                LogCancelled(id.Value);
        }

        var known = registry.Find(id);
        registry.Remove(id);
        var fileName = current.IsSuccess ? current.Value.FileName : known?.FileName ?? id.Value;
        var status = current.IsSuccess ? current.Value.Status : known?.Status ?? OcrJobStatus.Failed;
        await notifier.NotifyAsync(new OcrJobChange(id, fileName, status, OcrJobChangeKind.Deleted), cancellationToken);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: cancelled queued job {JobId}")]
    private partial void LogCancelled(string jobId);
}
