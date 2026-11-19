using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.SyncTrackedOcrJobs;

/// <summary>Refreshes unfinished jobs submitted through this application and announces changes.</summary>
public sealed partial class SyncTrackedOcrJobsHandler(
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<SyncTrackedOcrJobsHandler> logger)
{
    /// <returns>Number of jobs whose visible state changed.</returns>
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var changed = 0;
        foreach (var known in registry.Unfinished())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latest = await RefreshAsync(known, cancellationToken);
            if (latest is null)
                continue;

            var previous = registry.Update(latest);
            if (previous is null || !latest.HasVisibleChangesSince(previous))
                continue;

            changed++;
            await notifier.NotifyAsync(
                new OcrJobChange(latest.Id, latest.FileName, latest.Status, OcrJobChangeKind.Updated, latest.ErrorCode), cancellationToken);
        }

        return changed;
    }

    private async Task<OcrJob?> RefreshAsync(OcrJob known, CancellationToken cancellationToken)
    {
        var result = await api.GetAsync(known.Id, cancellationToken);
        if (result.IsSuccess)
            return result.Value;

        if (result.Error!.Type == ErrorType.NotFound)
        {
            // The demo server can lose jobs when it is reset; there is nothing left to wait for.
            return known with
            {
                Status = OcrJobStatus.Failed,
                CurrentStep = null,
                ErrorCode = OcrServiceErrorCodes.RemoteJobLost,
                ErrorMessage = "The OCR service no longer knows this job.",
            };
        }

        LogRefreshFailed(known.Id.Value, result.Error.Code);
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: could not refresh job {JobId} ({ErrorCode}); will retry")]
    private partial void LogRefreshFailed(string jobId, string errorCode);
}
