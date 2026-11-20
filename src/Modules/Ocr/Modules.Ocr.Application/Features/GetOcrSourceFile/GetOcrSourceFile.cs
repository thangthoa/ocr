using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Features.GetOcrSourceFile;

public sealed record GetOcrSourceFileQuery(string Token);

/// <summary>Hands an uploaded file to the OCR service, which downloads it from the link it was sent.</summary>
public sealed class GetOcrSourceFileHandler(IOcrSourceFiles files)
{
    public Result<FileContent> Handle(GetOcrSourceFileQuery query) =>
        files.Open(query.Token) is { } file ? file : OcrErrors.NotFound;
}
