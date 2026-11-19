using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Approved documents: the final, reviewed documents, newest approval first, with who approved them.
/// Each can be downloaded (DOCX or text) and the filtered list exported as CSV.
/// </summary>
public sealed partial class ApprovedPage : IDisposable
{
    public const int PageSize = 10;

    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private DocumentList? _list;
    private IReadOnlyList<DocumentRecord> _results = [];
    private string _search = string.Empty;
    private DocumentSearchField _field = DocumentSearchField.All;
    private DocumentDepartment? _department;
    private DocumentKind? _kind;
    private DocumentConfidentiality? _confidentiality;
    private DateOnly? _approvedFrom;
    private DateOnly? _approvedTo;
    private int _page = 1;
    private string? _loadError;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<ApprovedPage> Logger { get; set; } = default!;

    private DocumentQuery Query => new(_search, _field, Department: _department, Kind: _kind,
        Confidentiality: _confidentiality, ApprovedFrom: _approvedFrom, ApprovedTo: _approvedTo);

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_results.Count / (double)PageSize));

    private IEnumerable<DocumentRecord> PageItems => _results.Skip((_page - 1) * PageSize).Take(PageSize);

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
            _list = await Catalog.ListAsync(_disposed.Token);
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
        _results = ApprovedDocumentFiles.Approved(_list?.Documents ?? [], Query);
        _page = keepPage ? Math.Min(_page, TotalPages) : 1;
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
