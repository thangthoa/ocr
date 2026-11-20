using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Mockups.Word;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// One document: an OCR job (images, text, progress, downloads, delete) or a sample, with review
/// status, metadata, edits saved as versions, and metadata links to search. Each page of the scanned
/// original is shown next to the same page of the Word file.
/// </summary>
public sealed partial class DocumentDetailPage : IDisposable
{
    private const int MaxThumbnails = 8;
    private const string TitleField = "title";

    // Mockup: no authentication yet, so every change is made by the demo account.
    private const string CurrentUser = LoginPage.DemoUserName;

    /// <summary>The OCR service guide suggests refreshing every 2–3 seconds while a job runs.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private static readonly char[] KeywordSeparators = [',', '،'];

    private readonly CancellationTokenSource _disposed = new();
    private readonly Dictionary<string, string> _errors = [];
    private IDisposable? _subscription;
    private DocumentRecord? _document;
    private DocumentVersion? _viewing;
    private IReadOnlyList<WordVersion> _wordVersions = [];
    private WordPreview? _wordPreview;
    private PreviewState _previewState;
    private string? _previewFor;
    private bool _showText;
    private EditModel _form = new();
    private bool _loaded;
    private bool _notFound;
    private bool _deleted;
    private bool _deleting;
    private bool _editing;
    private string? _error;
    private string? _deleteError;
    private string? _message;
    private bool _offerReviewQueue;
    private int _page = 1;
    private DetailTab _tab = DetailTab.Metadata;

    private enum DetailTab
    {
        Metadata,
        Versions,
    }

    private enum PreviewState
    {
        /// <summary>No Word file to show (editor not configured, or OCR not finished).</summary>
        None,
        Loading,
        Ready,
        Unavailable,
    }

    [Parameter]
    public string Id { get; set; } = default!;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private DocumentStore Store { get; set; } = default!;

    [Inject] private WordEditorService Word { get; set; } = default!;

