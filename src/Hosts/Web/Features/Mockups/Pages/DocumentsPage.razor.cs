using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Documents: the newest OCR jobs plus the samples, filtered in memory. Filters can be pre-set from
/// the query string; the list reloads when an OCR job changes.
/// </summary>
public sealed partial class DocumentsPage : IDisposable
{
    public const int PageSize = 10;

    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private DocumentList? _list;
    private IReadOnlyList<DocumentRecord> _results = [];
    private IReadOnlyDictionary<DocumentStatus, int> _counts = new Dictionary<DocumentStatus, int>();
    private string _search = string.Empty;
    private DocumentSearchField _field = DocumentSearchField.All;
    private DocumentStatus? _status;
    private DocumentDepartment? _department;
    private DocumentKind? _kind;
    private DateOnly? _from;
    private DateOnly? _to;
    private int _page = 1;
    private string? _loadError;
    private string? _deletingId;
    private string? _deleteError;

    [SupplyParameterFromQuery(Name = "q")]
    public string? QueryText { get; set; }

    [SupplyParameterFromQuery(Name = "field")]
    public string? QueryField { get; set; }

    [SupplyParameterFromQuery(Name = "status")]
    public string? QueryStatus { get; set; }

    [SupplyParameterFromQuery(Name = "department")]
    public string? QueryDepartment { get; set; }

    [SupplyParameterFromQuery(Name = "kind")]
    public string? QueryKind { get; set; }

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private ILogger<DocumentsPage> Logger { get; set; } = default!;

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(_results.Count / (double)PageSize));

    private IEnumerable<DocumentRecord> PageItems => _results.Skip((_page - 1) * PageSize).Take(PageSize);

    private bool HasFilters => !string.IsNullOrWhiteSpace(_search) || _field != DocumentSearchField.All || _status is not null
        || _department is not null || _kind is not null || _from is not null || _to is not null;

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override void OnInitialized() => _subscription = Notifications.Subscribe(OnJobChangedAsync);

    protected override async Task OnParametersSetAsync()
    {
        // Links from the detail page and the dashboard (e.g. ?field=Reference&q=2019/413) arrive here.
        _search = QueryText ?? string.Empty;
        _field = Parse<DocumentSearchField>(QueryField) ?? DocumentSearchField.All;
        _status = Parse<DocumentStatus>(QueryStatus);
        _department = Parse<DocumentDepartment>(QueryDepartment);
        _kind = Parse<DocumentKind>(QueryKind);
        _from = null;
        _to = null;
        _page = 1;
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
            LogUnexpected(Logger, ex, "load documents");
            _loadError = OcrText["LoadFailed"];
        }

        Apply(keepPage: true);
    }

    private void Apply(bool keepPage = false)
    {
        var documents = _list?.Documents ?? [];
        _counts = documents.GroupBy(d => d.Status).ToDictionary(g => g.Key, g => g.Count());
        _results = DocumentStore.Filter(documents, new DocumentQuery(_search, _field, _status, _department, _kind, _from, _to));
        _page = keepPage ? Math.Min(_page, TotalPages) : 1;
    }

    /// <summary>Every status, so the legend keeps its place when a status has no documents.</summary>
    private List<KeyValuePair<DocumentStatus, int>> StatusCounts =>
        Enum.GetValues<DocumentStatus>().Select(s => KeyValuePair.Create(s, _counts.GetValueOrDefault(s))).ToList();

    private void SetStatus(DocumentStatus? status)
    {
        _status = status;
        Apply();
    }

    private void ClearFilters()
    {
        _search = string.Empty;
        _field = DocumentSearchField.All;
        _status = null;
        _department = null;
        _kind = null;
        _from = null;
        _to = null;
        Apply();
    }

    private async Task DeleteAsync(string id, string fileName)
    {
        _deleteError = null;
        _deletingId = id;
        try
        {
            var result = await Catalog.DeleteAsync(id, fileName, LoginPage.DemoUserName, _disposed.Token);
            if (result.IsFailure)
                _deleteError = OcrText.Localize(result.Error!);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "delete a document");
            _deleteError = OcrText["UnexpectedError"];
        }
        finally
        {
            _deletingId = null;
        }
    }

    private void Open(string id) => Navigation.NavigateTo(DetailUrl(id));

    private static string DetailUrl(string id) => OcrEndpoints.JobUrl(id);

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);
}
