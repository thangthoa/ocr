using Bunit;
using Microsoft.AspNetCore.Components;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

/// <summary>
/// Every list of documents (All Documents, Review Queue, Document Archive) shows the same tracking columns,
/// and a dash where a value is not known yet.
/// </summary>
public sealed class DocumentListColumnsTests : BunitContext
{
    private static readonly string[] Required =
        ["Pages", "Uploaded by", "Uploaded at", "Approved by", "Approved date", "Version", "Last updated at"];

    private static readonly string[] TrackingCells =
        ["doc-pages", "doc-uploaded-by", "doc-uploaded", "doc-approved-by", "doc-approved", "doc-version", "doc-updated"];

    private readonly InMemoryOcr _ocr;

    public DocumentListColumnsTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments();
    }

    [Fact]
    public void Every_document_list_has_the_required_columns_in_the_same_order()
    {
        Assert.Equal(Required, TrackingHeaders<DocumentsPage>());
        Assert.Equal(Required, TrackingHeaders<ReviewPage>());
        Assert.Equal(Required, TrackingHeaders<ApprovedPage>());
    }

    [Fact]
    public void A_document_with_nothing_known_yet_shows_a_dash_in_every_tracking_column()
    {
        // Queued and not counted yet: no pages, no batch (so no uploader), no upload time, no approval, no version.
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "fresh.png", OcrJobStatus.Queued) with { PagesTotal = 0 });

        var cut = Render<DocumentsPage>();

        var row = cut.WaitForElement("tbody tr");
        Assert.Contains("fresh.png", row.TextContent, StringComparison.Ordinal);
        Assert.All(TrackingCells, cell => Assert.Equal("—", row.QuerySelector($"td.{cell}")!.TextContent.Trim()));
    }

    [Fact]
    public void An_archived_document_shows_who_uploaded_and_approved_it_and_when()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        var archived = cut.FindAll("tbody tr").First(row => row.TextContent.Contains("ADM-2022-0310.pdf", StringComparison.Ordinal));
        Assert.Equal("1", Cell(archived, "doc-pages"));
        Assert.Equal("capture02", Cell(archived, "doc-uploaded-by"));
        Assert.Matches(@"\d{4}", Cell(archived, "doc-uploaded"));
        Assert.Equal("review01", Cell(archived, "doc-approved-by"));
        Assert.Matches(@"\d{4}", Cell(archived, "doc-approved"));
        Assert.Matches(@"\d{4}", archived.QuerySelector("td.doc-approved")!.GetAttribute("title"));
        Assert.Equal("1.1", Cell(archived, "doc-version"));
        Assert.Matches(@"\d{4}", Cell(archived, "doc-updated"));
    }

    [Fact]
    public void Documents_waiting_for_review_have_no_approval_yet()
    {
        var cut = Render<ReviewPage>();
        cut.WaitForElement("tbody tr");

        Assert.NotEmpty(cut.FindAll("td.doc-approved-by"));
        Assert.All(cut.FindAll("td.doc-approved-by, td.doc-approved"), cell => Assert.Equal("—", cell.TextContent.Trim()));
        Assert.All(cut.FindAll("td.doc-uploaded-by"), cell => Assert.NotEqual("—", cell.TextContent.Trim()));
    }

    /// <summary>The headers from the tracking block on, as long as the required list.</summary>
    private string[] TrackingHeaders<TPage>()
        where TPage : IComponent
    {
        var cut = Render<TPage>();
        cut.WaitForElement("tbody tr");
        var headers = cut.FindAll("thead th").Select(th => th.TextContent.Trim()).ToArray();
        var start = Array.IndexOf(headers, Required[0]);
        Assert.True(start >= 0, $"{typeof(TPage).Name} has no {Required[0]} column");
        return headers[start..Math.Min(start + Required.Length, headers.Length)];
    }

    private static string Cell(AngleSharp.Dom.IElement row, string name) => row.QuerySelector($"td.{name}")!.TextContent.Trim();
}
