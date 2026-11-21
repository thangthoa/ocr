using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.UploadOcrPages;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Components;

/// <summary>A document after its pages changed, and the page to show next.</summary>
public sealed record DocumentPagesUpdate(DocumentRecord Document, int Page);

/// <summary>
/// Inserts, replaces and deletes pages of a document's OCR job, and shows the pages the OCR service thinks are the
/// same page with a button to delete the later one (API doc §3.8, §4.4). The service reads only new pages; the text
/// and the Word file follow once it is done.
/// </summary>
public sealed partial class DocumentPagesPanel : IDisposable
{
    private static readonly string AnyFormat = string.Join(',', OcrFileFormats.SupportedExtensions);

    // Scanners and phones number their files: page-2 must come before page-10.
    private static readonly StringComparer FileOrder =
        StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

    // Mockup: no authentication yet, so every change is made by the demo account.
    private const string CurrentUser = LoginPage.DemoUserName;

    private readonly CancellationTokenSource _disposed = new();
    private readonly List<IBrowserFile> _files = [];
    private PageForm? _form;

    /// <summary>The page the new pages become (insert), or the page replaced.</summary>
    private int _target;
    private bool _busy;
    private string? _error;
    private string? _message;

    private enum PageForm
    {
        Insert,
        Replace,
    }

    /// <summary>A document with an OCR job.</summary>
    [Parameter, EditorRequired] public DocumentRecord Document { get; set; } = default!;

    /// <summary>The page shown, which replace and delete act on.</summary>
    [Parameter, EditorRequired] public int Page { get; set; }

    [Parameter] public EventCallback<int> OnShowPage { get; set; }

    [Parameter] public EventCallback<DocumentPagesUpdate> OnChanged { get; set; }

    [Inject] private DocumentPageEditor Editor { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<DocumentPagesPanel> Logger { get; set; } = default!;

    private OcrJob Job => Document.Job!;

    private string Limits => Text["Pages.Limits", OcrTextExtensions.SupportedFormatsLabel, OcrRules.MaxSizeBytes / 1024 / 1024, OcrRules.MaxPagesPerDocument];

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override void OnParametersSet()
    {
        if (Document.Job is null)
            throw new InvalidOperationException($"{nameof(DocumentPagesPanel)} needs a document with an OCR job.");

        // The form was for the page shown, or for pages the job no longer has.
        if (_form == PageForm.Replace && _target != Page && !_busy)
            Close();
    }

    private void Open(PageForm form)
    {
        _form = form;
        _files.Clear();
        _error = null;
        _message = null;
        _target = form == PageForm.Insert ? Job.PagesTotal + 1 : Page;
    }

    private void Close()
    {
        _form = null;
        _files.Clear();
    }

    /// <summary>Files are only listed here; they are read when the user confirms.</summary>
    private void SelectFiles(InputFileChangeEventArgs e)
    {
        _files.Clear();
        _error = null;
        var max = _form == PageForm.Insert ? OcrRules.MaxPagesPerDocument : 1;
        if (e.FileCount > max)
        {
            _error = _form == PageForm.Insert ? OcrText[OcrErrorCodes.TooManyPages, OcrRules.MaxPagesPerDocument] : Text["Pages.OneFile"];
            return;
        }

        var files = e.GetMultipleFiles(max);
        if (files.Any(f => OcrFileFormats.FromFileName(f.Name) is null))
        {
            _error = OcrText[OcrErrorCodes.UnsupportedFormat, OcrTextExtensions.SupportedFormatsLabel];
            return;
        }

        _files.AddRange(files.OrderBy(f => f.Name, FileOrder));
    }

    private Task SubmitAsync() => _form == PageForm.Insert ? InsertAsync() : ReplaceAsync();

    private Task InsertAsync() => RunAsync(async token =>
    {
        // Opened one at a time as they are read: opening them all at once stalls the upload (Interactive Server).
        var streams = _files.Select(f => new DeferredBrowserFileStream(f, token)).ToList();
        try
        {
            var files = _files.Zip(streams, (file, stream) => new OcrPageFile(file.Name, file.Size, stream)).ToList();
            var position = _target;
            var result = await Editor.InsertAsync(Document.Id, files, position, CurrentUser, token);
            return result.IsSuccess
                ? Changed(result.Value, position, Text["Pages.Inserted", files.Count, position])
                : result.Error!;
        }
        finally
        {
            foreach (var stream in streams)
                await stream.DisposeAsync();
        }
    });

    private Task ReplaceAsync() => RunAsync(async token =>
    {
        var file = _files[0];
        var page = _target;
        await using var stream = new DeferredBrowserFileStream(file, token);
        var result = await Editor.ReplaceAsync(Document.Id, page, new OcrPageFile(file.Name, file.Size, stream), CurrentUser, token);
        return result.IsSuccess ? Changed(result.Value, page, Text["Pages.Replaced", page]) : result.Error!;
    });

    private Task DeleteAsync(int page) => RunAsync(async token =>
    {
        var result = await Editor.DeleteAsync(Document.Id, page, CurrentUser, token);
        return result.IsSuccess
            ? Changed(result.Value, Math.Min(page, Math.Max(result.Value.Pages, 1)), Text["Pages.Deleted", page])
            : result.Error!;
    });

    private DocumentPagesUpdate Changed(DocumentRecord document, int page, string message)
    {
        _message = message;
        Close();
        return new DocumentPagesUpdate(document, page);
    }

    private async Task RunAsync(Func<CancellationToken, Task<Result<DocumentPagesUpdate>>> change)
    {
        _busy = true;
        _error = null;
        _message = null;
        try
        {
            var result = await change(_disposed.Token);
            if (result.IsSuccess)
                await OnChanged.InvokeAsync(result.Value);
            else
                _error = OcrText.Localize(result.Error!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
            _error = OcrText["UnexpectedError"];
        }
        finally
        {
            _busy = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to change the pages of a document")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
