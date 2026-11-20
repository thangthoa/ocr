using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.Features.Mockups.Approved;

/// <summary>Downloads for approved documents: one document's final version, and the filtered list as CSV.</summary>
internal static class ApprovedDocumentEndpoints
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string FinalFileUrl(string documentId, FinalFileFormat format) =>
        $"/documents/{Uri.EscapeDataString(documentId)}/final?format={format.ToString().ToLowerInvariant()}";

    /// <summary>The current version, for a document in review or archived (the document page).</summary>
    public static string CurrentFileUrl(string documentId, FinalFileFormat format) =>
        $"/documents/{Uri.EscapeDataString(documentId)}/file?format={format.ToString().ToLowerInvariant()}";

    /// <summary>The export link for the filters shown on the Approved page.</summary>
    public static string ExportUrl(DocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var values = new Dictionary<string, string?>
        {
            ["q"] = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            ["field"] = query.Field == DocumentSearchField.All ? null : query.Field.ToString(),
            ["department"] = query.Department?.ToString(),
            ["kind"] = query.Kind?.ToString(),
            ["confidentiality"] = query.Confidentiality?.ToString(),
            ["from"] = query.ApprovedFrom?.ToString(DateFormat, CultureInfo.InvariantCulture),
            ["to"] = query.ApprovedTo?.ToString(DateFormat, CultureInfo.InvariantCulture),
        };
        return QueryHelpers.AddQueryString("/approved/export.csv", values.Where(v => v.Value is not null));
    }

    /// <summary>Reads the filters of <see cref="ExportUrl"/>; values that do not parse are ignored.</summary>
    public static DocumentQuery ParseQuery(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return new DocumentQuery(
            Text: query["q"].ToString(),
            Field: Parse<DocumentSearchField>(query["field"]) ?? DocumentSearchField.All,
            Department: Parse<DocumentDepartment>(query["department"]),
            Kind: Parse<DocumentKind>(query["kind"]),
            Confidentiality: Parse<DocumentConfidentiality>(query["confidentiality"]),
            ApprovedFrom: ParseDate(query["from"]),
            ApprovedTo: ParseDate(query["to"]));
    }

    public static IEndpointRouteBuilder MapApprovedDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/documents/{id}/final", async (string id, string? format, ApprovedDocumentFiles files, HttpContext http, CancellationToken ct) =>
        {
            if (Parse<FinalFileFormat>(format) is not { } fileFormat)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown format.");

            return ToResult(await files.FinalAsync(id, fileFormat, LoginPage.DemoUserName, ct), http);
        });

        endpoints.MapGet("/documents/{id}/file", async (string id, string? format, ApprovedDocumentFiles files, HttpContext http, CancellationToken ct) =>
        {
            if (Parse<FinalFileFormat>(format) is not { } fileFormat)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown format.");

            return ToResult(await files.CurrentAsync(id, fileFormat, LoginPage.DemoUserName, ct), http);
        });

        // Read by the document server only (it has no session), to make a PDF: the link is signed and expires.
        endpoints.MapGet("/documents/{id}/text.docx", async (string id, [FromQuery(Name = "exp")] long expires,
            [FromQuery(Name = "sig")] string? signature, WordLinks links, DocumentCatalog catalog, CancellationToken ct) =>
        {
            if (!links.IsValidTextDocxLink(id, expires, signature))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var found = await catalog.GetAsync(id, ct);
            return found.IsSuccess && ApprovedDocumentFiles.HasText(found.Value)
                ? Results.File(ApprovedDocumentFiles.TextDocx(found.Value), ApprovedDocumentFiles.DocxContentType)
                : Results.NotFound();
        });

        endpoints.MapGet("/approved/export.csv", async (HttpRequest request, ApprovedDocumentFiles files, HttpContext http, CancellationToken ct) =>
            ToResult(await files.ExportAsync(ParseQuery(request.Query), LoginPage.DemoUserName, ct), http));

        return endpoints;
    }

    private static IResult ToResult(Result<DownloadFile> result, HttpContext http)
    {
        if (result.IsFailure)
        {
            // Details stay in the logs and the audit trail; the code tells the page what happened.
            var status = result.Error!.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                ErrorType.Validation => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status503ServiceUnavailable,
            };
            return Results.Problem(statusCode: status, title: result.Error.Code);
        }

        var file = result.Value;
        http.Response.Headers.XContentTypeOptions = "nosniff";
        http.Response.Headers.ContentSecurityPolicy = "sandbox";
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(file.Content, file.ContentType, file.FileName);
    }

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
