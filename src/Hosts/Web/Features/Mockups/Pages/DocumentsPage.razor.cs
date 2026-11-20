using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Documents: the newest OCR jobs plus the samples, filtered in memory. Filters can be pre-set from
/// the query string; the list reloads when an OCR job changes.
/// </summary>
public sealed partial class DocumentsPage : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private readonly DocumentFilterState _filter = new();
    private readonly TablePaging _paging = new();
    private IDisposable? _subscription;
    private DocumentList? _list;
    private IReadOnlyList<DocumentRecord> _results = [];
    private IReadOnlyDictionary<DocumentStatus, int> _counts = new Dictionary<DocumentStatus, int>();
    private DocumentStatus? _status;
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

    private bool HasFilters => _filter.HasValues || _status is not null;

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
        _filter.Clear();
        _filter.Search = QueryText ?? string.Empty;
        _filter.Field = Parse<DocumentSearchField>(QueryField) ?? DocumentSearchField.All;
        _filter.Department = Parse<DocumentDepartment>(QueryDepartment);
        _filter.Kind = Parse<DocumentKind>(QueryKind);
        _status = Parse<DocumentStatus>(QueryStatus);
        _paging.Reset();
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
        _results = DocumentStore.Filter(documents, _filter.ToQuery(_status));
        if (keepPage)
            _paging.Clamp(_results.Count);
        else
            _paging.Reset();
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
        _filter.Clear();
        _status = null;
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
