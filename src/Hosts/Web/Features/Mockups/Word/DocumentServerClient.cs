using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OppOcr.Web.Features.Mockups.Word;

/// <summary>The document server refused a request or answered with something this app does not accept.</summary>
public sealed class DocumentServerException : InvalidOperationException
{
    public DocumentServerException()
    {
    }

    public DocumentServerException(string message)
        : base(message)
    {
    }

    public DocumentServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Server-side calls to OnlyOffice Docs over its internal address (ADR 0004): readiness, the
/// conversion service, and downloading the files it produces.
/// </summary>
public sealed partial class DocumentServerClient(
    IOptions<WordEditorOptions> options,
    IHttpClientFactory httpClients,
    ILogger<DocumentServerClient> logger)
{
    public const string HttpClientName = "word-editor";
    private static readonly TimeSpan HealthCheckTimeout = TimeSpan.FromSeconds(5);

    private Uri Server => options.Value.DocumentServerUrl ?? throw new InvalidOperationException("The Word editor is not configured.");

    /// <summary>OnlyOffice answers <c>true</c> on <c>/healthcheck</c> once it can open documents.</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(HealthCheckTimeout);
        try
        {
            using var http = httpClients.CreateClient(HttpClientName);
            var answer = await http.GetStringAsync(new Uri(Server, "healthcheck"), timeout.Token);
            return answer.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogServerNotReady(logger, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Runs the conversion service synchronously and returns the converted file. The request names the
    /// source file by a URL the document server downloads; the request is signed like the editor config.
    /// </summary>
    public async Task<byte[]> ConvertAsync(IReadOnlyDictionary<string, object?> request, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>(request) { ["async"] = false };
        body["token"] = OnlyOfficeJwt.Sign(body, options.Value.JwtSecret!);

        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync(new Uri(Server, "converter"), body, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var answer = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = answer.RootElement;

        // Negative codes: -4 the file could not be downloaded, -8 the token was rejected, and so on.
        if (root.TryGetProperty("error", out var error))
            throw new DocumentServerException($"The document server could not convert the file (error {error.GetRawText()}).");
        if (!root.TryGetProperty("endConvert", out var done) || done.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("fileUrl", out var fileUrl) || fileUrl.GetString() is not { } url)
            throw new DocumentServerException("The document server did not finish the conversion.");

        return await DownloadAsync(url, cancellationToken);
    }

    /// <summary>
    /// Downloads a file the document server names, using the address the browser or this request used.
    /// Only that server is ever contacted (rewritten to its internal address), so a forged URL cannot make
    /// this app fetch from elsewhere.
    /// </summary>
    public async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var target = new Uri(url, UriKind.Absolute);
        if (settings.PublicUrl is { } publicUrl && SameOrigin(target, publicUrl))
            target = new Uri(Server, target.PathAndQuery);
        if (!SameOrigin(target, Server))
            throw new DocumentServerException("The document server named a file outside its own address.");

        var maxBytes = settings.MaxFileSizeMegabytes * 1024L * 1024L;
        using var http = httpClients.CreateClient(HttpClientName);
        using var response = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new DocumentServerException("The file is larger than allowed.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new DocumentServerException("The file is larger than allowed.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Word editor: the document server is not ready ({Reason})")]
    private static partial void LogServerNotReady(ILogger logger, string reason);
}
