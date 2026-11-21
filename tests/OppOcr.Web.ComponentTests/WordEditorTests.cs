using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Web;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Mockups.Word;

namespace OppOcr.Web.ComponentTests;

/// <summary>Editing Word files through OnlyOffice Docs (ADR 0004), without a real document server.</summary>
public sealed class WordEditorTests : BunitContext
{
    private const string Secret = "test-secret-0123456789-abcdefghijklmnop";
    private const string Sample = "DOC-2026-000103";

    private static readonly Dictionary<string, string?> EditorOn = new()
    {
        [WordEditorOptions.PublicUrlVariable] = "http://docs.example:8085",
        [WordEditorOptions.InternalUrlVariable] = "http://documentserver",
        [WordEditorOptions.AppUrlVariable] = "http://app:8080",
        [WordEditorOptions.JwtSecretVariable] = Secret,
    };

    private readonly InMemoryOcr _ocr;
    private readonly FakeDocumentServer _server = new();

    public WordEditorTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments(EditorOn);
        Services.AddHttpClient(DocumentServerClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _server);
    }

    private WordEditorService Editor => Services.GetRequiredService<WordEditorService>();

    private IWordFileStore Files => Services.GetRequiredService<IWordFileStore>();

    private WordPreviewService Preview => Services.GetRequiredService<WordPreviewService>();

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public void Docx_text_keeps_pages_and_lays_out_arabic_right_to_left()
    {
        var docx = DocxText.Create(["سطر أول\nسطر ثان", "Second page"]);

        Assert.Equal(["سطر أول\nسطر ثان", "Second page"], DocxText.ExtractPages(new MemoryStream(docx)));
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("<w:bidi />", xml, StringComparison.Ordinal);
        Assert.Contains("w:type=\"page\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Jwt_rejects_tampering_another_secret_and_expiry()
    {
        var now = DateTimeOffset.UtcNow;
        var token = OnlyOfficeJwt.Sign(new { status = 2 }, Secret);

        Assert.True(OnlyOfficeJwt.TryValidate(token, Secret, now, out var payload));
        Assert.Equal(2, payload.GetProperty("status").GetInt32());
        var parts = token.Split('.');
        var forged = $"{parts[0]}.{OnlyOfficeJwt.Sign(new { status = 6 }, Secret).Split('.')[1]}.{parts[2]}";
        Assert.False(OnlyOfficeJwt.TryValidate(forged, Secret, now, out _));
        Assert.False(OnlyOfficeJwt.TryValidate(token, Secret + "x", now, out _));
        Assert.False(OnlyOfficeJwt.TryValidate(OnlyOfficeJwt.Sign(new { exp = now.AddMinutes(-1).ToUnixTimeSeconds() }, Secret), Secret, now, out _));
    }

    [Fact]
    public void File_links_are_bound_to_one_document_and_version()
    {
        var links = Services.GetRequiredService<WordLinks>();
        var query = HttpUtility.ParseQueryString(new Uri(links.FileUrl(Sample, 2)).Query);
        var expires = long.Parse(query["exp"]!, CultureInfo.InvariantCulture);

        Assert.StartsWith($"http://app:8080/documents/{Sample}/word/file?v=2", links.FileUrl(Sample, 2), StringComparison.Ordinal);
        Assert.True(links.IsValidFileLink(Sample, 2, expires, query["sig"]));
        Assert.False(links.IsValidFileLink(Sample, 1, expires, query["sig"]));
        Assert.False(links.IsValidFileLink("DOC-2026-000104", 2, expires, query["sig"]));
        Assert.False(links.IsValidFileLink(Sample, 2, expires + 60, query["sig"]));
        Assert.False(links.IsValidFileLink(Sample, 2, DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), query["sig"]));
    }

    [Fact]
    public async Task Disk_store_keeps_versions_and_skips_an_identical_save()
    {
        var first = await Files.SaveAsync(Sample, new MemoryStream([1, 2, 3]), "admin", WordVersionSource.Generated, Ct);
        var same = await Files.SaveAsync(Sample, new MemoryStream([1, 2, 3]), "admin", WordVersionSource.Edited, Ct);
        var second = await Files.SaveAsync(Sample, new MemoryStream([4, 5]), "admin", WordVersionSource.Edited, Ct);

        Assert.Equal((1, true), (first.Version.Number, first.Created));
        Assert.Equal((1, false), (same.Version.Number, same.Created));
        Assert.Equal((2, true), (second.Version.Number, second.Created));
        Assert.Equal([1, 2], (await Files.ListAsync(Sample, Ct)).Select(v => v.Number));
        await using var stream = (await Files.OpenAsync(Sample, 2, Ct))!;
        Assert.Equal(2, stream.Length);
        Assert.Null(await Files.OpenAsync(Sample, 3, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Files.ListAsync("../etc", Ct));
    }

    [Fact]
    public async Task Opening_a_sample_starts_from_its_text_with_a_signed_configuration()
    {
        var result = await Editor.OpenAsync(Sample, "admin", CultureInfo.GetCultureInfo("ar"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(WordVersionSource.Generated, result.Value.Version.Source);
        Assert.Equal("http://docs.example:8085/web-apps/apps/api/documents/api.js", result.Value.ApiScriptUrl);
        Assert.True(OnlyOfficeJwt.TryValidate((string)result.Value.Config["token"]!, Secret, DateTimeOffset.UtcNow, out var payload));
        var document = payload.GetProperty("document");
        Assert.StartsWith($"http://app:8080/documents/{Sample}/word/file?v=1", document.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.StartsWith($"{Sample}-1-", document.GetProperty("key").GetString(), StringComparison.Ordinal);
        Assert.Equal("ar", payload.GetProperty("editorConfig").GetProperty("lang").GetString());
        await using var file = (await Files.OpenAsync(Sample, 1, Ct))!;
        Assert.Contains("عقد صيانة", DocxText.ExtractPages(file)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_a_finished_job_starts_from_the_ocr_docx()
    {
        var page = new OcrPage(1, OcrDocumentType.Printed, "نص", 5, "A v2", [], Cached: false);
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000005", "scan.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] });
        _ocr.Docx = DocxText.Create(["من خدمة OCR"]);
        // Keep the job in review: one that passes the automatic check is archived, and archived documents are final.
        Services.GetRequiredService<QualitySettingsStore>().Save(QualitySettings.Default with { AutoApprove = false }, "admin");

        var result = await Editor.OpenAsync("job_0000000000000005", "admin", CultureInfo.GetCultureInfo("en"), Ct);

        Assert.Equal(WordVersionSource.Ocr, result.Value.Version.Source);
        Assert.Equal("scan.docx", ((Dictionary<string, object?>)result.Value.Config["document"]!)["title"]);
    }

    [Fact]
    public async Task The_editor_is_not_opened_while_the_document_server_is_starting()
    {
        _server.Ready = false;

        var result = await Editor.OpenAsync(Sample, "admin", CultureInfo.GetCultureInfo("en"), Ct);

        Assert.Equal(WordErrors.ServerNotReady, result.Error!.Code);
        Assert.Empty(await Files.ListAsync(Sample, Ct));
    }

    [Fact]
    public async Task Archived_documents_cannot_be_opened_for_editing()
    {
        var result = await Editor.OpenAsync("DOC-2026-000101", "admin", CultureInfo.GetCultureInfo("en"), Ct);

        Assert.Equal(WordErrors.NotEditable, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task A_saved_edit_becomes_a_word_version_the_document_text_and_an_audit_entry()
    {
        await Editor.OpenAsync(Sample, "admin", CultureInfo.GetCultureInfo("en"), Ct);
        _server.File = DocxText.Create(["نص بعد التعديل"]);

        var outcome = await Editor.HandleCallbackAsync(Sample, CallbackSignature(), Callback(6, "http://docs.example:8085/cache/files/out.docx?md5=1"), null, Ct);

        Assert.Equal(WordCallbackOutcome.Accepted, outcome);
        Assert.Equal("http://documentserver/cache/files/out.docx?md5=1", Assert.Single(_server.Requests).AbsoluteUri);
        var versions = await Files.ListAsync(Sample, Ct);
        Assert.Equal([WordVersionSource.Generated, WordVersionSource.Edited], versions.Select(v => v.Source));
        var document = Services.GetRequiredService<DocumentStore>().FindSample(Sample)!;
        Assert.Equal(["نص بعد التعديل"], document.PageTexts);
        Assert.Equal((DocumentVersionChange.EditedInWord, 2), (document.CurrentVersion!.Change, document.CurrentVersion.WordVersion));
        var entry = Services.GetRequiredService<AuditLog>().Latest()[0];
        Assert.Equal((AuditAction.EditedInWord, "2"), (entry.Action, entry.Version));
    }

    [Fact]
    public async Task The_same_content_reported_again_on_close_is_not_a_new_version()
    {
        await Editor.OpenAsync(Sample, "admin", CultureInfo.GetCultureInfo("en"), Ct);
        _server.File = DocxText.Create(["نص"]);

        await Editor.HandleCallbackAsync(Sample, CallbackSignature(), Callback(6, "http://documentserver/a.docx"), null, Ct);
        await Editor.HandleCallbackAsync(Sample, CallbackSignature(), Callback(2, "http://documentserver/b.docx"), null, Ct);

        Assert.Equal(2, (await Files.ListAsync(Sample, Ct)).Count);
    }

    [Fact]
    public async Task Callbacks_with_a_wrong_link_or_token_are_refused()
    {
        var forgedToken = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            status = 2,
            url = "http://documentserver/a.docx",
            token = OnlyOfficeJwt.Sign(new { status = 2, url = "http://documentserver/a.docx" }, "another-secret-0123456789-abcdefghij"),
        })).RootElement;

        Assert.Equal(WordCallbackOutcome.Forbidden, await Editor.HandleCallbackAsync(Sample, "bad", Callback(2, "http://documentserver/a.docx"), null, Ct));
        Assert.Equal(WordCallbackOutcome.Forbidden, await Editor.HandleCallbackAsync(Sample, CallbackSignature(), forgedToken, null, Ct));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Edited_files_are_only_fetched_from_the_document_server()
    {
        await Assert.ThrowsAsync<DocumentServerException>(() =>
            Editor.HandleCallbackAsync(Sample, CallbackSignature(), Callback(2, "http://intranet.example/secret"), null, Ct));
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public void Document_page_offers_word_editing_for_editable_documents()
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, Sample));

        // Edit opens Word, next to Approve in the progress band; there is no separate text editor.
        cut.WaitForAssertion(() => Assert.Equal($"documents/{Sample}/word", cut.Find(".doc-band-actions #action-edit").GetAttribute("href")));
        Assert.Equal("Edit", cut.Find("#action-edit").TextContent);
        Assert.NotNull(cut.Find(".doc-band-actions #action-approve"));
        Assert.Equal(["DOCX", "PDF", "TXT"], cut.FindAll("#download-format option").Select(o => o.TextContent));
    }

    [Fact]
    public async Task Editor_page_explains_a_blocked_editor_and_clears_the_warning_once_it_starts()
    {
        var module = JSInterop.SetupModule("./js/word-editor.js");
        module.SetupVoid("open", _ => true).SetVoidResult();

        var cut = Render<WordEditorPage>(p => p.Add(x => x.Id, Sample));

        cut.WaitForAssertion(() => Assert.Contains("Word file version 1", cut.Markup, StringComparison.Ordinal));
        // The editor is opened right after that render; queueing behind it lets that finish first.
        await cut.InvokeAsync(() => { });
        var open = Assert.Single(module.Invocations["open"]);
        Assert.Equal((int)WordEditorPage.StartTimeout.TotalMilliseconds, open.Arguments[4]);
        Assert.Empty(cut.FindAll("#word-stalled"));

        await cut.InvokeAsync(cut.Instance.EditorStalled);
        Assert.Contains("docs.example:8085", cut.Find("#word-stalled").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("d-none", cut.Find(".word-frame").ClassName, StringComparison.Ordinal);

        await cut.InvokeAsync(cut.Instance.EditorStarted);
        Assert.Empty(cut.FindAll("#word-stalled"));
    }

    [Fact]
    public async Task Word_file_is_rendered_once_per_version_into_ordered_page_images()
    {
        _server.File = PageZip("image2.png", "image10.png", "image1.png");
        var document = await DocumentAsync(Sample);

        var first = await Preview.GetAsync(document, Ct);
        var again = await Preview.GetAsync(document, Ct);

        Assert.Equal(3, first.Value.PageCount);
        Assert.Equal(1, first.Value.Version.Number);
        Assert.Equal(3, again.Value.PageCount);
        var conversion = Assert.Single(_server.Conversions);
        Assert.Equal("png", conversion.GetProperty("outputtype").GetString());
        Assert.False(conversion.GetProperty("thumbnail").GetProperty("first").GetBoolean());
        Assert.StartsWith($"http://app:8080/documents/{Sample}/word/file?", conversion.GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.True(OnlyOfficeJwt.TryValidate(conversion.GetProperty("token").GetString()!, Secret, DateTimeOffset.UtcNow, out var signed));
        Assert.Equal(conversion.GetProperty("url").GetString(), signed.GetProperty("url").GetString());
        int[] markers = [await PageMarkerAsync(1), await PageMarkerAsync(2), await PageMarkerAsync(3)];
        Assert.Equal([1, 2, 10], markers);
    }

    [Fact]
    public async Task Word_preview_failures_keep_nothing_and_never_fetch_outside_the_document_server()
    {
        var document = await DocumentAsync(Sample);

        _server.Ready = false;
        var notReady = await Preview.GetAsync(document, Ct);
        _server.Ready = true;
        _server.ConverterAnswer = """{"error":-4}""";
        var refused = await Preview.GetAsync(document, Ct);
        _server.ConverterAnswer = """{"endConvert":true,"fileUrl":"http://intranet.example/secret.zip"}""";
        var foreign = await Preview.GetAsync(document, Ct);
        _server.ConverterAnswer = FakeDocumentServer.Converted;
        _server.File = "not an image"u8.ToArray();
        var notImages = await Preview.GetAsync(document, Ct);

        Assert.Equal(WordErrors.ServerNotReady, notReady.Error!.Code);
        Assert.All([refused, foreign, notImages], r => Assert.Equal(WordErrors.PreviewFailed, r.Error!.Code));
        Assert.DoesNotContain(_server.Requests, uri => uri.Host == "intranet.example");
        Assert.Null(await Files.PreviewPageCountAsync(Sample, 1, Ct));
    }

    [Fact]
    public async Task Documents_still_in_ocr_have_no_word_preview()
    {
        _ocr.SetOnService(InMemoryOcr.NewJob("job_0000000000000007", "scan.png", OcrJobStatus.Running));

        var result = await Preview.GetAsync(await DocumentAsync("job_0000000000000007"), Ct);

        Assert.Equal(WordErrors.NoWordFile, result.Error!.Code);
        Assert.Empty(_server.Conversions);
    }

    [Fact]
    public async Task Document_page_shows_each_original_page_next_to_the_same_word_page()
    {
        const string id = "job_0000000000000008";
        var page = new OcrPage(1, OcrDocumentType.Printed, "نص الصفحة", 5, "A v2", [], Cached: false);
        _ocr.SetOnService(InMemoryOcr.NewJob(id, "scan.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page] });
        _ocr.Docx = DocxText.Create(["نص الصفحة"]);
        _server.File = PageZip("image1.png");

        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, id));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal($"/ocr/jobs/{id}/pages/1/image", cut.Find("#pane-original img.page-image").GetAttribute("src"));
            Assert.Equal($"/documents/{id}/word/preview/1/1", cut.Find("#pane-result img.word-page").GetAttribute("src"));
            Assert.Empty(cut.FindAll("#word-latest"));
        });
        // Awaited: the preview's own render may still be finishing when the click is queued.
        await cut.Find("#show-text").ClickAsync(new MouseEventArgs());
        Assert.Equal("نص الصفحة", cut.Find("#pane-result .page-text").TextContent);
        Assert.Empty(cut.FindAll("#pane-result img"));
    }

    [Fact]
    public async Task Document_page_shows_the_text_while_the_word_file_cannot_be_rendered_and_retries()
    {
        _server.Ready = false;
        _server.File = PageZip("image1.png");

        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, Sample));

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#preview-retry")));
        Assert.NotEmpty(cut.Find("#pane-result .page-text").TextContent);

        _server.Ready = true;
        await cut.Find("#preview-retry").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal($"/documents/{Sample}/word/preview/1/1", cut.Find("#pane-result img.word-page").GetAttribute("src")));
        Assert.Empty(cut.FindAll("#preview-retry"));
    }

    [Fact]
    public async Task After_a_page_change_the_new_ocr_word_file_is_taken_once_the_service_has_finished_it()
    {
        const string id = "job_0000000000000009";
        Services.GetRequiredService<QualitySettingsStore>().Save(QualitySettings.Default with { AutoApprove = false }, "admin");
        OcrPage[] pages = [new(1, OcrDocumentType.Printed, "الأولى", 5, "A v2", [], false), new(2, OcrDocumentType.Printed, "الثانية", 5, "A v2", [], false)];
        _ocr.Seed(InMemoryOcr.NewJob(id, "scan.pdf", OcrJobStatus.Done) with { PagesTotal = 2, PagesDone = 2, Pages = pages });
        _ocr.Docx = DocxText.Create(["الأولى", "الثانية"]);
        var first = await Editor.CurrentVersionAsync(await DocumentAsync(id), Ct);

        var deleted = await Services.GetRequiredService<DocumentPageEditor>().DeleteAsync(id, 2, "admin", Ct);

        // The service is rebuilding its Word file: neither the preview nor the editor takes the old one meanwhile.
        Assert.True(WordEditorService.IsWordPending(deleted.Value));
        Assert.Equal(WordErrors.WordPending, (await Preview.GetAsync(deleted.Value, Ct)).Error!.Code);
        Assert.Equal(WordErrors.WordPending, (await Editor.OpenAsync(id, "admin", CultureInfo.InvariantCulture, Ct)).Error!.Code);

        _ocr.SetOnService(_ocr.OnService(id) with { WordReady = true });
        _ocr.Docx = DocxText.Create(["الأولى"]);
        var document = await DocumentAsync(id);
        var latest = await Editor.CurrentVersionAsync(document, Ct);

        Assert.Equal((first.Number + 1, WordVersionSource.Ocr), (latest.Number, latest.Source));
        Assert.Equal(latest.Number, (await DocumentAsync(id)).CurrentVersion!.WordVersion);
        // Taken once: later reads keep that version.
        Assert.Equal(latest.Number, (await Editor.CurrentVersionAsync(await DocumentAsync(id), Ct)).Number);
        Assert.Equal(2, (await Files.ListAsync(id, Ct)).Count);
    }

    [Fact]
    public async Task A_job_just_done_waits_for_the_services_complete_word_file()
    {
        const string id = "job_0000000000000010";
        var page = new OcrPage(1, OcrDocumentType.Printed, "نص", 5, "A v2", [], false);
        _ocr.Seed(InMemoryOcr.NewJob(id, "scan.png", OcrJobStatus.Done) with { PagesDone = 1, Pages = [page], WordReady = false });
        _ocr.Docx = DocxText.Create(["نص"]);

        var result = await Preview.GetAsync(await DocumentAsync(id), Ct);

        // Taking the file now would keep it without its logo, signature and stamp images for good.
        Assert.Equal(WordErrors.WordPending, result.Error!.Code);
        Assert.Empty(await Files.ListAsync(id, Ct));
    }

    private async Task<DocumentRecord> DocumentAsync(string id) =>
        (await Services.GetRequiredService<DocumentCatalog>().GetAsync(id, Ct)).Value;

    /// <summary>The last byte of a kept page image, which <see cref="PageZip"/> takes from the page's name.</summary>
    private async Task<int> PageMarkerAsync(int page)
    {
        await using var stream = (await Files.OpenPreviewPageAsync(Sample, 1, page, Ct))!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        return copy.ToArray()[^1];
    }

    /// <summary>A conversion result: one tiny "PNG" per <c>imageN.png</c> name, ending with the byte N.</summary>
    private static byte[] PageZip(params string[] names)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in names)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, byte.Parse(name.AsSpan(5, name.Length - 9), CultureInfo.InvariantCulture)]);
            }
        }

        return buffer.ToArray();
    }

    private string CallbackSignature() =>
        HttpUtility.ParseQueryString(new Uri(Services.GetRequiredService<WordLinks>().CallbackUrl(Sample)).Query)["sig"]!;

    private static JsonElement Callback(int status, string url)
    {
        var payload = new { key = "k", status, url, users = new[] { "admin" } };
        return JsonDocument.Parse(JsonSerializer.Serialize(new { payload.key, payload.status, payload.url, payload.users, token = OnlyOfficeJwt.Sign(payload, Secret) })).RootElement;
    }


    [Fact]
    public async Task An_archived_document_downloads_as_a_pdf_printed_by_the_document_server()
    {
        Services.GetRequiredService<DocumentStore>().ChangeStatus(Sample, DocumentStatus.Archived, "admin");
        _server.File = "%PDF-1.7 final"u8.ToArray();
        var files = Services.GetRequiredService<OppOcr.Web.Features.Mockups.Approved.ApprovedDocumentFiles>();

        var result = await files.FinalAsync(Sample, OppOcr.Web.Features.Mockups.Approved.FinalFileFormat.Pdf, "admin", Ct);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Equal("%PDF-1.7 final"u8.ToArray(), result.Value.Content);
        Assert.EndsWith(".pdf", result.Value.FileName, StringComparison.Ordinal);
        var conversion = Assert.Single(_server.Conversions);
        Assert.Equal("pdf", conversion.GetProperty("outputtype").GetString());

        // The text was never edited in Word, so the document server reads the final text through a signed link.
        var source = new Uri(conversion.GetProperty("url").GetString()!);
        Assert.Equal("/documents/DOC-2026-000103/text.docx", source.AbsolutePath);
        var query = HttpUtility.ParseQueryString(source.Query);
        var links = Services.GetRequiredService<WordLinks>();
        var expires = long.Parse(query["exp"]!, CultureInfo.InvariantCulture);
        Assert.True(links.IsValidTextDocxLink(Sample, expires, query["sig"]));
        Assert.False(links.IsValidTextDocxLink("DOC-2026-000104", expires, query["sig"]));
        Assert.False(links.IsValidFileLink(Sample, 0, expires, query["sig"]));
        Assert.Equal("PDF", Assert.Single(Services.GetRequiredService<AuditLog>().Latest(), e => e.Action == AuditAction.Downloaded).Detail);
    }

    [Fact]
    public async Task A_pdf_that_the_document_server_cannot_make_is_an_error_and_not_a_download()
    {
        Services.GetRequiredService<DocumentStore>().ChangeStatus(Sample, DocumentStatus.Archived, "admin");
        var files = Services.GetRequiredService<OppOcr.Web.Features.Mockups.Approved.ApprovedDocumentFiles>();
        _server.File = "<html>not a pdf</html>"u8.ToArray();

        var result = await files.FinalAsync(Sample, OppOcr.Web.Features.Mockups.Approved.FinalFileFormat.Pdf, "admin", Ct);

        Assert.Equal(OppOcr.Web.Features.Mockups.Approved.ApprovedErrors.PdfFailedCode, result.Error?.Code);
        Assert.DoesNotContain(Services.GetRequiredService<AuditLog>().Latest(), e => e.Action == AuditAction.Downloaded);
    }

    [Fact]
    public void With_a_document_server_the_archive_offers_docx_pdf_and_txt()
    {
        var cut = Render<ApprovedPage>();
        cut.WaitForElement("tbody tr");

        Assert.Equal(["DOCX", "PDF", "TXT"], cut.Find("tbody tr .download-panel").QuerySelectorAll("a").Select(a => a.TextContent));
        Assert.Equal("/documents/DOC-2026-000112/final?format=pdf", cut.Find("tbody tr .download-pdf").GetAttribute("href"));
    }
    private sealed class FakeDocumentServer : HttpMessageHandler
    {
        public const string Converted = """{"endConvert":true,"fileType":"zip","fileUrl":"http://documentserver/cache/files/output.zip","percent":100}""";

        public byte[] File { get; set; } = [];

        public List<Uri> Requests { get; } = [];

        /// <summary>Bodies posted to the conversion service.</summary>
        public List<JsonElement> Conversions { get; } = [];

        public string ConverterAnswer { get; set; } = Converted;

        public bool Ready { get; set; } = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/healthcheck")
            {
                return Ready
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("true") }
                    : new HttpResponseMessage(HttpStatusCode.BadGateway);
            }

            Requests.Add(request.RequestUri!);
            if (request.RequestUri.AbsolutePath == "/converter")
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
                Conversions.Add(body.RootElement.Clone());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ConverterAnswer) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File) };
        }
    }
}
