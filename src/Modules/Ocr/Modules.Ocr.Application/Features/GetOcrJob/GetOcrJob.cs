using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.GetOcrJob;

public sealed record GetOcrJobQuery(string JobId);

public sealed class GetOcrJobHandler(IOcrApiClient api, IOcrJobRegistry registry)
{
    public async Task<Result<OcrJob>> HandleAsync(GetOcrJobQuery query, CancellationToken cancellationToken)
    {
        if (!OcrJobId.TryParse(query.JobId, out var id))
            return OcrErrors.NotFound;

        var result = await api.GetAsync(id, cancellationToken);
        if (result.IsFailure)
            return OcrErrors.FromService(result.Error!);

        registry.Update(result.Value);
        return result;
    }
}
