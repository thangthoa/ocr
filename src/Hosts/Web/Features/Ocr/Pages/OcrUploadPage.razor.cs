using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Domain.Jobs;

namespace OppOcr.Web.Features.Ocr.Pages;

public sealed partial class OcrUploadPage : IDisposable
{
    public const int MaxFilesPerBatch = 20;

    private static readonly string Accept = string.Join(',', OcrFileFormats.SupportedExtensions);

    private readonly CancellationTokenSource _disposed = new();
    private readonly List<UploadItem> _uploads = [];
    private OcrDocumentType? _documentType;
    private bool _uploading;
    private string? _batchError;

    [Inject] private UploadOcrFileHandler UploadHandler { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> Text { get; set; } = default!;

    [Inject] private ILogger<OcrUploadPage> Logger { get; set; } = default!;

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }

    private async Task UploadAsync(InputFileChangeEventArgs e)
    {
        _batchError = null;
        if (e.FileCount > MaxFilesPerBatch)
        {
            _batchError = Text["TooManyFiles", MaxFilesPerBatch];
            return;
        }

        var files = e.GetMultipleFiles(MaxFilesPerBatch);
        var batch = files.Select(f => new UploadItem(f.Name, f.Size)).ToList();
        _uploads.Clear();
        _uploads.AddRange(batch);
        _uploading = true;
        StateHasChanged();

        try
        {
            // Each file is sent to the OCR service as its own job, in the order selected.
            for (var i = 0; i < files.Count; i++)
            {
                await UploadOneAsync(files[i], batch[i]);
                StateHasChanged();
            }
        }
        finally
        {
            _uploading = false;
        }
    }

    private async Task UploadOneAsync(IBrowserFile file, UploadItem item)
    {
        try
        {
            await using var stream = file.OpenReadStream(maxAllowedSize: Math.Max(file.Size, 1), _disposed.Token);
            var result = await UploadHandler.HandleAsync(new UploadOcrFileCommand(file.Name, file.Size, stream, _documentType), _disposed.Token);
            if (result.IsSuccess)
                item.MarkSucceeded(result.Value.Id.Value);
            else
                item.MarkFailed(Text.Localize(result.Error!));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
            item.MarkFailed(Text["UnexpectedError"]);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "OCR UI: failed to upload a file")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);

    private enum UploadStatus
    {
        Uploading,
        Succeeded,
        Failed,
    }

    private sealed class UploadItem(string fileName, long sizeBytes)
    {
        public string FileName { get; } = fileName;

        public long SizeBytes { get; } = sizeBytes;

        public UploadStatus Status { get; private set; } = UploadStatus.Uploading;

        public string? JobId { get; private set; }

        public string? ErrorMessage { get; private set; }

        public void MarkSucceeded(string jobId)
        {
            Status = UploadStatus.Succeeded;
            JobId = jobId;
        }

        public void MarkFailed(string message)
        {
            Status = UploadStatus.Failed;
            ErrorMessage = message;
        }
    }
}
