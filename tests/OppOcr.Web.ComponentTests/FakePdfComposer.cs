using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Ports;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.ComponentTests;

/// <summary>Records the pages it is given and writes a stand-in PDF (the real composer is tested in Infrastructure).</summary>
internal sealed class FakePdfComposer : IPdfComposer
{
    public List<IReadOnlyList<PageImage>> Calls { get; } = [];

    public bool Fail { get; set; }

    public async Task<Result> ComposeAsync(IReadOnlyList<PageImage> pages, Stream destination, CancellationToken cancellationToken)
    {
        Calls.Add(pages);
        if (Fail)
            return OcrErrors.ImageNotSupported;

        await destination.WriteAsync("%PDF-1.4\n%%EOF\n"u8.ToArray(), cancellationToken);
        return Result.Success();
    }
}
