using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.Features.Ocr.Pages;

public sealed partial class OcrUploadPage : IDisposable
{
    public const int MaxFilesPerBatch = 20;

    /// <summary>Upper bound on entries read from a chosen folder, before unsupported files are skipped.</summary>
    public const int MaxFolderEntries = 1000;

    /// <summary>Newest scans listed under the upload area; the rest are in Documents.</summary>
    public const int HistorySize = 10;

    private static readonly string Accept = string.Join(',', OcrFileFormats.SupportedExtensions);

    private readonly CancellationTokenSource _disposed = new();
    private readonly List<UploadItem> _uploads = [];
    private OcrDocumentType? _documentType;
    private bool _uploading;
    private string? _batchError;
    private string? _folderNote;
    private IDisposable? _subscription;
    private OcrJobList? _history;
    private string? _historyError;
    private string _batchId = string.Empty;
    private string _boxBarcode = string.Empty;
    private DocumentDepartment? _batchDepartment;
    private int? _expectedFiles;
    private CaptureBatch? _lastBatch;

    [Inject] private UploadOcrFileHandler UploadHandler { get; set; } = default!;

    [Inject] private ListOcrJobsHandler ListHandler { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private CaptureBatchStore Batches { get; set; } = default!;

    [Inject] private AuditLog Audit { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> MockupText { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> Text { get; set; } = default!;

    [Inject] private ILogger<OcrUploadPage> Logger { get; set; } = default!;

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        _subscription = Notifications.Subscribe(OnJobChangedAsync);
        _batchId = Batches.NewBatchId();
        await LoadHistoryAsync();
    }

    private Task OnJobChangedAsync(OcrJobChange change) =>
        InvokeAsync(async () =>
        {
            await LoadHistoryAsync();
            StateHasChanged();
        });

    private async Task LoadHistoryAsync()
    {
        _historyError = null;
        try
        {
            var result = await ListHandler.HandleAsync(new ListOcrJobsQuery(1, HistorySize), _disposed.Token);
            if (result.IsSuccess)
                _history = result.Value;
            else
                _historyError = Text.Localize(result.Error!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpectedHistory(Logger, ex);
            _historyError = Text["LoadFailed"];
        }
    }

    private async Task UploadAsync(InputFileChangeEventArgs e)
    {
        _batchError = null;
        _folderNote = null;
        if (e.FileCount > MaxFilesPerBatch)
        {
            _batchError = Text["TooManyFiles", MaxFilesPerBatch];
            return;
        }

        await UploadFilesAsync(e.GetMultipleFiles(MaxFilesPerBatch));
    }

    /// <summary>
    /// A folder often holds files the service cannot read (thumbnails, notes); those are skipped
    /// up front instead of failing one by one. The batch limit still applies to what is left.
    /// </summary>
    private async Task UploadFolderAsync(InputFileChangeEventArgs e)
    {
        _batchError = null;
        _folderNote = null;
        if (e.FileCount > MaxFolderEntries)
        {
            _batchError = Text["FolderTooLarge", MaxFolderEntries];
            return;
        }

        var all = e.GetMultipleFiles(MaxFolderEntries);
        var supported = all.Where(f => OcrFileFormats.FromFileName(f.Name) is not null).ToList();
        if (supported.Count == 0)
        {
            _batchError = Text["FolderEmpty", OcrTextExtensions.SupportedFormatsLabel];
            return;
        }

        if (supported.Count > MaxFilesPerBatch)
        {
            _batchError = Text["FolderTooManyFiles", supported.Count, MaxFilesPerBatch];
            return;
        }

        var skipped = all.Count - supported.Count;
        if (skipped > 0)
            _folderNote = Text["FolderSkipped", skipped];

        await UploadFilesAsync(supported);
    }

    private async Task UploadFilesAsync(IReadOnlyList<IBrowserFile> files)
    {
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

        RecordBatch(batch);
        await LoadHistoryAsync();
    }

    /// <summary>Keeps the batch ticket with its jobs (for reconciliation and documents) and starts a new ticket.</summary>
    private void RecordBatch(List<UploadItem> items)
    {
        var uploaded = items.Where(i => i.Status == UploadStatus.Succeeded).ToList();
        _lastBatch = new CaptureBatch(_batchId, Time.GetUtcNow().UtcDateTime, LoginPage.DemoUserName,
            string.IsNullOrWhiteSpace(_boxBarcode) ? null : _boxBarcode.Trim(), _batchDepartment, _expectedFiles,
            uploaded.Select(i => i.JobId!).ToList(), items.Count(i => i.Status == UploadStatus.Failed));
        Batches.Save(_lastBatch);
        foreach (var item in uploaded)
            Audit.Record(LoginPage.DemoUserName, AuditAction.Uploaded, item.JobId, item.FileName, _batchId);

        _batchId = Batches.NewBatchId();
        _boxBarcode = string.Empty;
        _expectedFiles = null;
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

    [LoggerMessage(Level = LogLevel.Error, Message = "OCR UI: failed to load the scan history")]
    private static partial void LogUnexpectedHistory(ILogger logger, Exception exception);

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
