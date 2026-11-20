using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Infrastructure.Api;
using Modules.Ocr.Infrastructure.Health;
using Modules.Ocr.Infrastructure.Pdf;
using Modules.Ocr.Infrastructure.Registry;
using Modules.Ocr.Infrastructure.Worker;

namespace Modules.Ocr.Infrastructure;

public static class DependencyInjection
{
    /// <remarks>The host must also register an <see cref="IOcrJobNotifier"/>.</remarks>
    public static IServiceCollection AddOcrInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OcrApiOptions>()
            .Bind(configuration.GetSection(OcrApiOptions.SectionName))
            .PostConfigure(options => ApplyEnvironmentAliases(options, configuration))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<OcrApiClient>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<OcrApiOptions>>().Value;
            http.BaseAddress = new Uri(options.BaseUrl!.AbsoluteUri.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
            http.DefaultRequestHeaders.Add(OcrApiClient.ApiKeyHeader, options.ApiKey);
        });
        services.AddTransient<IOcrApiClient>(sp => sp.GetRequiredService<OcrApiClient>());
        services.AddSingleton<OcrSourceFileStore>();
        services.AddSingleton<IOcrSourceFiles>(sp => sp.GetRequiredService<OcrSourceFileStore>());
        services.AddSingleton<IOcrCallbackReader, OcrCallbackReader>();
        services.AddSingleton<IPdfComposer, ImagePdfComposer>();

        services.AddOptions<OcrWorkerOptions>().Bind(configuration.GetSection(OcrWorkerOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IOcrJobRegistry>(sp => new InMemoryOcrJobRegistry(sp.GetRequiredService<IOptions<OcrWorkerOptions>>().Value.RegistryCapacity));
        services.TryAddSingleton(TimeProvider.System);
        if (configuration.GetSection(OcrWorkerOptions.SectionName).Get<OcrWorkerOptions>()?.Enabled ?? true)
            services.AddHostedService<OcrJobWorker>();

        return services;
    }

    public static IHealthChecksBuilder AddOcrHealthChecks(this IHealthChecksBuilder builder, params string[] tags) =>
        builder.AddCheck<OcrServiceHealthCheck>("ocr-service", tags: tags);

    private static void ApplyEnvironmentAliases(OcrApiOptions options, IConfiguration configuration)
    {
        if (configuration[OcrApiOptions.BaseUrlVariable] is { Length: > 0 } baseUrl)
            options.BaseUrl = new Uri(baseUrl, UriKind.Absolute);
        if (configuration[OcrApiOptions.ApiKeyVariable] is { Length: > 0 } apiKey)
            options.ApiKey = apiKey;
        if (configuration[OcrApiOptions.AppUrlVariable] is { Length: > 0 } appUrl)
            options.AppUrl = new Uri(appUrl, UriKind.Absolute);
        if (configuration[OcrApiOptions.CallbackSecretVariable] is { Length: > 0 } callbackSecret)
            options.CallbackSecret = callbackSecret;
    }
}
