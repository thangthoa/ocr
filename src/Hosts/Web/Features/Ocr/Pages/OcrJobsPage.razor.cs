using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrJob;
using Modules.Ocr.Application.Features.ListOcrJobs;
using Modules.Ocr.Domain.Jobs;

namespace OppOcr.Web.Features.Ocr.Pages;

public sealed partial class OcrJobsPage : IDisposable
{
    private readonly CancellationTokenSource _disposed = new();
    private IDisposable? _subscription;
    private OcrJobList? _list;
    private OcrJobStatus? _status;
    private int _page = 1;
    private string? _deletingId;
    private string? _error;
    private string? _deleteError;
    private string? _search;
    private CancellationTokenSource? _searchDebounce;

    [Inject] private ListOcrJobsHandler ListHandler { get; set; } = default!;

    [Inject] private DeleteOcrJobHandler DeleteHandler { get; set; } = default!;

    [Inject] private OcrJobNotifications Notifications { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> Text { get; set; } = default!;

    [Inject] private ILogger<OcrJobsPage> Logger { get; set; } = default!;

    /// <summary>Typing pauses this long before searching, so each keystroke does not hit the service.</summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    public void Dispose()
    {
        _subscription?.Dispose();
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnInitializedAsync()
    {
        _subscription = Notifications.Subscribe(OnJobChangedAsync);
        await LoadAsync(1);
    }

    private Task OnJobChangedAsync(OcrJobChange change) =>
        InvokeAsync(async () =>
        {
            await LoadAsync(_page);
            StateHasChanged();
        });

    private async Task SearchChangedAsync()
    {
        _searchDebounce?.Cancel();
        _searchDebounce?.Dispose();
        _searchDebounce = CancellationTokenSource.CreateLinkedTokenSource(_disposed.Token);
        try
        {
            await Task.Delay(SearchDelay, _searchDebounce.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await LoadAsync(1);
    }

    private async Task FilterChangedAsync(ChangeEventArgs e)
    {
        _status = Enum.TryParse<OcrJobStatus>(e.Value?.ToString(), out var status) ? status : null;
        await LoadAsync(1);
    }

    private async Task LoadAsync(int page)
    {
        _error = null;
        try
        {
            var result = await ListHandler.HandleAsync(new ListOcrJobsQuery(page, Status: _status, Search: _search), _disposed.Token);
            if (result.IsFailure)
            {
                _error = Text.Localize(result.Error!);
                return;
            }

            _list = result.Value;
            _page = _list.Jobs.Page;
            if (_list.Jobs.Items.Count == 0 && _list.Jobs.HasPreviousPage)
                await LoadAsync(_list.Jobs.TotalPages);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "load jobs");
            _error = Text["LoadFailed"];
        }
    }

    private async Task DeleteAsync(string jobId)
    {
        _deleteError = null;
        _deletingId = jobId;
        try
        {
            var result = await DeleteHandler.HandleAsync(new DeleteOcrJobCommand(jobId), _disposed.Token);
            if (result.IsFailure)
                _deleteError = Text.Localize(result.Error!);
            await LoadAsync(_page);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "delete job");
            _deleteError = Text["UnexpectedError"];
        }
        finally
        {
            _deletingId = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "OCR UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);
}
