using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Application.Features.UploadOcrFile;
using Modules.Ocr.Application.Features.UploadOcrPages;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.Features.Ocr.Pages;

/// <summary>
/// Capture: the user picks the document type, one image (or PDF) or several images, and a file
/// name, then uploads. Several images become one PDF with a page per image (one OCR job). Below,
/// the documents uploaded since the page was opened are followed until the OCR service is done.
/// </summary>
public sealed partial class OcrUploadPage : IDisposable
{
    /// <summary>Upper bound on entries read from a chosen folder, before unsupported files are skipped.</summary>
    public const int MaxFolderEntries = 1000;

    private static readonly string AnyFormat = string.Join(',', OcrFileFormats.SupportedExtensions);
    private static readonly string PageFormat = string.Join(',', OcrFileFormats.PageExtensions);

    // Scanners and phones number their files: page-2 must come before page-10.
    private static readonly StringComparer PageOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    private readonly CancellationTokenSource _disposed = new();
    private readonly List<IBrowserFile> _files = [];

    // Newest first. Only what was uploaded on this page: older documents are in Documents.
    private readonly List<UploadedDocument> _uploaded = [];
    private OcrDocumentType _documentType = OcrDocumentType.Printed;
    private CaptureMode _mode = CaptureMode.One;
    private string _fileName = string.Empty;
    private bool _nameRequired;
    private bool _uploading;
    private string? _error;
    private string? _note;
    private SentDocument? _sent;
    private IDisposable? _subscription;

    private enum CaptureMode
    {
        One,
        Multi,
    }

    [Inject] private UploadOcrFileHandler UploadFileHandler { get; set; } = default!;

    [Inject] private UploadOcrPagesHandler UploadPagesHandler { get; set; } = default!;

