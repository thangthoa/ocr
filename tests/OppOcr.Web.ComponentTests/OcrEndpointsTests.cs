using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Ocr;

namespace OppOcr.Web.ComponentTests;

/// <summary>The download proxy names formats as the OCR service does, so its links read like the service's own.</summary>
public sealed class OcrEndpointsTests
{
    [Theory]
    [InlineData(OcrResultFormat.Docx, "docx")]
    [InlineData(OcrResultFormat.DocxLayout, "docx_layout")]
    [InlineData(OcrResultFormat.DocxGdocs, "docx_gdocs")]
    [InlineData(OcrResultFormat.Txt, "txt")]
    public void Download_links_name_the_format_as_the_service_does(OcrResultFormat format, string name)
    {
        Assert.Equal($"/ocr/jobs/job_1/download?format={name}", OcrEndpoints.DownloadUrl("job_1", format));
        Assert.True(OcrEndpoints.TryParseFormat(name.ToUpperInvariant(), out var parsed));
        Assert.Equal(format, parsed);
    }

    [Fact]
    public void A_download_without_a_format_is_the_plain_docx_and_an_unknown_one_is_refused()
    {
        Assert.True(OcrEndpoints.TryParseFormat(null, out var none));
        Assert.Equal(OcrResultFormat.Docx, none);
        Assert.False(OcrEndpoints.TryParseFormat("DocxLayout", out _));
        Assert.False(OcrEndpoints.TryParseFormat("pdf", out _));
        Assert.False(OcrEndpoints.TryParseFormat("1", out _));
    }
}
