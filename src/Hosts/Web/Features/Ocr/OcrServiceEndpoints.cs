using Microsoft.Extensions.Primitives;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.GetOcrSourceFile;
using Modules.Ocr.Application.Features.ReceiveOcrJobCallback;
using Modules.Ocr.Application.Ports;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Ocr;

/// <summary>
/// Endpoints the OCR service calls (ADR 0006): it downloads uploaded files from short-lived links, and posts
/// each job when it is done or has failed. Neither request has a user session: the unguessable link and the
/// callback's signature are what prove them.
/// </summary>
internal static class OcrServiceEndpoints
{
    public const string TimestampHeader = "X-OCR-Timestamp";
    public const string SignatureHeader = "X-OCR-Signature";

    /// <summary>The service's guide puts a job at 10–50 KB; this leaves room for 20 pages of escaped text.</summary>
    internal const int MaxCallbackBytes = 4 * 1024 * 1024;

    private const int ReadChunkBytes = 16 * 1024;

    private const string SourceFilesPrefix = "/" + OcrServicePaths.SourceFiles + "/";

    /// <summary>The request path for logs: a file link's token is a credential, so it is hidden.</summary>
    public static string PathForLogs(string path) =>
        path.StartsWith(SourceFilesPrefix, StringComparison.OrdinalIgnoreCase) ? SourceFilesPrefix + "***" : path;

    public static IEndpointRouteBuilder MapOcrServiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // {name} only lets the service see the file name and type in the link; the token alone finds the file.
        endpoints.MapGet($"{SourceFilesPrefix}{{token}}/{{name?}}", GetSourceFile);
        endpoints.MapPost($"/{OcrServicePaths.Callback}", ReceiveCallbackAsync);
        return endpoints;
    }

    internal static IResult GetSourceFile(string token, GetOcrSourceFileHandler handler, HttpContext http)
    {
        var result = handler.Handle(new GetOcrSourceFileQuery(token));
        if (result.IsFailure)
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: result.Error!.Code);

        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.CacheControl = "no-store";
        var file = result.Value;
        return Results.Stream(file.Content, file.ContentType, file.FileName);
    }

    internal static async Task<IResult> ReceiveCallbackAsync(
        HttpContext http,
        ReceiveOcrJobCallbackHandler handler,
        IOcrJobNotifier notifier,
        IHostApplicationLifetime lifetime,
        CancellationToken cancellationToken)
    {
        // The signature covers the exact bytes received, so the body is read raw, not model-bound.
        var body = await ReadBodyAsync(http.Request, MaxCallbackBytes, cancellationToken);
        if (body is null)
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Callback body too large.");

        var headers = http.Request.Headers;
        var callback = new OcrCallback(body, Single(headers[TimestampHeader]), Single(headers[SignatureHeader]));
        var result = handler.Handle(new ReceiveOcrJobCallbackCommand(callback));
        if (result.IsFailure)
        {
            var status = result.Error!.Type == ErrorType.Unauthorized ? StatusCodes.Status401Unauthorized : StatusCodes.Status400BadRequest;
            return Results.Problem(statusCode: status, title: result.Error.Code);
        }

        // The service waits only 10 seconds for this answer and sends the job again without one. Open pages
        // reload the job from the service when told, which can take longer, so they are told afterwards.
        if (result.Value is { } change)
            http.Response.OnCompleted(() => notifier.NotifyAsync(change, lifetime.ApplicationStopping));
        return Results.Ok();
    }

    /// <returns>The body, or null when it is larger than <paramref name="maxBytes"/>.</returns>
    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        if (request.ContentLength > maxBytes)
            return null;

        using var buffer = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    // A repeated header is not something the service sends; treating it as missing fails the check.
    private static string? Single(StringValues values) => values.Count == 1 ? values[0] : null;
}
