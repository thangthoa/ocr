using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace OppOcr.Web.Features.Mockups.Word;

/// <summary>
/// HTTP endpoints of the Word editor: the document server fetches files and posts saves (signed
/// links, JWT), and people download saved Word versions and see their rendered pages.
/// </summary>
internal static partial class WordEditorEndpoints
{
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>A callback body is a small JSON object; anything bigger is not from the document server.</summary>
    private const long MaxCallbackBytes = 1024 * 1024;

    public static string EditorUrl(string documentId) => $"documents/{Uri.EscapeDataString(documentId)}/word";

    public static string VersionDownloadUrl(string documentId, int version) =>
        $"/documents/{Uri.EscapeDataString(documentId)}/word/versions/{version}";

    public static string PreviewPageUrl(string documentId, int version, int page) =>
        $"/documents/{Uri.EscapeDataString(documentId)}/word/preview/{version}/{page}";

    public static IEndpointRouteBuilder MapWordEditorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/documents/{id}/word");

        group.MapGet("/file", async (string id, [FromQuery(Name = "v")] int version, [FromQuery(Name = "exp")] long expires,
            [FromQuery(Name = "sig")] string? signature, WordLinks links, IWordFileStore files, CancellationToken ct) =>
        {
            if (!links.IsValidFileLink(id, version, expires, signature))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var stream = await files.OpenAsync(id, version, ct);
            return stream is null ? Results.NotFound() : Results.Stream(stream, DocxContentType);
        });

        group.MapPost("/callback", async ([FromRoute] string id, [FromQuery(Name = "sig")] string? signature, HttpRequest request,
            WordEditorService editor, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (request.ContentLength > MaxCallbackBytes)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            try
            {
                using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
                var outcome = await editor.HandleCallbackAsync(id, signature, body.RootElement, request.Headers.Authorization.ToString(), ct);
                return outcome == WordCallbackOutcome.Forbidden
                    ? Results.Json(new { error = 1 }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.Json(new { error = 0 });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The document server keeps the file and shows a save error; nothing is lost on its side.
                LogCallbackFailed(loggers.CreateLogger(typeof(WordEditorEndpoints)), ex, id);
                return Results.Json(new { error = 1 }, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        group.MapGet("/versions/{number:int}", async (string id, int number, IWordFileStore files, HttpContext http, CancellationToken ct) =>
        {
            Stream? stream;
            try
            {
                stream = await files.OpenAsync(id, number, ct);
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }

            if (stream is null)
                return Results.NotFound();
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
            return Results.Stream(stream, DocxContentType, $"{id}-v{number}.docx");
        });

        // A version never changes, so its rendered pages can be cached by the browser.
        group.MapGet("/preview/{number:int}/{page:int}", async (string id, int number, int page, IWordFileStore files, HttpContext http, CancellationToken ct) =>
        {
            Stream? stream;
            try
            {
                stream = await files.OpenPreviewPageAsync(id, number, page, ct);
            }
            catch (ArgumentException)
            {
                return Results.NotFound();
            }

            if (stream is null)
                return Results.NotFound();
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.Stream(stream, "image/png");
        });

        return endpoints;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Word editor: saving a callback for {DocumentId} failed")]
    private static partial void LogCallbackFailed(ILogger logger, Exception exception, string documentId);
}
