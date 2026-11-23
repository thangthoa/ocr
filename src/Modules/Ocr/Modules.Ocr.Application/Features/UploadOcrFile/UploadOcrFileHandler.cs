using FluentValidation;
using Microsoft.Extensions.Logging;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.UploadOcrFile;

/// <summary>
/// Checks one uploaded file and sends it straight to the OCR service as its own job. Nothing is
/// stored by this application; the service keeps the file and the result.
/// </summary>
public sealed partial class UploadOcrFileHandler(
    IValidator<UploadOcrFileCommand> validator,
    IOcrApiClient api,
    IOcrJobRegistry registry,
    IOcrJobNotifier notifier,
    ILogger<UploadOcrFileHandler> logger)
{
    public async Task<Result<OcrJob>> HandleAsync(UploadOcrFileCommand command, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        // Checked here, before anything is sent to a third party: the client-reported size and
        // the extension cannot be trusted, so the read is capped and the signature verified.
        await using var buffer = new MemoryStream();
        if (!await TryCopyBoundedAsync(command.Content, buffer, OcrRules.MaxSizeBytes, cancellationToken))
            return OcrErrors.TooLarge;

        var header = buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, OcrFileFormats.SignatureLength));
        var detected = OcrFileFormats.Detect(header);
        if (detected is null || detected != OcrFileFormats.FromFileName(command.FileName))
            return OcrErrors.ContentMismatch;

        var sizeBytes = buffer.Length;
        buffer.Position = 0;
        var result = await api.SubmitAsync(buffer, command.FileName, cancellationToken);
        if (result.IsFailure)
        {
            LogRejected(command.FileName, result.Error!.Code);
            return result.Error.Type == ErrorType.Failure ? OcrErrors.ServiceUnavailable(result.Error) : result.Error;
        }

        var job = result.Value;
        registry.Add(job);
        LogSubmitted(job.Id.Value, detected.Value, sizeBytes);
        await notifier.NotifyAsync(new OcrJobChange(job.Id, job.FileName, job.Status, OcrJobChangeKind.Created), cancellationToken);
        return job;
    }

    private static async Task<bool> TryCopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
                return false;
            await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return total > 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Ocr: submitted job {JobId} ({Format}, {SizeBytes} bytes)")]
    private partial void LogSubmitted(string jobId, OcrFileFormat format, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ocr: the service did not accept {FileName} ({ErrorCode})")]
    private partial void LogRejected(string fileName, string errorCode);
}
