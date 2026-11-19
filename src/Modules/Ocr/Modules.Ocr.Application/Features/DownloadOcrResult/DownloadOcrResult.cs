using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.DownloadOcrResult;

public sealed record DownloadOcrResultQuery(string JobId, OcrResultFormat Format);

public sealed class DownloadOcrResultHandler(IOcrApiClient api)
{
    public async Task<Result<FileContent>> HandleAsync(DownloadOcrResultQuery query, CancellationToken cancellationToken)
    {
        if (!OcrJobId.TryParse(query.JobId, out var id))
            return OcrErrors.NotFound;

        var result = await api.DownloadAsync(id, query.Format, cancellationToken);
        if (result.IsSuccess)
            return result;

        // The service answers 409 not_ready until the job is done (API doc §3.4b).
        return result.Error!.Type == ErrorType.Conflict ? OcrErrors.NotReady : OcrErrors.FromService(result.Error);
    }
}
