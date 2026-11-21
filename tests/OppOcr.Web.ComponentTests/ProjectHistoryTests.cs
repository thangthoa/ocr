using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Activity;
using OppOcr.Web.Features.Mockups.Approved;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Pages;
using OppOcr.Web.Features.Mockups.Quality;

namespace OppOcr.Web.ComponentTests;

/// <summary>
/// The simulated history of the project (ADR 0008): 1,093,911 documents before today that every page counts and
/// lists the same way. The samples are off here, so the history is the whole project.
/// </summary>
public sealed class ProjectHistoryTests : BunitContext
{
    public ProjectHistoryTests()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments(new Dictionary<string, string?>
        {
            [TestServices.HistoryEnabled] = "true",
            [TestServices.SamplesEnabled] = "false",
        });
        Services.AddHealthChecks();
    }

    private static DateOnly Today => DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);

    private static DateOnly FirstDay => ProjectHistory.FirstDay(Today);

    private ProjectHistory History => Services.GetRequiredService<ProjectHistory>();

    private DocumentCatalog Catalog => Services.GetRequiredService<DocumentCatalog>();

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    private Task<DocumentSnapshot> Snapshot() => Catalog.SnapshotAsync(CancellationToken.None);

    [Fact]
    public async Task The_project_has_processed_1093911_documents_before_today()
    {
        var snapshot = await Snapshot();

        Assert.Equal(1_093_911, snapshot.Total);
        Assert.Equal(1_093_911, snapshot.Facts.Sum(FirstDay, Today.AddDays(-1)).Documents);
        Assert.Equal(0, snapshot.Facts.Sum(Today, Today).Documents);
        Assert.Equal(snapshot.Total, snapshot.StatusCounts.Values.Sum());
        Assert.Equal(0, snapshot.StatusCounts[DocumentStatus.Processing]);
        // About one page a document: the 2,000,000-page plan is a little over half done after 45 of its 100 days.
        var pages = snapshot.Facts.AllTime.Pages;
        Assert.InRange(pages / (double)snapshot.Total, 1.05, 1.2);
    }

    [Fact]
    public void The_samples_are_part_of_the_project_too()
    {
        var history = new ProjectHistory(Options.Create(new HistoryOptions()), Options.Create(new DocumentSampleOptions { Enabled = true }),
            TimeProvider.System);

        Assert.Equal(HistoryProfile.Documents, history.Count + SampleDocumentSeed.Create().Count);
    }

    [Fact]
    public async Task The_review_queue_is_what_was_scanned_after_the_reviewers_left_yesterday()
    {
        var waiting = (await Snapshot()).Facts.WaitingReview;

        // Above the 300 target and within the 500 alert level of the review backlog KPI.
        Assert.InRange(waiting, 301, 500);
        var lastDay = (HistoryProfile.Days - 1) * HistoryProfile.SecondsPerDay;
        var queue = Enumerable.Range(0, History.Count).Where(i => History.Entry(i).Status == DocumentStatus.NeedsReview).ToList();
        Assert.Equal(waiting, queue.Count);
        // The reviewers clear the queue every day: nothing waits from before yesterday.
        Assert.All(queue, i => Assert.True(History.Entry(i).Uploaded >= lastDay, $"{HistoryText.Id(i)} waits from an earlier day."));
    }

    [Fact]
    public void Reviewers_approve_in_their_shift_what_they_may_open()
    {
        for (var index = 0; index < History.Count; index += 7)
        {
            var entry = History.Entry(index);
            if (!entry.ApprovedByReviewer)
                continue;

            var reviewer = HistoryProfile.Reviewers[entry.Approver];
            var second = entry.Approved % HistoryProfile.SecondsPerDay;
            Assert.True(entry.Approved > entry.Checked, $"{HistoryText.Id(index)} approved before it was checked.");
            Assert.InRange(second, HistoryProfile.ReviewStartSecond, HistoryProfile.ReviewEndSecond + 240);
            Assert.True(reviewer.CanSee(entry.Department, entry.Confidentiality), $"{reviewer.UserName} may not open {HistoryText.Id(index)}.");
        }
    }

    [Fact]
    public void The_people_of_the_history_have_accounts_with_the_same_access()
    {
        var accounts = Services.GetRequiredService<UserDirectory>();

        foreach (var op in HistoryProfile.Operators)
        {
            var account = accounts.Find(op.UserName);
            Assert.NotNull(account);
            Assert.Equal(UserRole.CaptureOperator, account.Role);
            Assert.Equal(op.LastDay is null, account.IsActive);
        }

        foreach (var reviewer in HistoryProfile.Reviewers)
        {
            var account = accounts.Find(reviewer.UserName);
            Assert.NotNull(account);
            Assert.Equal((UserRole.Reviewer, reviewer.Clearance), (account.Role, account.Clearance));
            Assert.Equal(reviewer.Departments.Order(), account.Departments.Order());
        }

        // The disabled operator scanned until the account was disabled, 40 days ago.
        var lastUpload = Enumerable.Range(0, History.Count).Where(i => History.Entry(i).Uploader == 2).Max(i => History.Entry(i).Uploaded);
        Assert.True(lastUpload / HistoryProfile.SecondsPerDay <= HistoryProfile.Days - 41);
    }

    [Fact]
    public void A_corrected_history_document_has_its_ocr_text_the_word_edit_and_the_approval()
    {
        var index = Enumerable.Range(0, History.Count).First(i => History.Entry(i) is { ApprovedByReviewer: true, Edited: true });
        var entry = History.Entry(index);

        var document = Assert.IsType<DocumentRecord>(Store.FindSample(HistoryText.Id(index)));

        Assert.True(document.IsSample);
        Assert.Equal(DocumentStatus.Archived, document.Status);
        Assert.Equal(HistoryProfile.Operators[entry.Uploader].UserName, document.UploadedBy);
        Assert.Equal(
            [(DocumentStore.OcrAuthor, DocumentVersionChange.OcrResult), (HistoryProfile.Reviewers[entry.Approver].UserName, DocumentVersionChange.EditedInWord)],
            document.Versions.Select(v => (v.Author, v.Change)));
        Assert.NotEqual(document.Versions[0].PageTexts, document.Versions[1].PageTexts);
        Assert.Equal(HistoryProfile.Reviewers[entry.Approver].UserName, document.Approval!.By);
        Assert.False(document.Quality!.Passed);

        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, document.Id));
        cut.WaitForAssertion(() => Assert.Equal(document.FileName, cut.Find("h1").TextContent));
        Assert.Equal(document.Approval.By, cut.Find("#approved-by").TextContent);
    }

    [Fact]
    public void The_check_of_a_history_document_gives_its_status()
    {
        for (var index = 0; index < History.Count; index += 997)
        {
            var entry = History.Entry(index);
            var document = History.Materialize(index);

            Assert.Equal(entry.Status, document.Status);
            if (entry.Status == DocumentStatus.Failed)
            {
                Assert.Null(document.Quality);
                continue;
            }

            Assert.Equal(entry.Reason is null, document.Quality!.Passed);
            Assert.Equal(entry.Reason, (document.Quality.Issues.Count == 0 ? (QualityIssueKind?)null : document.Quality.Issues[0].Kind));
            Assert.Equal(entry.LowPages, document.PageConfidences!.Count(c => c < QualitySettings.Default.MinPageConfidence));
        }
    }

    [Fact]
    public void All_documents_pages_through_the_whole_history()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        Assert.Equal("1,093,911 document(s)", cut.Find("#doc-count").TextContent);
        Assert.Equal("1–10 of 1,093,911", cut.Find("#doc-pager-range").TextContent);
        Assert.Equal(10, cut.FindAll("tbody tr").Count);
        Assert.Contains(HistoryText.Id(History.Count - 1), cut.Find("tbody tr").TextContent, StringComparison.Ordinal);
        Assert.Equal("1,093,911", cut.Find("#status-all .count").TextContent);

        cut.Find("button[aria-label='Page 109392']").Click();
        var last = Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains(HistoryText.Id(0), last.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Review_queue_lists_every_waiting_document_oldest_first()
    {
        var waiting = (await Snapshot()).Facts.WaitingReview;
        var oldest = Enumerable.Range(0, History.Count).First(i => History.Entry(i).Status == DocumentStatus.NeedsReview);

        var cut = Render<ReviewPage>();
        cut.WaitForElement("tbody tr");

        Assert.Equal(Number(waiting), cut.Find("#review-count").TextContent);
        Assert.Equal(Number(waiting), cut.Find("#review-flagged").TextContent);
        Assert.Equal($"{Number(waiting)} document(s)", cut.Find("#review-results").TextContent);
        Assert.Contains(HistoryText.Id(oldest), cut.Find("tbody tr").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approving_a_waiting_history_document_moves_it_from_the_queue_to_the_archive()
    {
        var before = await Snapshot();
        var index = Enumerable.Range(0, History.Count).First(i => History.Entry(i).Status == DocumentStatus.NeedsReview);
        var id = HistoryText.Id(index);

        Store.ChangeStatus(id, DocumentStatus.Archived, "review01");

        var after = await Snapshot();
        Assert.Equal(before.Total, after.Total);
        Assert.Equal(before.Facts.WaitingReview - 1, after.Facts.WaitingReview);
        Assert.Equal(before.StatusCounts[DocumentStatus.Archived] + 1, after.StatusCounts[DocumentStatus.Archived]);
        var archive = Catalog.Query(after, new DocumentQuery(Status: DocumentStatus.Archived), DocumentOrder.NewestApproval, 0, 1);
        Assert.Equal(id, Assert.Single(archive.Items).Id);
        Assert.Equal("review01", archive.Items[0].Approval!.By);
        var queue = Catalog.Query(after, new DocumentQuery(Status: DocumentStatus.NeedsReview), DocumentOrder.OldestUpload, 0, 10);
        Assert.DoesNotContain(queue.Items, d => d.Id == id);
        Assert.Equal(after.Facts.WaitingReview, queue.Total);
    }

    [Fact]
    public async Task Searching_a_reference_finds_the_one_history_document()
    {
        var snapshot = await Snapshot();
        var document = History.Materialize(123_456);

        var byReference = Catalog.Query(snapshot, new DocumentQuery(document.Metadata.Reference, DocumentSearchField.Reference), DocumentOrder.Newest, 0, 10);
        var byId = Catalog.Query(snapshot, new DocumentQuery(document.Id), DocumentOrder.Newest, 0, 10);
        var byText = Catalog.Query(snapshot, new DocumentQuery(document.Metadata.Title, DocumentSearchField.Content), DocumentOrder.Newest, 0, 10);

        Assert.Equal(document.Id, Assert.Single(byReference.Items).Id);
        Assert.Equal(document.Id, Assert.Single(byId.Items).Id);
        // History documents are searched by their details only.
        Assert.Equal(0, byText.Total);
    }

    [Fact]
    public async Task The_activity_log_names_the_history_documents_and_people_as_they_are()
    {
        var log = Services.GetRequiredService<SampleActivity>().Entries;

        Assert.All(log.Where(e => e.DocumentId is not null).Take(2_000), e =>
        {
            Assert.True(History.TryIndex(e.DocumentId, out var index));
            Assert.Equal(History.Materialize(index).FileName, e.DocumentName);
        });

        // Every approval of a reviewer on a day is in the log, at the time of the approval.
        var day = HistoryProfile.Days - 3;
        var approvals = Enumerable.Range(0, History.Count)
            .Where(i => History.Entry(i).ApprovedByReviewer && History.Entry(i).Approved / HistoryProfile.SecondsPerDay == day)
            .Select(i => (HistoryText.Id(i), History.Materialize(i).Approval!.AtUtc, HistoryProfile.Reviewers[History.Entry(i).Approver].UserName))
            .ToHashSet();
        var date = FirstDay.AddDays(day);
        var logged = log
            .Where(e => e.Action == AuditAction.StatusChanged && DateOnly.FromDateTime(e.AtUtc.ToLocalTime()) == date)
            .Select(e => (e.DocumentId!, e.AtUtc, e.User))
            .ToHashSet();
        Assert.Equal(approvals, logged);

        // Uploads are logged per batch, as many documents as the history has.
        var uploaded = log.Where(e => e.Action == AuditAction.Uploaded && DateOnly.FromDateTime(e.AtUtc.ToLocalTime()) == date)
            .Sum(e => int.Parse(e.Detail!, CultureInfo.InvariantCulture));
        Assert.Equal((await Snapshot()).Facts.Sum(date, date).Documents, uploaded);
    }

    [Fact]
    public void Every_history_document_has_its_own_reference()
    {
        var seen = new HashSet<(int Year, int Sequence)>();
        for (var index = 0; index < History.Count; index++)
            Assert.True(seen.Add((HistoryText.Year(index), HistoryText.Sequence(index))), $"{HistoryText.Id(index)} repeats a reference.");
    }

    [Fact]
    public async Task Exporting_the_whole_archive_asks_for_narrower_filters()
    {
        var files = Services.GetRequiredService<ApprovedDocumentFiles>();

        var result = await files.ExportAsync(new DocumentQuery(), "admin", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ApprovedErrors.ExportTooLargeCode, result.Error!.Code);
    }

    [Fact]
    public void The_dashboard_counts_the_history_documents()
    {
        var cut = Render<DashboardPage>();
        cut.WaitForElement("#project-documents");

        Assert.Equal("1,093,911", cut.Find("#project-documents").TextContent);
        cut.Find("#period-Month").Click();
        Assert.Equal(30, cut.FindAll(".bar").Count);
    }

    private static string Number(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}
