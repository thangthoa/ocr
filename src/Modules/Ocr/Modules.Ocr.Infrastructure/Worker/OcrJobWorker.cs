using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Features.SyncTrackedOcrJobs;
using Modules.Ocr.Infrastructure.Api;

namespace Modules.Ocr.Infrastructure.Worker;

/// <summary>Polls the OCR service for jobs submitted through this instance and raises notifications.</summary>
internal sealed partial class OcrJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<OcrWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<OcrJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollIntervalSeconds), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RunOnceAsync(stoppingToken);
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SyncTrackedOcrJobsHandler>().HandleAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the loop alive: a service hiccup must not stop future refreshes.
            LogTickFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Ocr worker: refresh round failed")]
    private partial void LogTickFailed(Exception exception);
}
