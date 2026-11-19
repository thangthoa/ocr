using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.GetOcrJob;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Ocr.Pages;

public sealed partial class OcrJobPage : IDisposable
{
    /// <summary>The service guide suggests refreshing every 2–3 seconds while a job runs.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private OcrJob? _job;
    private bool _notFound;
    private bool _deleted;
    private bool _deleting;
    private string? _error;
    private string? _deleteError;

    [Parameter] public string JobId { get; set; } = default!;

    [Inject] private GetOcrJobHandler GetHandler { get; set; } = default!;

    [Inject] private DeleteOcrJobHandler DeleteHandler { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> Text { get; set; } = default!;

    [Inject] private ILogger<OcrJobPage> Logger { get; set; } = default!;

    public void Dispose()
    {
        _subscription?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override void OnInitialized() => _subscription = Notifications.Subscribe(OnJobChangedAsync);

    protected override async Task OnParametersSetAsync() => await LoadAsync();

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
            _ = RefreshWhileRunningAsync();
    }

    // The job may have been submitted elsewhere, so the page refreshes itself rather than
    // relying on notifications from this instance's worker.
    private async Task RefreshWhileRunningAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(RefreshInterval);
            while (await timer.WaitForNextTickAsync(_disposed.Token))
            {
                if (_deleted || _notFound || _job?.IsFinished == true)
                    continue;
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Page closed.
        }
    }

    private Task OnJobChangedAsync(OcrJobChange change)
    {
        if (change.JobId.Value != JobId || change.Kind != OcrJobChangeKind.Deleted)
            return Task.CompletedTask;

        return InvokeAsync(() =>
        {
            _deleted = true;
            StateHasChanged();
        });
    }

    private async Task LoadAsync()
    {
        try
        {
            var result = await GetHandler.HandleAsync(new GetOcrJobQuery(JobId), _disposed.Token);
            if (result.IsSuccess)
            {
                _job = result.Value;
                _error = null;
                return;
            }

            _notFound = result.Error!.Type == ErrorType.NotFound;
            _error = Text.Localize(result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "load job");
            _error = Text["LoadFailed"];
        }
    }

    private async Task DeleteAsync()
    {
        _deleteError = null;
        _deleting = true;
        try
        {
            var result = await DeleteHandler.HandleAsync(new DeleteOcrJobCommand(JobId), _disposed.Token);
            if (result.IsSuccess)
                Navigation.NavigateTo("ocr/jobs");
            else
                _deleteError = Text.Localize(result.Error!);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "delete job");
            _deleteError = Text["UnexpectedError"];
        }
        finally
        {
            _deleting = false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "OCR UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);
}
