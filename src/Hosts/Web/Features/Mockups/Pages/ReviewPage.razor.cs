using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Documents waiting for review: OCR has finished and nobody has approved them yet. Real
/// documents come first, then the oldest. Reviewing itself happens on the document page.
/// </summary>
public sealed partial class ReviewPage : IDisposable
{
    /// <summary>Below this OCR confidence a document is highlighted (samples only: the service reports none).</summary>
    public const double LowConfidence = 80;

    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private DocumentList? _list;
    private IReadOnlyList<DocumentRecord> _queue = [];
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

    private static string DetailUrl(string id) => OcrEndpoints.JobUrl(id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Documents UI: failed to load the review queue")]
    private static partial void LogUnexpected(ILogger logger, Exception exception);
}
