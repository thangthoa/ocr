using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.ReceiveOcrJobCallback;

public sealed record ReceiveOcrJobCallbackCommand(OcrCallback Callback);

/// <summary>
/// Records a job the OCR service posted when it finished or failed (ADR 0006). The service sends a job
/// again when it got no answer, so the latest copy simply replaces the one held.
/// </summary>
public sealed partial class ReceiveOcrJobCallbackHandler(
    IOcrCallbackReader reader,
    IOcrJobRegistry registry,
    ILogger<ReceiveOcrJobCallbackHandler> logger)
{
    /// <returns>
    /// The change to announce, or null when nothing visible changed (a repeated callback, or the worker saw
    /// the end first). Announcing is left to the caller: the service waits only 10 seconds for an answer,
    /// and open pages reload the job from the service when they are told.
    /// </returns>
    public Result<OcrJobChange?> Handle(ReceiveOcrJobCallbackCommand command)
    {
        var read = reader.Read(command.Callback);
        if (read.IsFailure)
        {
            LogRejected(read.Error!.Code);
            return read.Error;
        }

        var job = read.Value;
        // Not tracked when it was submitted before this instance started; it is still one of ours, since
        // the service only calls back to the address this application gave it.
        var previous = registry.Update(job);
        if (previous is null)
            registry.Add(job);
        LogReceived(job.Id.Value, job.Status, job.ErrorCode);

        OcrJobChange? change = job.HasVisibleChangesSince(previous)
            ? new OcrJobChange(job.Id, job.FileName, job.Status, OcrJobChangeKind.Updated, job.ErrorCode)
            : null;
        return Result.Success(change);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: callback for job {JobId} ({Status}, {ErrorCode})")]
    private partial void LogReceived(string jobId, OcrJobStatus status, string? errorCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: rejected a callback ({ErrorCode})")]
    private partial void LogRejected(string errorCode);
}
