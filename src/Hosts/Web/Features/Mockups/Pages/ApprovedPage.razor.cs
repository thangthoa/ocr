using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Document Archive: the final, reviewed documents (the simulated history's included), newest approval first, with
/// who approved them, a page at a time.
/// Each can be downloaded as DOCX, PDF (when the document server is configured) or text. The CSV export
/// of the list stays available at its address (<see cref="ApprovedDocumentEndpoints.ExportUrl"/>) but has
/// no button.
/// </summary>
public sealed partial class ApprovedPage : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private readonly TablePaging _paging = new();
    private IDisposable? _subscription;
    private DocumentSnapshot? _snapshot;
    private DocumentPage _results = new([], 0);
    private string _search = string.Empty;
    private DocumentSearchField _field = DocumentSearchField.All;
    private DocumentDepartment? _department;
    private DocumentKind? _kind;
    private DocumentConfidentiality? _confidentiality;
    private DateOnly? _approvedFrom;
    private DateOnly? _approvedTo;
    private string? _loadError;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private ApprovedDocumentFiles Files { get; set; } = default!;

    [Inject] private ProjectHistory History { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<ApprovedPage> Logger { get; set; } = default!;

    private DocumentQuery Query => new(_search, _field, Department: _department, Kind: _kind,
        Confidentiality: _confidentiality, ApprovedFrom: _approvedFrom, ApprovedTo: _approvedTo);

    private bool HasFilters => !string.IsNullOrWhiteSpace(_search) || _field != DocumentSearchField.All || _department is not null
        || _kind is not null || _confidentiality is not null || _approvedFrom is not null || _approvedTo is not null;

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        _subscription = Notifications.Subscribe(OnJobChangedAsync);
        await LoadAsync();
    }

    private Task OnJobChangedAsync(OcrJobChange change) =>
        InvokeAsync(async () =>
        {
            await LoadAsync();
            StateHasChanged();
        });

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

    private void Apply(bool keepPage = false)
    {
        if (!keepPage)
            _paging.Reset();
        ShowPage();
    }

    /// <summary>Reads the current page; a list that got shorter moves back to its last page.</summary>
    private void ShowPage()
    {
        if (_snapshot is null)
            return;
        var query = Query with { Status = DocumentStatus.Archived };
        _results = Catalog.Query(_snapshot, query, DocumentOrder.NewestApproval, (_paging.Page - 1) * _paging.Size, _paging.Size);
        if (_paging.Page > _paging.PageCount(_results.Total))
        {
            _paging.Clamp(_results.Total);
            _results = Catalog.Query(_snapshot, query, DocumentOrder.NewestApproval, (_paging.Page - 1) * _paging.Size, _paging.Size);
        }
    }

    private void ClearFilters()
    {
        _search = string.Empty;
        _field = DocumentSearchField.All;
        _department = null;
        _kind = null;
        _confidentiality = null;
        _approvedFrom = null;
        _approvedTo = null;
        Apply();
    }

    private static string DetailUrl(string id) => OcrEndpoints.JobUrl(id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Approved documents UI: failed to load documents")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
