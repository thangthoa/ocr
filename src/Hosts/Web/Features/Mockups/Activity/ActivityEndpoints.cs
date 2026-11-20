using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.Features.Mockups.Activity;

/// <summary>The activity log as a CSV file, with the filters of the Activity logs page.</summary>
internal static class ActivityEndpoints
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string ExportUrl(ActivityQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var values = new Dictionary<string, string?>
        {
            ["q"] = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim(),
            ["user"] = query.User,
            ["category"] = query.Category?.ToString(),
            ["result"] = query.Result?.ToString(),
            ["from"] = query.From?.ToString(DateFormat, CultureInfo.InvariantCulture),
            ["to"] = query.To?.ToString(DateFormat, CultureInfo.InvariantCulture),
        };
        return QueryHelpers.AddQueryString("/users/activity/export.csv", values.Where(v => v.Value is not null));
    }

    /// <summary>Reads the filters of <see cref="ExportUrl"/> (and of the page's own address); values that do not parse are ignored.</summary>
    public static ActivityQuery ParseQuery(string? text, string? user, string? category, string? result, string? from, string? to) => new(
        Text: string.IsNullOrWhiteSpace(text) ? null : text,
        User: string.IsNullOrWhiteSpace(user) ? null : user,
        Category: Parse<ActivityCategory>(category),
        Result: Parse<ActivityResult>(result),
        From: ParseDate(from),
        To: ParseDate(to));

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/users/activity/export.csv", (HttpRequest request, ActivityTrail trail, HttpContext http) =>
        {
            var q = request.Query;
            var file = trail.Export(ParseQuery(q["q"], q["user"], q["category"], q["result"], q["from"], q["to"]), LoginPage.DemoUserName);
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.ContentSecurityPolicy = "sandbox";
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(file.Content, file.ContentType, file.FileName);
        });

        return endpoints;
    }

    private static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
