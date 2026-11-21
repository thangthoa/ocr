namespace OppOcr.Web.Features.Mockups.History;

/// <summary>
/// Builds the simulated history in the background as the app starts (about a second for a million documents), so
/// the first page that needs it does not wait.
/// </summary>
internal sealed partial class HistoryWarmup(ProjectHistory history, ILogger<HistoryWarmup> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (history.Enabled)
            _ = Task.Run(Warm, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Warm()
    {
        try
        {
            var started = TimeProvider.System.GetTimestamp();
            history.Warm();
            var milliseconds = TimeProvider.System.GetElapsedTime(started).TotalMilliseconds;
            LogBuilt(history.Count, milliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Not fatal: the pages build it on first use and report the failure there.
            LogFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulated history: {Documents} documents built in {Milliseconds:0} ms")]
    private partial void LogBuilt(int documents, double milliseconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Simulated history: building it in the background failed")]
    private partial void LogFailed(Exception exception);
}