    [Inject] private GetOcrJobHandler GetJobHandler { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private CaptureBatchStore Batches { get; set; } = default!;

    [Inject] private AuditLog Audit { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> Text { get; set; } = default!;

    [Inject] private ILogger<OcrUploadPage> Logger { get; set; } = default!;

    private string Accept => _mode == CaptureMode.One ? AnyFormat : PageFormat;

    private string DropZoneHint => _mode == CaptureMode.One
        ? Text["DropZoneHint", OcrTextExtensions.SupportedFormatsLabel, OcrRules.MaxSizeBytes / 1024 / 1024]
        : Text["DropZoneHint.Multi", OcrTextExtensions.PageFormatsLabel, OcrRules.MaxPagesPerDocument, OcrRules.MaxSizeBytes / 1024 / 1024];

    private bool ShowNameError => _nameRequired && string.IsNullOrWhiteSpace(_fileName);

    /// <summary>The name the document will get on the OCR service, once a name is typed and files are chosen.</summary>
    private string? SavedName
    {
        get
        {
            var name = _fileName.Trim();
            if (name.Length == 0 || _files.Count == 0)
                return null;
            if (_files.Count > 1)
                return OcrFileFormats.WithExtension(name, OcrFileFormat.Pdf);
            return OcrFileFormats.FromFileName(_files[0].Name) is { } format ? OcrFileFormats.WithExtension(name, format) : null;
        }
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override void OnInitialized() => _subscription = Notifications.Subscribe(OnJobChangedAsync);

    /// <summary>
    /// The notification only says that a job changed; its progress is read again from the service.
    /// If that fails, the status from the notification is still shown.
    /// </summary>
    private Task OnJobChangedAsync(OcrJobChange change) =>
        InvokeAsync(async () =>
        {
            var index = _uploaded.FindIndex(u => u.Job.Id == change.JobId);
            if (index < 0)
                return;

            if (change.Kind == OcrJobChangeKind.Deleted)
            {
                _uploaded.RemoveAt(index);
                StateHasChanged();
                return;
            }

            var latest = _uploaded[index].Job with { Status = change.Status, ErrorCode = change.ErrorCode };
            try
            {
                var result = await GetJobHandler.HandleAsync(new GetOcrJobQuery(change.JobId.Value), _disposed.Token);
                if (result.IsSuccess)
                    latest = result.Value;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogUnexpectedRefresh(Logger, ex);
            }

            // The list may have changed while the service was asked.
            index = _uploaded.FindIndex(u => u.Job.Id == change.JobId);
            if (index >= 0)
                _uploaded[index] = _uploaded[index] with { Job = latest };
            StateHasChanged();
        });

    /// <summary>The chosen files belong to one mode: switching mode starts the selection again.</summary>
    private void SetMode(CaptureMode mode)
    {
        if (_mode == mode)
            return;

        _mode = mode;
        ClearSelection();
    }

    private void ClearSelection()
    {
        _files.Clear();
        _error = null;
        _note = null;
    }

    /// <summary>
    /// Files are only listed here; they are read when the user presses Upload. A new selection
    /// replaces the list, because the browser forgets the previous files of the same input.
    /// </summary>
    private void SelectFiles(InputFileChangeEventArgs e)
    {
        ClearSelection();
        _sent = null;
        if (_mode == CaptureMode.One)
        {
            if (e.FileCount > 1)
            {
                _error = Text["OneFileOnly"];
                return;
            }

            if (OcrFileFormats.FromFileName(e.File.Name) is null)
            {
                _error = Text[OcrErrorCodes.UnsupportedFormat, OcrTextExtensions.SupportedFormatsLabel];
                return;
            }

            _files.Add(e.File);
            return;
        }

        if (e.FileCount > OcrRules.MaxPagesPerDocument)
        {
            _error = Text[OcrErrorCodes.TooManyPages, OcrRules.MaxPagesPerDocument];
            return;
        }

        var files = e.GetMultipleFiles(OcrRules.MaxPagesPerDocument);
        if (!files.All(IsPageImage))
        {
            _error = Text[OcrErrorCodes.UnsupportedPageFormat, OcrTextExtensions.PageFormatsLabel];
            return;
        }

        _files.AddRange(files.OrderBy(f => f.Name, PageOrder));
    }

    /// <summary>
    /// A folder often holds files that cannot be pages (thumbnails, notes, PDFs); those are skipped
    /// up front. The images left become the pages, in file name order.
    /// </summary>
    private void SelectFolder(InputFileChangeEventArgs e)
    {
        ClearSelection();
        _sent = null;
        if (e.FileCount > MaxFolderEntries)
        {
            _error = Text["FolderTooLarge", MaxFolderEntries];
            return;
        }

        var all = e.GetMultipleFiles(MaxFolderEntries);
        var images = all.Where(IsPageImage).ToList();
        if (images.Count == 0)
        {
            _error = Text["FolderEmpty", OcrTextExtensions.PageFormatsLabel];
            return;
        }

        if (images.Count > OcrRules.MaxPagesPerDocument)
        {
            _error = Text["FolderTooManyFiles", images.Count, OcrRules.MaxPagesPerDocument];
            return;
        }

        var skipped = all.Count - images.Count;
        if (skipped > 0)
            _note = Text["FolderSkipped", skipped];

        _files.AddRange(images.OrderBy(f => f.Name, PageOrder));
    }

    private void Move(int index, int offset)
    {
        var target = index + offset;
        if (target < 0 || target >= _files.Count)
            return;

        (_files[index], _files[target]) = (_files[target], _files[index]);
    }

    private void Remove(int index) => _files.RemoveAt(index);

    private async Task UploadAsync()
    {
        _error = null;
        _sent = null;
        _nameRequired = true;
        if (string.IsNullOrWhiteSpace(_fileName) || _files.Count == 0)
            return;

        _uploading = true;
        var files = _files.ToList();
        var name = _fileName.Trim();
        try
        {
            var result = files.Count == 1
                ? await UploadOneAsync(files[0], name)
                : await UploadPagesAsync(files, name);
            if (result.IsSuccess)
            {
                Record(result.Value);
                _uploaded.Insert(0, new UploadedDocument(result.Value, Time.GetUtcNow().UtcDateTime));
                _sent = new SentDocument(result.Value.Id.Value, result.Value.FileName, files.Count);
                _files.Clear();
                _note = null;
                _fileName = string.Empty;
                _nameRequired = false;
            }
            else
            {
                _error = Text.Localize(result.Error!);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
            _error = Text["UnexpectedError"];
        }
        finally
        {
            _uploading = false;
        }
    }

    /// <summary>One file is sent as it is (an image or a PDF), named as typed plus its own extension.</summary>
    private async Task<Result<OcrJob>> UploadOneAsync(IBrowserFile file, string name)
    {
        if (OcrFileFormats.FromFileName(file.Name) is not { } format)
            return Error.Validation(OcrErrorCodes.UnsupportedFormat, "Unsupported file type.");

        await using var stream = file.OpenReadStream(maxAllowedSize: Math.Max(file.Size, 1), _disposed.Token);
        return await UploadFileHandler.HandleAsync(
            new UploadOcrFileCommand(OcrFileFormats.WithExtension(name, format), file.Size, stream, _documentType), _disposed.Token);
    }

    private async Task<Result<OcrJob>> UploadPagesAsync(List<IBrowserFile> files, string name)
    {
        var streams = files.Select(f => f.OpenReadStream(maxAllowedSize: Math.Max(f.Size, 1), _disposed.Token)).ToList();
        try
        {
            var pages = files.Zip(streams, (file, stream) => new OcrPageFile(file.Name, file.Size, stream)).ToList();
            return await UploadPagesHandler.HandleAsync(new UploadOcrPagesCommand(name, pages, _documentType), _disposed.Token);
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    }

    /// <summary>Keeps who uploaded the document (for Documents) and writes the audit entry.</summary>
    private void Record(OcrJob job)
    {
        var captureId = Batches.NewBatchId();
        Batches.Save(new CaptureBatch(captureId, Time.GetUtcNow().UtcDateTime, LoginPage.DemoUserName, [job.Id.Value]));
        Audit.Record(LoginPage.DemoUserName, AuditAction.Uploaded, job.Id.Value, job.FileName, captureId);
    }

    private static bool IsPageImage(IBrowserFile file) =>
        OcrFileFormats.FromFileName(file.Name) is { } format && OcrFileFormats.PageFormats.Contains(format);

    [LoggerMessage(Level = LogLevel.Error, Message = "OCR UI: failed to upload a document")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OCR UI: failed to refresh an uploaded document")]
    private static partial void LogUnexpectedRefresh(ILogger logger, Exception exception);

    private sealed record SentDocument(string JobId, string FileName, int Pages);

    private sealed record UploadedDocument(OcrJob Job, DateTime UploadedAtUtc);
}
