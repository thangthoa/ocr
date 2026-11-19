using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application;
using Modules.Ocr.Application.Ports;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Ocr;
using OppOcr.Web.Localization;

namespace OppOcr.Web.ComponentTests;

internal static class TestServices
{
    /// <summary>The real OCR handlers over an in-memory service, plus the documents mockup.</summary>
    public static (InMemoryOcr Ocr, OcrJobNotifications Notifications) AddOcrAndDocuments(this BunitServiceProvider services)
    {
        var ocr = new InMemoryOcr();
        var notifications = new OcrJobNotifications(NullLogger<OcrJobNotifications>.Instance);
        services.AddLogging();
        services.AddLocalization();
        services.AddOcrApplication();
        services.AddSingleton<IOcrApiClient>(ocr);
        services.AddSingleton<IOcrJobRegistry>(ocr);
        services.AddSingleton(notifications);
        services.AddSingleton<IOcrJobNotifier>(notifications);
        services.AddMockups();
        return (ocr, notifications);
    }

    public static void SetCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = LocalizationSetup.CreateCulture(name);
}
