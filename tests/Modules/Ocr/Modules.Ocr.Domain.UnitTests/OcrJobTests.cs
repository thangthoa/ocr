using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Domain.UnitTests;

public sealed class OcrJobTests
{
    [Theory]
    [InlineData("job_3f9c2a1b7d4e5f60")]
    [InlineData("job_b687f8826140409e")]
    public void Valid_job_ids_parse(string value)
    {
        Assert.True(OcrJobId.TryParse(value, out var id));
        Assert.Equal(value, id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3f9c2a1b7d4e5f60")]
    [InlineData("job_")]
    [InlineData("job_../../v1/health")]
    [InlineData("job_abc?api_key=x")]
    [InlineData("job_abc/download")]
    public void Ids_that_could_alter_the_request_path_are_rejected(string? value) =>
        Assert.False(OcrJobId.TryParse(value, out _));

    [Theory]
    [InlineData(OcrJobStatus.Queued, true)]
    [InlineData(OcrJobStatus.Running, false)]
    [InlineData(OcrJobStatus.Done, false)]
    public void Only_queued_jobs_can_be_cancelled(OcrJobStatus status, bool expected) =>
        Assert.Equal(expected, Job(status).CanCancel);

    [Theory]
    [InlineData(OcrJobStatus.Done)]
    [InlineData(OcrJobStatus.Failed)]
    [InlineData(OcrJobStatus.Cancelled)]
    public void Terminal_statuses_are_finished(OcrJobStatus status) => Assert.True(Job(status).IsFinished);

    [Fact]
    public void Visible_change_detection_ignores_identical_state_and_sees_progress()
    {
        var running = Job(OcrJobStatus.Running) with { CurrentStep = "page 1/2: reading (view 1/4)" };

        Assert.True(running.HasVisibleChangesSince(null));
        Assert.False(running.HasVisibleChangesSince(running with { }));
        Assert.True((running with { CurrentStep = "page 1/2: reading (view 2/4)" }).HasVisibleChangesSince(running));
        Assert.True((running with { Status = OcrJobStatus.Done }).HasVisibleChangesSince(running));
    }

    [Theory]
    [InlineData(OcrJobStatus.Queued, true, true)]
    [InlineData(OcrJobStatus.Failed, true, true)]
    [InlineData(OcrJobStatus.Done, true, true)]
    [InlineData(OcrJobStatus.Done, false, false)]
    [InlineData(OcrJobStatus.Running, true, false)]
    [InlineData(OcrJobStatus.Cancelled, true, false)]
    public void Pages_change_while_waiting_after_a_failure_or_once_the_word_file_is_ready(OcrJobStatus status, bool wordReady, bool expected) =>
        Assert.Equal(expected, (Job(status) with { WordReady = wordReady }).CanEditPages);

    [Fact]
    public void Reading_and_building_the_word_file_make_the_job_busy_but_cancelled_is_simply_not_editable()
    {
        Assert.True(Job(OcrJobStatus.Running).IsBusyWithPages);
        Assert.True((Job(OcrJobStatus.Done) with { WordReady = false }).IsBusyWithPages);
        Assert.False(Job(OcrJobStatus.Cancelled).IsBusyWithPages);
        Assert.False(Job(OcrJobStatus.Cancelled).AllowsPageEdits);
    }

    [Fact]
    public void Pages_are_numbered_from_one_and_new_pages_may_go_just_after_the_last()
    {
        var job = Job(OcrJobStatus.Done) with { PagesTotal = 3 };

        Assert.Equal([false, true, true, true, false], [.. Enumerable.Range(0, 5).Select(job.HasPage)]);
        Assert.Equal([false, true, true, true, true, false], [.. Enumerable.Range(0, 6).Select(job.CanInsertAt)]);
    }

    [Fact]
    public void The_only_page_cannot_be_deleted()
    {
        Assert.False((Job(OcrJobStatus.Done) with { PagesTotal = 1 }).CanDeletePage);
        Assert.True((Job(OcrJobStatus.Done) with { PagesTotal = 2 }).CanDeletePage);
    }

    [Fact]
    public void A_duplicate_pair_is_ordered_and_needs_two_different_pages()
    {
        var pair = new OcrPageDuplicate(7, 3, OcrDuplicateKind.Similar);

        Assert.Equal((3, 7), (pair.FirstPage, pair.SecondPage));
        Assert.Equal(new OcrPageDuplicate(3, 7, OcrDuplicateKind.Similar), pair);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrPageDuplicate(2, 2, OcrDuplicateKind.Identical));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OcrPageDuplicate(0, 2, OcrDuplicateKind.Identical));
    }

    [Fact]
    public void Page_changes_word_file_and_duplicates_are_visible_changes()
    {
        var done = Job(OcrJobStatus.Done);

        Assert.False((done with { Duplicates = [new OcrPageDuplicate(1, 2, OcrDuplicateKind.Identical)] })
            .HasVisibleChangesSince(done with { Duplicates = [new OcrPageDuplicate(1, 2, OcrDuplicateKind.Identical)] }));
        Assert.True((done with { Edits = 1 }).HasVisibleChangesSince(done));
        Assert.True((done with { WordReady = false }).HasVisibleChangesSince(done));
        Assert.True((done with { Duplicates = [new OcrPageDuplicate(1, 2, OcrDuplicateKind.SameText)] }).HasVisibleChangesSince(done));
    }

    private static OcrJob Job(OcrJobStatus status) =>
        new(OcrJobId.Parse("job_0123456789abcdef"), "a.png", OcrDocumentType.Printed, status, 2, 0, null, null, null, null, [], null, null);
}
