using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application;
using Modules.Ocr.Application.Ports;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Ocr;
using OppOcr.Web.Localization;

namespace OppOcr.Web.ComponentTests;

internal static class TestServices
{
    /// <summary>
    /// The real OCR handlers over an in-memory service, plus the documents mockup. Word files go to a
    /// fresh temporary folder; <paramref name="settings"/> can turn the Word editor on.
    /// </summary>
    public static (InMemoryOcr Ocr, OcrJobNotifications Notifications) AddOcrAndDocuments(this BunitServiceProvider services,
        IDictionary<string, string?>? settings = null)
    {
        var ocr = new InMemoryOcr();
        var notifications = new OcrJobNotifications(NullLogger<OcrJobNotifications>.Instance);
        services.AddLogging();
        services.AddLocalization();
        services.AddOcrApplication();
        services.AddSingleton<IOcrApiClient>(ocr);
        services.AddSingleton<IOcrJobRegistry>(ocr);
        services.AddSingleton<IPdfComposer>(new FakePdfComposer());
        services.AddSingleton(notifications);
        services.AddSingleton<IOcrJobNotifier>(notifications);
        services.AddDocumentsMockup(settings);
        return (ocr, notifications);
    }

    /// <summary>
    /// The mockup's services with a temporary content root (for Word files) and the given settings. The
    /// sample documents are the fixtures of most tests, so they are on unless the settings turn them off.
    /// </summary>
    public static IServiceCollection AddDocumentsMockup(this IServiceCollection services, IDictionary<string, string?>? settings = null)
    {
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "ocr-component-tests", Guid.NewGuid().ToString("N"))).FullName));
        var values = new Dictionary<string, string?> { [SamplesEnabled] = "true" };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            values[key] = value;
        return services.AddMockups(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }

    public const string SamplesEnabled = DocumentSampleOptions.SectionName + ":Enabled";

    public static void SetCulture(string name) =>
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = LocalizationSetup.CreateCulture(name);
}

internal sealed class TestHostEnvironment(string contentRoot) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "OppOcr.Web.ComponentTests";

    public string ContentRootPath { get; set; } = contentRoot;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
