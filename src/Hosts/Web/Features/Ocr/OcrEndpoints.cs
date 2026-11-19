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

    public static string PageImageUrl(string jobId, int page) => $"/ocr/jobs/{Uri.EscapeDataString(jobId)}/pages/{page}/image";

    public static string DownloadUrl(string jobId, OcrResultFormat format) =>
        $"/ocr/jobs/{Uri.EscapeDataString(jobId)}/download?format={format.ToString().ToLowerInvariant()}";

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
            if (!Enum.TryParse<OcrResultFormat>(format ?? nameof(OcrResultFormat.Docx), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
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