    [Inject] private WordPreviewService Preview { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<DocumentDetailPage> Logger { get; set; } = default!;

    private DocumentMetadata ShownMetadata => _viewing?.Metadata ?? _document!.Metadata;

    private string ShownPageText => PageTextOf(_viewing?.PageTexts ?? _document!.PageTexts, _page);

    private static string BackArrow => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "→" : "←";

    /// <summary>Pages of the original or of the Word file, whichever has more (text can reflow).</summary>
    private int PageCount => Math.Max(_document!.Pages, _wordPreview?.PageCount ?? 0);

    private bool ShowsWord => _previewState == PreviewState.Ready && _viewing is null && !_showText;

    private string ResultPaneTitle => _editing ? Text["Edit.PageText", _page]
        : _viewing is not null ? Text["Preview.TextVersion", _viewing.Number]
        : ShowsWord || (_previewState == PreviewState.Loading && !_showText) ? Text["Preview.WordFile"]
        : Text["Preview.Text"];

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override void OnInitialized() => _subscription = Notifications.Subscribe(OnJobChangedAsync);

    protected override async Task OnParametersSetAsync()
    {
        _document = null;
        _viewing = null;
        _editing = false;
        _deleted = false;
        _notFound = false;
        _message = null;
        _offerReviewQueue = false;
        _errors.Clear();
        _page = 1;
        _tab = DetailTab.Metadata;
        _wordPreview = null;
        _previewState = PreviewState.None;
        _previewFor = null;
        _showText = false;
        await LoadAsync();
        _loaded = true;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            _ = RefreshWhileProcessingAsync();

        // Rendering a Word file takes seconds, so it starts once the page is interactive (never while
        // prerendering) and again when OCR finishes or another document is shown.
        if (_document is { } document && Preview.Enabled && PreviewKey(document) != _previewFor)
        {
            _previewFor = PreviewKey(document);
            _ = LoadWordPreviewAsync(document);
        }
    }

    private static string PreviewKey(DocumentRecord document) => $"{document.Id}|{document.Status}";

    private async Task LoadWordPreviewAsync(DocumentRecord document)
    {
        try
        {
            if (_wordPreview is null)
            {
                _previewState = PreviewState.Loading;
                await InvokeAsync(StateHasChanged);
            }

            var result = await Preview.GetAsync(document, _disposed.Token);
            var versions = result.IsSuccess ? await Word.VersionsAsync(document.Id, _disposed.Token) : _wordVersions;
            await InvokeAsync(() =>
            {
                if (_document?.Id != document.Id)
                    return;
                _wordPreview = result.IsSuccess ? result.Value : null;
                _previewState = result.IsSuccess ? PreviewState.Ready
                    : result.Error!.Code == WordErrors.NoWordFile ? PreviewState.None
                    : PreviewState.Unavailable;
                _wordVersions = versions;
                StateHasChanged();
            });
        }
        catch (OperationCanceledException)
        {
            // Page closed.
        }
        catch (Exception ex)
        {
            LogUnexpected(Logger, ex, "render the Word preview");
            await InvokeAsync(() =>
            {
                _previewState = PreviewState.Unavailable;
                StateHasChanged();
            });
        }
    }

    private void RetryPreview() => _previewFor = null;

    private void ShowPage(int page) => _page = Math.Clamp(page, 1, Math.Max(PageCount, 1));

    private bool IsFlagged(int page) => _document?.Quality?.FlaggedPages.Contains(page) == true;

    private bool IsLowPage(int page) =>
        _document?.Quality?.Issues.Any(i => i.Kind == QualityIssueKind.LowPageConfidence && i.Page == page) == true;

    // The job may have been submitted elsewhere, so the page polls rather than relying on
    // notifications from this instance's worker.
    private async Task RefreshWhileProcessingAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(_disposed.Token))
            {
                if (_deleted || _document?.Job is not { IsFinished: false })
                    continue;
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Page closed.
        }
    }

    private Task OnJobChangedAsync(OcrJobChange change)
    {
        if (change.JobId.Value != Id || change.Kind != OcrJobChangeKind.Deleted)
            return Task.CompletedTask;

        return InvokeAsync(() =>
        {
            _deleted = true;
            StateHasChanged();
        });
    }

    private async Task LoadAsync()
    {
        try
        {
            var result = await Catalog.GetAsync(Id, _disposed.Token);
            if (result.IsSuccess)
            {
                _document = result.Value;
                _wordVersions = await Word.VersionsAsync(_document.Id, _disposed.Token);
                _error = null;
                return;
            }

            _notFound = result.Error!.Type == ErrorType.NotFound;
            _error = OcrText.Localize(result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "load a document");
            _error = OcrText["LoadFailed"];
        }
    }

    /// <summary>Re-reads the stored part after a change, reusing the job already loaded (no service call).</summary>
    private void Refresh() =>
        _document = _document!.Job is { } job ? Store.FromJob(job) : Store.FindSample(Id) ?? _document;

    private void StartEdit()
    {
        _offerReviewQueue = false;
        _form = EditModel.From(_document!);
        _errors.Clear();
        _message = null;
        _viewing = null;
        _tab = DetailTab.Metadata;
        _editing = true;
    }

    private void CancelEdit()
    {
        _errors.Clear();
        _editing = false;
    }

    private void Save()
    {
        _errors.Clear();
        if (string.IsNullOrWhiteSpace(_form.Title))
        {
            _errors[TitleField] = Text["Edit.TitleRequired"];
            return;
        }

        Store.SaveEdit(Id, _form.PageTexts, _form.ToMetadata(), CurrentUser, _form.Note);
        Refresh();
        _editing = false;
        _message = Text["Detail.Saved", _document!.CurrentVersion!.Number];
    }

    private void ChangeStatus(DocumentStatus status)
    {
        var wasWaiting = _document!.Status == DocumentStatus.NeedsReview;
        Store.ChangeStatus(Id, status, CurrentUser);
        Refresh();
        _viewing = null;
        _message = Text["Detail.StatusChanged", Text.Status(status)];
        _offerReviewQueue = wasWaiting && status == DocumentStatus.Archived;
    }

    private void View(DocumentVersion version)
    {
        _viewing = version.Number == _document!.CurrentVersion?.Number ? null : version;
        _message = null;
    }

    private void ShowCurrent() => _viewing = null;

    private void Restore(DocumentVersion version)
    {
        var number = Store.Restore(Id, version.Number, CurrentUser);
        Refresh();
        _viewing = null;
        _message = Text["Detail.Restored", version.Number, number];
    }

    private async Task DeleteAsync()
    {
        _deleteError = null;
        _deleting = true;
        try
        {
            var result = await Catalog.DeleteAsync(Id, _document?.FileName, CurrentUser, _disposed.Token);
            if (result.IsSuccess)
                Navigation.NavigateTo("documents");
            else
                _deleteError = OcrText.Localize(result.Error!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "delete a document");
            _deleteError = OcrText["UnexpectedError"];
        }
        finally
        {
            _deleting = false;
        }
    }

    private string ChangeLabel(DocumentVersion version) => version.Change switch
    {
        DocumentVersionChange.OcrResult => Text["Change.OcrResult"],
        DocumentVersionChange.Restored => Text["Change.Restored", version.RestoredFrom ?? "?"],
        DocumentVersionChange.EditedInWord => Text["Change.EditedInWord", version.WordVersion ?? 0],
        _ => Text["Change.Edited"],
    };

    private string WordSource(WordVersion version) => Text[$"Word.Source.{version.Source}"];

    private static string PageTextOf(IReadOnlyList<string> pageTexts, int pageNumber) =>
        pageNumber >= 1 && pageNumber <= pageTexts.Count ? pageTexts[pageNumber - 1] : string.Empty;

    private static string SearchUrl(DocumentSearchField field, string value) =>
        $"documents?field={field}&q={Uri.EscapeDataString(value)}";

    /// <summary>Uneven line lengths so the placeholder page reads as text, not as a bar chart.</summary>
    private static string LineWidth(int line) => (line % 3) switch
    {
        0 => "92%",
        1 => "78%",
        _ => "64%",
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);

    private sealed class EditModel
    {
        private readonly List<string> _pageTexts = [];

        public IReadOnlyList<string> PageTexts => _pageTexts;

        public string Title { get; set; } = string.Empty;

        public string Reference { get; set; } = string.Empty;

        public DateOnly? DocumentDate { get; set; }

        public DocumentDepartment? Department { get; set; }

        public DocumentKind? Kind { get; set; }

        public DocumentConfidentiality Confidentiality { get; set; }

        public string Keywords { get; set; } = string.Empty;

        public string? Note { get; set; }

        public string PageText(int pageNumber) => PageTextOf(_pageTexts, pageNumber);

        /// <summary>Pages without text yet are padded so any page can be edited.</summary>
        public void SetPageText(int pageNumber, string? text)
        {
            while (_pageTexts.Count < pageNumber)
                _pageTexts.Add(string.Empty);
            _pageTexts[pageNumber - 1] = text ?? string.Empty;
        }

        public static EditModel From(DocumentRecord document)
        {
            var model = new EditModel
            {
                Title = document.Metadata.Title,
                Reference = document.Metadata.Reference,
                DocumentDate = document.Metadata.DocumentDate,
                Department = document.Metadata.Department,
                Kind = document.Metadata.Kind,
                Confidentiality = document.Metadata.Confidentiality,
                Keywords = string.Join(", ", document.Metadata.Keywords),
            };
            model._pageTexts.AddRange(document.PageTexts);
            return model;
        }

        public DocumentMetadata ToMetadata() => new(
            Title.Trim(),
            Reference.Trim(),
            DocumentDate,
            Department,
            Kind,
            Confidentiality,
            Keywords.Split(KeywordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
