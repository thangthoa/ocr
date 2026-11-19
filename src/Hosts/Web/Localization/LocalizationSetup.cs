using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace OppOcr.Web.Localization;

internal static class LocalizationSetup
{
    public const string DefaultCulture = "en";

    public const string ArabicCulture = "ar";

    public static readonly IReadOnlyList<string> SupportedCultures = [DefaultCulture, ArabicCulture];

    public static IServiceCollection AddAppLocalization(this IServiceCollection services)
    {
        services.AddLocalization();
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = SupportedCultures.Select(CreateCulture).ToList();
            options.DefaultRequestCulture = new RequestCulture(DefaultCulture);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;

            // Only an explicit user choice changes the language; the browser's Accept-Language
            // is ignored so everyone starts in the default (English).
            options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        });
        return services;
    }

    public static IEndpointRouteBuilder MapCultureEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/culture/set", (string culture, string? redirectUri, HttpContext httpContext) =>
        {
            if (!SupportedCultures.Contains(culture, StringComparer.OrdinalIgnoreCase))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsupported culture.");

            httpContext.Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions
                {
                    MaxAge = TimeSpan.FromDays(365),
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = httpContext.Request.IsHttps,
                });

            return Results.LocalRedirect(IsLocalPath(redirectUri) ? redirectUri! : "/");
        });
        return endpoints;
    }

    /// <summary>
    /// .NET's neutral "ar" culture defaults to the Umm al-Qura (Hijri) calendar; dates in this
    /// app are shown in the Gregorian calendar so they match the documents and the OCR service.
    /// </summary>
    internal static CultureInfo CreateCulture(string name)
    {
        var culture = new CultureInfo(name);
        if (culture.DateTimeFormat.Calendar is not GregorianCalendar)
            culture.DateTimeFormat.Calendar = new GregorianCalendar();
        return culture;
    }

    // Only same-site absolute paths are allowed back, so the endpoint cannot be used as an open redirect.
    internal static bool IsLocalPath(string? uri) =>
        !string.IsNullOrEmpty(uri)
        && uri[0] == '/'
        && (uri.Length == 1 || (uri[1] != '/' && uri[1] != '\\'));
}
