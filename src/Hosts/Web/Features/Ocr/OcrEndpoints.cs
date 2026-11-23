using System.Text.Json;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DownloadOcrResult;
using Modules.Ocr.Application.Features.GetOcrPageImage;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Ocr;

/// <summary>
/// File endpoints for the OCR feature. They proxy the OCR service so its API key stays on the
/// server, and harden responses because the bytes originate from user uploads.
/// </summary>
internal static class OcrEndpoints
{
    /// <summary>An OCR job is shown as a document; this is its page.</summary>
    public static string JobUrl(string jobId) => $"documents/{Uri.EscapeDataString(jobId)}";

    /// <param name="revision">
    /// Changes when the job's pages change (<c>edits</c>), so the browser does not show a cached image of the page
    /// that used to have this number. The endpoint ignores it.
    /// </param>
    public static string PageImageUrl(string jobId, int page, int revision = 0) =>
        $"/ocr/jobs/{Uri.EscapeDataString(jobId)}/pages/{page}/image" + (revision > 0 ? $"?v={revision}" : string.Empty);

    public static string DownloadUrl(string jobId, OcrResultFormat format) =>
        $"/ocr/jobs/{Uri.EscapeDataString(jobId)}/download?format={FormatName(format)}";

    /// <summary>The OCR service's own name for the format: <c>docx</c>, <c>docx_layout</c>, <c>docx_gdocs</c>, <c>txt</c>…</summary>
    public static string FormatName(OcrResultFormat format) => JsonNamingPolicy.SnakeCaseLower.ConvertName(format.ToString());

    public static bool TryParseFormat(string? name, out OcrResultFormat format)
    {
        format = OcrResultFormat.Docx;
        if (name is null)
            return true;

        foreach (var candidate in Enum.GetValues<OcrResultFormat>())
        {
            if (string.Equals(FormatName(candidate), name, StringComparison.OrdinalIgnoreCase))
            {
                format = candidate;
                return true;
            }
        }

        return false;
    }

    public static IEndpointRouteBuilder MapOcrEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The OCR job pages were merged into Documents; bookmarks and old links still land there.
        endpoints.MapGet("/ocr/jobs", () => Results.LocalRedirect("~/documents"));
        endpoints.MapGet("/ocr/jobs/{id}", (string id) => Results.LocalRedirect($"~/{JobUrl(id)}"));

        var group = endpoints.MapGroup("/ocr/jobs/{id}");

        group.MapGet("/pages/{page:int}/image", async (string id, int page, GetOcrPageImageHandler handler, HttpContext http, CancellationToken ct) =>
            ToFile(await handler.HandleAsync(new GetOcrPageImageQuery(id, page), ct), http, attachment: false));

        group.MapGet("/download", async (string id, string? format, DownloadOcrResultHandler handler, HttpContext http, CancellationToken ct) =>
        {
            if (!TryParseFormat(format, out var parsed))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsupported format.");
            return ToFile(await handler.HandleAsync(new DownloadOcrResultQuery(id, parsed), ct), http, attachment: true);
        });

        return endpoints;
    }

    private static IResult ToFile(Result<FileContent> result, HttpContext http, bool attachment)
    {
        if (result.IsFailure)
        {
            var status = result.Error!.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Failure => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status400BadRequest,
            };
            return Results.Problem(statusCode: status, title: result.Error.Code);
        }

        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        http.Response.Headers.CacheControl = "private, max-age=300";
        var file = result.Value;
        return Results.Stream(file.Content, file.ContentType, attachment ? file.FileName : null);
    }
}
