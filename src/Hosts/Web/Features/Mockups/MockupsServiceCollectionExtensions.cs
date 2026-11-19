using Microsoft.Extensions.DependencyInjection.Extensions;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups;

public static class MockupsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the documents mockup: the in-memory store (one per app, like the OCR job list) and
    /// the catalog that joins it with OCR jobs. Needs the OCR module's application services.
    /// </summary>
    public static IServiceCollection AddMockups(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditLog>();
        services.AddSingleton<CaptureBatchStore>();
        services.AddSingleton<DocumentStore>();
        services.AddScoped<DocumentCatalog>();
        return services;
    }
}
