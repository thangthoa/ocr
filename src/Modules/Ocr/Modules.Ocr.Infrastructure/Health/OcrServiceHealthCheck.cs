using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modules.Ocr.Infrastructure.Api;

namespace Modules.Ocr.Infrastructure.Health;

/// <summary>Readiness: the OCR service answers its health endpoint.</summary>
internal sealed class OcrServiceHealthCheck(OcrApiClient api) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await api.PingAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "The OCR service is not reachable.");
}
