using Microsoft.Extensions.DependencyInjection.Extensions;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Activity;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Kpi;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Mockups.Reports;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.Features.Mockups;

public static class MockupsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the documents mockup: the simulated project history (ADR 0008), the in-memory store (one per app, like
    /// the OCR job list), the catalog that joins them with OCR jobs, the automatic quality check, KPI targets and reports,
    /// users and roles, the activity log, and the Word editor and preview (ADR 0004). Needs the OCR module's application
    /// services and <c>OcrJobNotifications</c>.
    /// </summary>
    public static IServiceCollection AddMockups(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditLog>();
        services.AddSingleton<CaptureBatchStore>();
        services.AddSingleton<QualitySettingsStore>();
        services.AddSingleton<KpiSettingsStore>();
        services.AddSingleton<UserDirectory>();
        services.AddSingleton<SampleActivity>();
        services.AddScoped<ActivityTrail>();
        services.AddScoped<ReportBuilder>();
        services.AddOptions<DocumentSampleOptions>().Bind(configuration.GetSection(DocumentSampleOptions.SectionName));
        services.AddOptions<HistoryOptions>().Bind(configuration.GetSection(HistoryOptions.SectionName));
        services.AddSingleton<ProjectHistory>();
        services.AddHostedService<HistoryWarmup>();
        services.AddSingleton<DocumentStore>();
        services.AddScoped<DocumentCatalog>();
        services.AddHostedService<QualityCheckTrigger>();
        services.AddScoped<ApprovedDocumentFiles>();

        services.AddOptions<WordEditorOptions>()
            .Bind(configuration.GetSection(WordEditorOptions.SectionName))
            .PostConfigure(options => ApplyEditorAliases(options, configuration))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<WordStorageOptions>()
            .Bind(configuration.GetSection(WordStorageOptions.SectionName))
            .PostConfigure(options =>
            {
                if (configuration[WordStorageOptions.PathVariable] is { Length: > 0 } path)
                    options.Path = path;
            });
        services.AddSingleton<IWordFileStore, DiskWordFileStore>();
        services.AddSingleton<WordLinks>();
        services.AddSingleton<DocumentServerClient>();
        services.AddScoped<WordEditorService>();
        services.AddScoped<WordPreviewService>();
        services.AddHttpClient(DocumentServerClient.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(2));
        return services;
    }

    private static void ApplyEditorAliases(WordEditorOptions options, IConfiguration configuration)
    {
        if (configuration[WordEditorOptions.PublicUrlVariable] is { Length: > 0 } publicUrl)
            options.PublicUrl = new Uri(publicUrl, UriKind.Absolute);
        if (configuration[WordEditorOptions.InternalUrlVariable] is { Length: > 0 } internalUrl)
            options.InternalUrl = new Uri(internalUrl, UriKind.Absolute);
        if (configuration[WordEditorOptions.AppUrlVariable] is { Length: > 0 } appUrl)
            options.AppUrl = new Uri(appUrl, UriKind.Absolute);
        if (configuration[WordEditorOptions.JwtSecretVariable] is { Length: > 0 } secret)
            options.JwtSecret = secret;
    }
}
