using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.ComponentTests;

/// <summary>
/// Archived documents (approving a document archives it, ADR 0007): who approved them, the Document
/// Archive page, the final-version downloads and the CSV export. Samples archived in the seed, newest
/// approval first: 000112, 000111, 000109, 000106, 000104, 000102, 000101.
/// </summary>
public sealed class ApprovedDocumentsTests : BunitContext
{
    private readonly InMemoryOcr _ocr;

    public ApprovedDocumentsTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments();
        Services.AddHealthChecks(); // the activity log lives on the Settings page, which also shows health
    }

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    private ApprovedDocumentFiles Files => Services.GetRequiredService<ApprovedDocumentFiles>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    [Fact]
    public void Archiving_records_who_approved_and_when_and_sending_back_clears_it()
    {
        var before = DateTime.UtcNow;
        Store.ChangeStatus("DOC-2026-000105", DocumentStatus.Archived, "reviewer.1");

        var approval = Store.FindSample("DOC-2026-000105")!.Approval!;
        Assert.Equal("reviewer.1", approval.By);
        Assert.InRange(approval.AtUtc, before, DateTime.UtcNow);

        // Archiving an archived document again keeps the first approval.
        Store.ChangeStatus("DOC-2026-000105", DocumentStatus.Archived, "records.1");
        Assert.Equal(approval, Store.FindSample("DOC-2026-000105")!.Approval);

        Store.ChangeStatus("DOC-2026-000103", DocumentStatus.Archived, "reviewer.1");
        Store.ChangeStatus("DOC-2026-000103", DocumentStatus.NeedsReview, "reviewer.1");
        Assert.Null(Store.FindSample("DOC-2026-000103")!.Approval);
    }

    [Fact]
    public void Archive_page_lists_only_archived_documents_newest_approval_first()
    {
        var cut = Render<ApprovedPage>();

        cut.WaitForAssertion(() => Assert.Equal(
            ["ADM-2022-0310.pdf", "HR-2018-0044.pdf", "PV-2020-0021.pdf", "LG-2011-0032.pdf", "HR-2016-0187.pdf", "PO-2019-0388.pdf", "PV-2019-0413.pdf"],
            Names(cut)));
        Assert.Contains("7 document(s)", cut.Find("#approved-count").TextContent, StringComparison.Ordinal);
        Assert.All(cut.FindAll(".approved-by span[dir=ltr]"), approver => Assert.Equal("m.khalid", approver.TextContent));
        Assert.Equal("/documents/DOC-2026-000112/final?format=docx", cut.Find("tbody tr .download-docx").GetAttribute("href"));
        Assert.Equal("/documents/DOC-2026-000112/final?format=txt", cut.Find("tbody tr .download-txt").GetAttribute("href"));
        Assert.Equal("/approved/export.csv", cut.Find("#export-csv").GetAttribute("href"));
    }

    [Fact]
    public void A_real_document_approved_just_now_comes_first_with_its_approver()
    {
        var page = new OcrPage(1, OcrDocumentType.Printed, "نص", 10, "A v2", [], Cached: false);
        var job = _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000031", "memo.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] });
        Store.FromJob(job);
        Store.ChangeStatus(job.Id.Value, DocumentStatus.Archived, "admin");

        var cut = Render<ApprovedPage>();

        cut.WaitForAssertion(() => Assert.Equal("memo.png", Names(cut)[0]));
        Assert.Equal("admin", cut.Find("tbody tr .approved-by span[dir=ltr]").TextContent);
    }

    [Fact]
    public void Filters_narrow_the_list_and_the_export_link_carries_them()
    {
        var cut = Render<ApprovedPage>();
        cut.WaitForElement("tbody tr");

        cut.Find("#approved-department").Change(nameof(DocumentDepartment.Finance));
        Assert.Equal(["PV-2020-0021.pdf", "PV-2019-0413.pdf"], Names(cut));
        Assert.Equal("/approved/export.csv?department=Finance", cut.Find("#export-csv").GetAttribute("href"));

        cut.Find("button.btn-outline-secondary").Click(); // Clear filters
        cut.Find("#approved-confidentiality").Change(nameof(DocumentConfidentiality.Confidential));
        Assert.Equal(["HR-2018-0044.pdf", "HR-2016-0187.pdf", "PV-2019-0413.pdf"], Names(cut));

        cut.Find("button.btn-outline-secondary").Click();
        cut.Find("#approved-from").Change("2026-11-16");
        Assert.Equal(["ADM-2022-0310.pdf", "HR-2018-0044.pdf"], Names(cut));
        Assert.Equal("/approved/export.csv?from=2026-11-16", cut.Find("#export-csv").GetAttribute("href"));

        cut.Find("#approved-search").Input("no such document");
        Assert.Contains("No documents match these filters.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_archived_documents_the_page_points_to_review()
    {
        foreach (var id in new[] { "DOC-2026-000101", "DOC-2026-000102", "DOC-2026-000104", "DOC-2026-000106", "DOC-2026-000109", "DOC-2026-000111", "DOC-2026-000112" })
            Store.ChangeStatus(id, DocumentStatus.NeedsReview, "admin");

        var cut = Render<ApprovedPage>();

        cut.WaitForAssertion(() => Assert.Contains("No archived documents yet.", cut.Markup, StringComparison.Ordinal));
        Assert.NotNull(cut.Find(".empty-state a[href='review']"));
        Assert.Empty(cut.FindAll("#export-csv"));
    }

    [Fact]
    public void Export_is_not_offered_when_the_ocr_jobs_cannot_be_loaded()
    {
        _ocr.Unavailable = true;

        var cut = Render<ApprovedPage>();

        cut.WaitForAssertion(() => Assert.Contains("OCR jobs could not be loaded", cut.Markup, StringComparison.Ordinal));
        Assert.Empty(cut.FindAll("#export-csv"));
    }

    [Fact]
    public async Task Final_text_is_the_reviewed_text_and_the_download_is_logged()
    {
        var result = await Files.FinalAsync("DOC-2026-000104", FinalFileFormat.Txt, "admin", Ct);

        var file = result.Value;
        Assert.Equal(("HR-2016-0187-v1.1.txt", "text/plain; charset=utf-8"), (file.FileName, file.ContentType));
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        Assert.Equal(string.Join("\n\f\n", Store.FindSample("DOC-2026-000104")!.PageTexts), Encoding.UTF8.GetString(file.Content[3..]));

        var entry = Assert.Single(Audit.Latest(), e => e.Action == AuditAction.Downloaded);
        Assert.Equal(("admin", "DOC-2026-000104", "1.1", "TXT"), (entry.User, entry.DocumentId, entry.Version, entry.Detail));
    }

    [Fact]
    public async Task Final_docx_is_the_word_file_only_while_it_holds_the_current_text()
    {
        const string Id = "DOC-2026-000112";
        var pages = Store.FindSample(Id)!.PageTexts;

        // No Word file: a DOCX is made from the approved text.
        var generated = (await Files.FinalAsync(Id, FinalFileFormat.Docx, "admin", Ct)).Value;
        Assert.Equal(("ADM-2022-0310-v1.1.docx", ApprovedDocumentFiles.DocxContentType), (generated.FileName, generated.ContentType));
        Assert.Equal(pages, DocxText.ExtractPages(new MemoryStream(generated.Content)));

        // Archived documents are final: they are changed after being sent back to review, then archived again.
        // Saved from the Word editor: that file, with its formatting, is the final version.
        Store.ChangeStatus(Id, DocumentStatus.NeedsReview, "admin");
        var wordFiles = Services.GetRequiredService<IWordFileStore>();
        var edited = DocxText.Create(["edited in Word"]);
        var (word, _) = await wordFiles.SaveAsync(Id, new MemoryStream(edited), "admin", WordVersionSource.Edited, Ct);
        Store.RecordWordEdit(Id, ["edited in Word"], "admin", word.Number);
        Store.ChangeStatus(Id, DocumentStatus.Archived, "admin");
        var fromWord = (await Files.FinalAsync(Id, FinalFileFormat.Docx, "admin", Ct)).Value;
        Assert.Equal($"ADM-2022-0310-word-v{word.Number}.docx", fromWord.FileName);
        Assert.Equal(edited, fromWord.Content);

        // Edited again as text afterwards: the Word file is out of date, so the text is used.
        Store.ChangeStatus(Id, DocumentStatus.NeedsReview, "admin");
        Store.SaveEdit(Id, ["edited as text"], Store.FindSample(Id)!.Metadata, "admin", null);
        Store.ChangeStatus(Id, DocumentStatus.Archived, "admin");
        var fromText = (await Files.FinalAsync(Id, FinalFileFormat.Docx, "admin", Ct)).Value;
        Assert.Equal(["edited as text"], DocxText.ExtractPages(new MemoryStream(fromText.Content)));
    }

    [Theory]
    [InlineData("DOC-2026-000105", ApprovedErrors.NotFinalCode)]
    [InlineData("DOC-404", OcrErrorCodes.NotFound)]
    public async Task Only_approved_and_archived_documents_have_a_final_version(string id, string expectedCode)
    {
        var result = await Files.FinalAsync(id, FinalFileFormat.Docx, "admin", Ct);

        Assert.Equal(expectedCode, result.Error?.Code);
        Assert.DoesNotContain(Audit.Latest(), e => e.Action == AuditAction.Downloaded);
        Assert.True((await Files.FinalAsync("DOC-2026-000101", FinalFileFormat.Txt, "admin", Ct)).IsSuccess); // archived
    }

    [Fact]
    public async Task Export_lists_the_filtered_approved_documents_as_csv()
    {
        var result = await Files.ExportAsync(new DocumentQuery(Department: DocumentDepartment.Finance), "admin", Ct);

        var file = result.Value;
        Assert.StartsWith("approved-documents-", file.FileName, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetPreamble(), file.Content[..3]);
        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("Document ID,File name,Title,Reference no.,Document date,Department,Document type,Confidentiality,Keywords,Pages,Version,Uploaded by,Uploaded,Approved by,Approved", lines[0]);
        Assert.StartsWith("DOC-2026-000109,PV-2020-0021.pdf,", lines[1], StringComparison.Ordinal);
        Assert.EndsWith(",2020-01-22,Finance,Payment voucher,Internal,تدريب; دفعة,5,1.1,s.karim,2026-11-15T09:00:00Z,m.khalid,2026-11-15T11:10:00Z", lines[1], StringComparison.Ordinal);
        Assert.Equal("2", Assert.Single(Audit.Latest(), e => e.Action == AuditAction.Exported).Detail);
    }

    [Fact]
    public async Task Export_makes_formula_like_values_inert()
    {
        const string Id = "DOC-2026-000112";
        Store.ChangeStatus(Id, DocumentStatus.NeedsReview, "admin");
        Store.SaveEdit(Id, Store.FindSample(Id)!.PageTexts, Store.FindSample(Id)!.Metadata with { Title = "=HYPERLINK(\"http://x\")" }, "admin", null);
        Store.ChangeStatus(Id, DocumentStatus.Archived, "admin");

        var csv = Encoding.UTF8.GetString((await Files.ExportAsync(new DocumentQuery(Text: "ADM-2022-0310"), "admin", Ct)).Value.Content);

        Assert.Contains(",\"'=HYPERLINK(\"\"http://x\"\")\",", csv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("line\nbreak", "\"line\nbreak\"")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Csv_fields_are_quoted_when_needed(string? value, string expected) =>
        Assert.Equal(expected, ApprovedDocumentFiles.CsvField(value));

    [Fact]
    public async Task Export_fails_rather_than_leaving_out_ocr_jobs_when_the_service_is_down()
    {
        _ocr.Unavailable = true;

        var result = await Files.ExportAsync(new DocumentQuery(), "admin", Ct);

        Assert.Equal(OcrErrorCodes.ServiceUnavailable, result.Error?.Code);
    }

    [Fact]
    public void Export_link_filters_are_read_back_by_the_endpoint()
    {
        var query = new DocumentQuery(" vou ", DocumentSearchField.Title, Department: DocumentDepartment.Legal, Kind: DocumentKind.Contract,
            Confidentiality: DocumentConfidentiality.Secret, ApprovedFrom: new(2026, 1, 2), ApprovedTo: new(2026, 3, 4));

        var url = new Uri("http://localhost" + ApprovedDocumentEndpoints.ExportUrl(query));
        var parsed = ApprovedDocumentEndpoints.ParseQuery(new QueryCollection(QueryHelpers.ParseQuery(url.Query)));

        Assert.Equal(query with { Text = "vou" }, parsed);
        Assert.Equal(new DocumentQuery(Text: string.Empty), ApprovedDocumentEndpoints.ParseQuery(
            new QueryCollection(QueryHelpers.ParseQuery("?department=Nope&from=2026-13-40&field=<script>"))));
    }

    [Fact]
    public void Document_page_shows_the_approver_and_offers_the_final_version()
    {
        var waiting = RenderDocument("DOC-2026-000105");
        Assert.Empty(waiting.FindAll("#final-download, #approved-by"));

        waiting.Find("#action-approve").Click();

        Assert.Equal("admin", waiting.Find("#approved-by").TextContent);
        Assert.Equal(
            ["/documents/DOC-2026-000105/final?format=docx", "/documents/DOC-2026-000105/final?format=txt"],
            waiting.FindAll("#final-download a").Select(a => a.GetAttribute("href")));
    }

    [Fact]
    public async Task Downloads_and_exports_are_described_in_the_activity_log()
    {
        await Files.FinalAsync("DOC-2026-000104", FinalFileFormat.Docx, "admin", Ct);
        await Files.ExportAsync(new DocumentQuery(), "admin", Ct);
        Services.GetRequiredService<NavigationManager>().NavigateTo("admin?tab=Audit");

        var cut = Render<AdminPage>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".audit-table tbody tr").Select(r => r.TextContent).ToList();
            Assert.Contains("Exported the approved list (7 documents)", rows[0], StringComparison.Ordinal);
            Assert.Contains("Downloaded final version 1.1 as DOCX", rows[1], StringComparison.Ordinal);
        });
    }

    private static string[] Names(IRenderedComponent<ApprovedPage> cut) =>
        [.. cut.FindAll("tbody tr td:first-child > a").Select(a => a.TextContent)];

    private IRenderedComponent<DocumentDetailPage> RenderDocument(string id)
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, id));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Markup, StringComparison.Ordinal));
        return cut;
    }
}
