using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Review round 2: documents the automatic check flagged (or that were not checked yet) wait here until
/// a person approves them, the simulated history's included. Real documents come first, then the oldest. The queue
/// can be filtered like All Documents and is shown a page at a time. Reviewing itself happens on the document page.
/// </summary>
public sealed partial class ReviewPage : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private readonly DocumentFilterState _filter = new();
    private readonly TablePaging _paging = new();
    private IDisposable? _subscription;
    private DocumentSnapshot? _snapshot;
    private DocumentPage _results = new([], 0);
    private string? _loadError;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<ReviewPage> Logger { get; set; } = default!;

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        _subscription = Notifications.Subscribe(_ => InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        }));
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loadError = null;
        try
        {
            _snapshot = await Catalog.SnapshotAsync(_disposed.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
            _loadError = OcrText["LoadFailed"];
        }

        Apply(keepPage: true);
    }

    /// <summary>The whole queue now (every document waiting, whatever the filters).</summary>
    private int QueueCount => _snapshot?.Facts.WaitingReview ?? 0;

    /// <summary>Waiting because the automatic check flagged them (the others were not checked yet).</summary>
    private int FlaggedCount => _snapshot?.Facts.WaitingFlagged ?? 0;

    /// <summary>Documents the automatic check approved without a person.</summary>
    private int AutoApprovedCount => _snapshot?.Facts.AllTime.AutoArchived ?? 0;

    private void Apply(bool keepPage = false)
    {
        if (!keepPage)
            _paging.Reset();
        ShowPage();
    }

    /// <summary>Reads the current page of the filtered queue, oldest first; a queue that got shorter moves back to its last page.</summary>
    private void ShowPage()
    {
        if (_snapshot is null)
            return;
        var query = _filter.ToQuery(DocumentStatus.NeedsReview);
        _results = Catalog.Query(_snapshot, query, DocumentOrder.OldestUpload, (_paging.Page - 1) * _paging.Size, _paging.Size);
        if (_paging.Page > _paging.PageCount(_results.Total))
        {
            _paging.Clamp(_results.Total);
            _results = Catalog.Query(_snapshot, query, DocumentOrder.OldestUpload, (_paging.Page - 1) * _paging.Size, _paging.Size);
        }
    }

    private void ClearFilters()
    {
        _filter.Clear();
        Apply();
    }

    /// <summary>Highlighted against the threshold the document was checked with.</summary>
    private static bool IsLow(DocumentRecord document) =>
        document.Quality?.Issues.Any(i => i.Kind == QualityIssueKind.LowDocumentConfidence) == true;

    private static string DetailUrl(string id) => OcrEndpoints.JobUrl(id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to load the review queue")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
