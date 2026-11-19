using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Review round 2: documents the automatic check flagged (or that were not checked yet) wait here until
/// a person approves them. Real documents come first, then the oldest. Reviewing itself happens on the
/// document page.
/// </summary>
public sealed partial class ReviewPage : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private DocumentList? _list;
    private IReadOnlyList<DocumentRecord> _queue = [];
    private int _autoApproved;
    private string? _loadError;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

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
            _list = await Catalog.ListAsync(_disposed.Token);
            _queue = _list.Documents
                .Where(d => d.Status == DocumentStatus.NeedsReview)
                .OrderBy(d => d.IsSample)
                .ThenBy(d => d.UploadedAtUtc ?? DateTime.MaxValue)
                .ToList();
            _autoApproved = _list.Documents.Count(d => d.Approval?.By == DocumentStore.QualityCheckAuthor);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex);
            _loadError = OcrText["LoadFailed"];
        }
    }

    private string WaitingFor(DocumentRecord document)
    {
        if (document.UploadedAtUtc is not { } uploaded)
            return MockupTextExtensions.Missing;

        var waited = Time.GetUtcNow().UtcDateTime - uploaded;
        return waited.TotalDays >= 1
            ? Text["Review.Days", (int)waited.TotalDays]
            : Text["Review.Hours", Math.Max(0, (int)waited.TotalHours)];
    }

    /// <summary>Highlighted against the threshold the document was checked with.</summary>
    private static bool IsLow(DocumentRecord document) =>
        document.Quality?.Issues.Any(i => i.Kind == QualityIssueKind.LowDocumentConfidence) == true;

    private static string DetailUrl(string id) => OcrEndpoints.JobUrl(id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to load the review queue")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
