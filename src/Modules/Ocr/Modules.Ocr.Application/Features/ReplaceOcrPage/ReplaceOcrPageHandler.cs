using FluentValidation;
using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Common;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.ReplaceOcrPage;

/// <summary>
/// Replaces one page of a job with a new image. The service reads only that page again; the other pages keep
/// their result (API doc §3.8).
/// </summary>
public sealed partial class ReplaceOcrPageHandler(
    IValidator<ReplaceOcrPageCommand> validator,
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<ReplaceOcrPageHandler> logger)
{
    public async Task<Result<OcrJob>> HandleAsync(ReplaceOcrPageCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();
        if (!OcrJobId.TryParse(command.JobId, out var id))
            return OcrErrors.NotFound;

        var current = await PageEditing.LoadEditableAsync(api, id, cancellationToken);
        if (current.IsFailure)
            return current.Error!;
        if (!current.Value.HasPage(command.PageNumber))
            return OcrErrors.PageNotFound;

        var read = await UploadedFiles.ReadAsync(command.File.Content, command.File.FileName, OcrRules.MaxSizeBytes, cancellationToken);
        if (read.IsFailure)
            return read.Error!;

        var result = await api.ReplacePageAsync(id, command.PageNumber, read.Value, cancellationToken);
        if (result.IsFailure)
        {
            LogRejected(id.Value, command.PageNumber, result.Error!.Code);
            return PageEditing.FromService(result.Error);
        }

        await PageEditing.TrackAsync(current.Value, result.Value, registry, notifier, cancellationToken);
        LogReplaced(id.Value, command.PageNumber);
        return result.Value;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: replaced page {PageNumber} of job {JobId}")]
    private partial void LogReplaced(string jobId, int pageNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: the service did not replace page {PageNumber} of job {JobId} ({ErrorCode})")]
    private partial void LogRejected(string jobId, int pageNumber, string errorCode);
}
