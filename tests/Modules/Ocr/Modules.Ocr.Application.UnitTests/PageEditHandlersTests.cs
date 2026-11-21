using Microsoft.Extensions.Logging.Abstractions;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Application.Features.DeleteOcrPage;
using Modules.Ocr.Application.Features.InsertOcrPages;
using Modules.Ocr.Application.Features.ReplaceOcrPage;
using Modules.Ocr.Application.Features.UploadOcrPages;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.UnitTests;

/// <summary>Inserting, replacing and deleting pages of a job (API doc §3.8).</summary>
public sealed class PageEditHandlersTests
{
    private const string Id = "job_0000000000000001";
    private readonly FakeApi _api = new();
    private readonly FakeRegistry _registry = new();
    private readonly FakeNotifier _notifier = new();

    public PageEditHandlersTests() => _api.Jobs[Id] = Done(pages: 10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private InsertOcrPagesHandler Insert =>
        new(new InsertOcrPagesValidator(), _api, _registry, _notifier, NullLogger<InsertOcrPagesHandler>.Instance);

    private ReplaceOcrPageHandler Replace =>
        new(new ReplaceOcrPageValidator(), _api, _registry, _notifier, NullLogger<ReplaceOcrPageHandler>.Instance);

    private DeleteOcrPageHandler Delete => new(_api, _registry, _notifier, NullLogger<DeleteOcrPageHandler>.Instance);

    [Fact]
    public async Task Inserted_files_are_checked_then_sent_in_order_at_the_position_and_the_job_is_followed_again()
    {
        var png = TestJobs.Png();
        var pdf = TestJobs.Pdf();

        var result = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("trang_05.png", png), File("phu_luc.pdf", pdf)], 5), Ct);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(_api.PageEdits);
        Assert.Equal(("insert", Id, (int?)5), (sent.Method, sent.JobId, sent.Page));
        Assert.Equal(["trang_05.png", "phu_luc.pdf"], sent.Files.Select(f => f.FileName));
        Assert.Equal(png, sent.Files[0].Content.ToArray());
        Assert.Equal(pdf, sent.Files[1].Content.ToArray());

        // Submitted elsewhere or before a restart: followed from now on, so its end is announced.
        var tracked = Assert.Single(_registry.All);
        Assert.Equal((OcrJobStatus.Queued, 1), (tracked.Status, tracked.Edits));
        var change = Assert.Single(_notifier.Changes);
        Assert.Equal((OcrJobChangeKind.Updated, OcrJobStatus.Queued), (change.Kind, change.Status));
    }

    [Fact]
    public async Task Inserting_without_a_position_adds_after_the_last_page()
    {
        await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.png", TestJobs.Png())], null), Ct);

        Assert.Null(Assert.Single(_api.PageEdits).Page);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    public async Task A_position_outside_the_pages_is_refused(int position)
    {
        var result = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.png", TestJobs.Png())], position), Ct);

        Assert.Equal(OcrErrorCodes.PageNotFound, result.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task Inserting_more_pages_than_a_document_may_have_is_refused_before_anything_is_sent()
    {
        _api.Jobs[Id] = Done(pages: OcrRules.MaxPagesPerDocument);

        var result = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.png", TestJobs.Png())], null), Ct);

        Assert.Equal(OcrErrorCodes.TooManyPages, result.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task An_inserted_file_whose_content_does_not_match_its_extension_is_refused()
    {
        var result = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.png", TestJobs.Pdf())], 1), Ct);

        Assert.Equal(OcrErrorCodes.ContentMismatch, result.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task Inserting_needs_supported_files()
    {
        var none = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [], null), Ct);
        var gif = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.gif", [0x47, 0x49, 0x46])], null), Ct);

        Assert.Equal(OcrErrorCodes.PagesRequired, none.Error?.Code);
        Assert.Equal(OcrErrorCodes.UnsupportedFormat, gif.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task Files_larger_than_they_claimed_are_cut_off_at_the_limit()
    {
        var large = new byte[OcrRules.MaxSizeBytes + 1];
        TestJobs.Png().CopyTo(large, 0);

        var result = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [new OcrPageFile("a.png", 10, new MemoryStream(large))], null), Ct);

        Assert.Equal(OcrErrorCodes.TooLarge, result.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Theory]
    [InlineData(OcrJobStatus.Running, true)]
    [InlineData(OcrJobStatus.Done, false)]
    public async Task While_the_service_reads_or_builds_the_word_file_pages_wait(OcrJobStatus status, bool wordReady)
    {
        _api.Jobs[Id] = Done(pages: 10) with { Status = status, WordReady = wordReady };

        var insert = await Insert.HandleAsync(new InsertOcrPagesCommand(Id, [File("a.png", TestJobs.Png())], null), Ct);
        var replace = await Replace.HandleAsync(new ReplaceOcrPageCommand(Id, 5, File("a.png", TestJobs.Png())), Ct);
        var delete = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 7), Ct);

        Assert.All([insert, replace, delete], r => Assert.Equal(OcrErrors.JobBusy, r.Error));
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task The_pages_of_a_cancelled_job_do_not_change()
    {
        _api.Jobs[Id] = Done(pages: 10) with { Status = OcrJobStatus.Cancelled };

        var result = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 2), Ct);

        Assert.Equal(OcrErrors.PagesNotEditable, result.Error);
    }

    [Fact]
    public async Task A_queued_job_can_lose_a_page_before_it_is_read()
    {
        _api.Jobs[Id] = Done(pages: 10) with { Status = OcrJobStatus.Queued, WordReady = false };

        var result = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 7), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(("delete", (int?)7), (_api.PageEdits[0].Method, _api.PageEdits[0].Page));
        Assert.Equal(9, result.Value.PagesTotal);
    }

    [Fact]
    public async Task A_page_is_replaced_with_the_new_image()
    {
        var jpeg = TestJobs.Jpeg();

        var result = await Replace.HandleAsync(new ReplaceOcrPageCommand(Id, 5, File("trang_05_scan_lai.jpg", jpeg)), Ct);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(_api.PageEdits);
        Assert.Equal(("replace", (int?)5, "trang_05_scan_lai.jpg"), (sent.Method, sent.Page, Assert.Single(sent.Files).FileName));
        Assert.Equal(jpeg, sent.Files[0].Content.ToArray());
        Assert.Equal(OcrJobStatus.Queued, Assert.Single(_notifier.Changes).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task Only_existing_pages_can_be_replaced_or_deleted(int page)
    {
        var replace = await Replace.HandleAsync(new ReplaceOcrPageCommand(Id, page, File("a.png", TestJobs.Png())), Ct);
        var delete = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, page), Ct);

        Assert.Equal(OcrErrorCodes.PageNotFound, replace.Error?.Code);
        Assert.Equal(OcrErrorCodes.PageNotFound, delete.Error?.Code);
        Assert.Empty(_api.PageEdits);
    }

    [Fact]
    public async Task A_deleted_page_is_gone_at_once_and_the_job_stays_done()
    {
        var result = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 7), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((9, 1, OcrJobStatus.Done), (result.Value.PagesTotal, result.Value.Edits, result.Value.Status));
        Assert.Equal(9, Assert.Single(_registry.All).PagesTotal);
        // Nothing to announce: a toast would read "OCR finished" although nothing was read.
        Assert.Empty(_notifier.Changes);
    }

    [Fact]
    public async Task The_only_page_is_not_deleted()
    {
        _api.Jobs[Id] = Done(pages: 1);

        var result = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 1), Ct);

        Assert.Equal(OcrErrors.LastPage, result.Error);
        Assert.Empty(_api.PageEdits);
    }

    [Theory]
    [InlineData("job_busy", ErrorType.Conflict, OcrErrorCodes.JobBusy)]
    [InlineData("last_page", ErrorType.Conflict, OcrErrorCodes.LastPage)]
    [InlineData("not_found", ErrorType.NotFound, OcrErrorCodes.NotFound)]
    [InlineData("service_unavailable", ErrorType.Failure, OcrErrorCodes.ServiceUnavailable)]
    [InlineData("too_many_pages", ErrorType.Validation, "too_many_pages")]
    public async Task The_services_refusal_is_reported_in_the_modules_terms(string code, ErrorType type, string expected)
    {
        // The job changed between the check and the request, or the service counted the PDF's pages.
        _api.PageEditResult = () => new Error(code, "refused", type);

        var result = await Delete.HandleAsync(new DeleteOcrPageCommand(Id, 7), Ct);

        Assert.Equal(expected, result.Error?.Code);
        Assert.Empty(_registry.All);
        Assert.Empty(_notifier.Changes);
    }

    [Fact]
    public async Task An_unknown_job_is_not_found()
    {
        var result = await Delete.HandleAsync(new DeleteOcrPageCommand("job_9999999999999999", 1), Ct);
        var malformed = await Replace.HandleAsync(new ReplaceOcrPageCommand("../v1/health", 1, File("a.png", TestJobs.Png())), Ct);

        Assert.Equal(OcrErrors.NotFound, result.Error);
        Assert.Equal(OcrErrors.NotFound, malformed.Error);
    }

    private static OcrJob Done(int pages) => TestJobs.Job(Id, OcrJobStatus.Done) with { PagesTotal = pages, PagesDone = pages };

    private static OcrPageFile File(string name, byte[] content) => new(name, content.Length, new MemoryStream(content));
}
