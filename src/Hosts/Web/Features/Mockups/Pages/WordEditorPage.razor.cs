using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Word;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>Edits a document's Word file in OnlyOffice Docs (ADR 0004). Saves arrive through the editor callback.</summary>
public sealed partial class WordEditorPage : IAsyncDisposable
{
    // Generous: the first load downloads several MB of editor scripts. A late start still clears the warning.
    internal static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(45);

    private readonly CancellationTokenSource _disposed = new();
    private DotNetObjectReference<WordEditorPage>? _self;
    private ElementReference _container;
    private IJSObjectReference? _module;
    private WordEditorSession? _session;
    private string? _error;
    private string? _stalledHost;
    private bool _opening;

    [Parameter]
    public string Id { get; set; } = default!;

    [Inject] private WordEditorService Editor { get; set; } = default!;

    [Inject] private IJSRuntime Js { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private IStringLocalizer<OcrText> OcrText { get; set; } = default!;

    [Inject] private ILogger<WordEditorPage> Logger { get; set; } = default!;

    private static string BackArrow => CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? "→" : "←";

    public async ValueTask DisposeAsync()
    {
        await _disposed.CancelAsync();
        _disposed.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("close");
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The browser is already gone.
            }
        }

        _self?.Dispose();
    }

    /// <summary>The editor app did not report ready in time; most often a browser extension blocked one of its scripts.</summary>
    [JSInvokable]
    public void EditorStalled()
    {
        _stalledHost = Uri.TryCreate(_session?.ApiScriptUrl, UriKind.Absolute, out var api) ? api.Authority : null;
        StateHasChanged();
    }

    [JSInvokable]
    public void EditorStarted()
    {
        if (_stalledHost is null)
            return;
        _stalledHost = null;
        StateHasChanged();
    }

    // Only once interactive: the editor needs the browser, and prerendering must not create versions.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await OpenAsync();
    }

    private async Task OpenAsync()
    {
        _opening = true;
        _error = null;
        _stalledHost = null;
        try
        {
            var result = await Editor.OpenAsync(Id, LoginPage.DemoUserName, CultureInfo.CurrentUICulture, _disposed.Token);
            if (result.IsFailure)
            {
                _error = Describe(result.Error!);
                StateHasChanged();
                return;
            }

            _session = result.Value;
            StateHasChanged();
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", _disposed.Token, "./js/word-editor.js");
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync(
                "open", _disposed.Token, _session.ApiScriptUrl, _container, _session.Config, _self, (int)StartTimeout.TotalMilliseconds);
        }
        catch (JSException ex)
        {
            LogEditorFailed(Logger, ex, Id);
            _error = Text["Word.ServerUnreachable"];
            StateHasChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not JSDisconnectedException)
        {
            LogEditorFailed(Logger, ex, Id);
            _error = OcrText["UnexpectedError"];
            StateHasChanged();
        }
        finally
        {
            _opening = false;
        }
    }

    private string Describe(Error error) => error.Code switch
    {
        WordErrors.NotEditable => Text["Word.NotEditable"],
        WordErrors.EditorDisabled => Text["Word.Unavailable"],
        WordErrors.ServerNotReady => Text["Word.ServerNotReady"],
        _ when error.Type == ErrorType.NotFound => Text["Detail.NotFound"],
        _ => OcrText.Localize(error),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Word editor: could not open {DocumentId}")]
    private static partial void LogEditorFailed(ILogger logger, Exception exception, string documentId);
}
