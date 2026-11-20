using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.Features.Mockups.Reports;

/// <summary>A report of the Reports page as a CSV file, for the same report and period.</summary>
internal static class ReportEndpoints
{
    public const string DateFormat = "yyyy-MM-dd";

    public static string ExportUrl(ReportKind kind, DateOnly from, DateOnly to) =>
        QueryHelpers.AddQueryString("/reports/export.csv", new Dictionary<string, string?>
        {
            ["report"] = kind.ToString(),
            ["from"] = from.ToString(DateFormat, CultureInfo.InvariantCulture),
            ["to"] = to.ToString(DateFormat, CultureInfo.InvariantCulture),
        });

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    public static ReportKind? ParseKind(string? value) =>
        Enum.TryParse<ReportKind>(value, ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : null;

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/reports/export.csv", (string? report, string? from, string? to, ReportBuilder reports, HttpContext http) =>
        {
            var start = ParseDate(from);
            var end = ParseDate(to);
            if (ParseKind(report) is not { } kind)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown report.");
            if (ReportRange.Check(start, end, reports.Today) is { } error)
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "report." + error.ToString().ToLowerInvariant());

            var file = reports.Export(kind, start!.Value, end!.Value, LoginPage.DemoUserName);
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return endpoints;
    }
}
