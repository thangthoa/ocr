using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.GetOcrPageImage;

public sealed record GetOcrPageImageQuery(string JobId, int PageNumber);

/// <summary>Proxies the service's page image so the API key never reaches the browser.</summary>
public sealed class GetOcrPageImageHandler(IOcrApiClient api)
{
    public async Task<Result<FileContent>> HandleAsync(GetOcrPageImageQuery query, CancellationToken cancellationToken)
    {
        if (!OcrJobId.TryParse(query.JobId, out var id) || query.PageNumber < 1)
            return OcrErrors.NotFound;

        var result = await api.GetPageImageAsync(id, query.PageNumber, cancellationToken);
        return result.IsSuccess ? result : OcrErrors.FromService(result.Error!);
    }
}
