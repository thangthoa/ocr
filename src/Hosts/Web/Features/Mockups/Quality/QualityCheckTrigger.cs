using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Quality;

/// <summary>
/// Runs the automatic check as soon as this instance sees one of its OCR jobs finish, so the document is
/// approved or flagged before anyone opens it. Jobs finished elsewhere are checked when first opened.
/// </summary>
/// <remarks>
/// It subscribes at start-up, before any page, and notifications reach subscribers in order: pages that
/// reload on the same notification already see the result.
/// </remarks>
internal sealed partial class QualityCheckTrigger(
    OcrJobNotifications notifications,
    IServiceScopeFactory scopeFactory,
    ILogger<QualityCheckTrigger> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private IDisposable? _subscription;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscription = notifications.Subscribe(OnJobChangedAsync);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _subscription?.Dispose();
        await _stopping.CancelAsync();
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _stopping.Dispose();
    }

    internal async Task OnJobChangedAsync(OcrJobChange change)
    {
        if (change.Kind != OcrJobChangeKind.Updated || change.Status != OcrJobStatus.Done)
            return;

        try
        {
            // Reading the job with its pages creates the document, which runs the check (DocumentStore.FromJob).
            await using var scope = scopeFactory.CreateAsyncScope();
            var catalog = scope.ServiceProvider.GetRequiredService<DocumentCatalog>();
            var result = await catalog.GetAsync(change.JobId.Value, _stopping.Token);
            if (result.IsFailure)
                LogNotChecked(change.JobId.Value, result.Error!.Code);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, change.JobId.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quality check: job {JobId} could not be read ({ErrorCode}); it is checked when opened")]
    private partial void LogNotChecked(string jobId, string errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Quality check: failed for job {JobId}; it is checked when opened")]
    private partial void LogFailed(Exception exception, string jobId);
}
