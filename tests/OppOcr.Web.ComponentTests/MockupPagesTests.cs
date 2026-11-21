using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

public sealed class MockupPagesTests : BunitContext
{
    public MockupPagesTests()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments();
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    [Fact]
    public void Sign_in_rejects_wrong_credentials()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();

        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("wrong");
        cut.Find("form").Submit();

        Assert.Contains("Incorrect user name or password.", cut.Find("[role=alert]").TextContent, StringComparison.Ordinal);
        Assert.EndsWith("/login", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void Sign_in_with_the_demo_account_asks_for_the_one_time_code_then_opens_the_dashboard()
    {
        var session = JSInterop.SetupVoid("ocrSession.start", _ => true);
        session.SetVoidResult();
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();

        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("admin");
        cut.Find("#login-form").Submit();

        // The password alone does not sign in: nothing is stored or logged until the code is right.
        Assert.Equal("Verify it's you", cut.Find("#otp-title").TextContent);
        Assert.EndsWith("/login", Navigation.Uri, StringComparison.Ordinal);
        Assert.Empty(session.Invocations);
        Assert.Empty(Audit.Latest());

        cut.Find("#login-otp").Change("123456");
        cut.Find("#otp-form").Submit();

        Assert.Equal(Navigation.BaseUri, Navigation.Uri);
        Assert.Equal(new object?[] { "admin", false }, Assert.Single(session.Invocations).Arguments);
        Assert.Equal(AuditAction.SignedIn, Assert.Single(Audit.Latest()).Action);
    }

    [Fact]
    public void Sign_in_offers_remember_me_unchecked_by_default()
    {
        var session = JSInterop.SetupVoid("ocrSession.start", _ => true);
        session.SetVoidResult();
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();

        var remember = cut.Find("#login-remember");
        Assert.Equal("checkbox", remember.GetAttribute("type"));
        Assert.False(remember.HasAttribute("checked"));
        Assert.Equal("Remember me", cut.Find("label[for=login-remember]").TextContent);

        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("admin");
        cut.Find("#login-remember").Change(true);
        cut.Find("#login-form").Submit();
        cut.Find("#login-otp").Change("123456");
        cut.Find("#otp-form").Submit();

        // Remembered sessions last longer (session.js).
        Assert.Equal(new object?[] { "admin", true }, Assert.Single(session.Invocations).Arguments);
    }

    [Fact]
    public void A_wrong_code_is_refused_and_logged()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();
        SignInWithPassword(cut);

        cut.Find("#login-otp").Change("12345");
        cut.Find("#otp-form").Submit();
        Assert.Equal("Enter the 6 digits of the code.", cut.Find("#login-error").TextContent);

        cut.Find("#login-otp").Change("654321");
        cut.Find("#otp-form").Submit();
        Assert.Equal("The code is not correct. Attempts left: 4.", cut.Find("#login-error").TextContent);
        Assert.Equal(AuditAction.OtpFailed, Assert.Single(Audit.Latest()).Action);
        Assert.EndsWith("/login", Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void The_code_has_one_box_per_digit_left_to_right()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();
        SignInWithPassword(cut);

        var group = cut.Find(".otp-boxes");
        var boxes = group.QuerySelectorAll(".otp-digit");
        Assert.Equal(LoginPage.CodeLength, boxes.Length);
        Assert.Equal(("ltr", "login-otp"), (group.GetAttribute("dir"), group.GetAttribute("data-target")));
        Assert.Equal("one-time-code", boxes[0].GetAttribute("autocomplete"));
        Assert.Equal("Digit 1 of 6", boxes[0].GetAttribute("aria-label"));
        Assert.Equal("hidden", cut.Find("#login-otp").GetAttribute("type"));
    }

    [Fact]
    public void A_new_code_can_be_asked_for_30_seconds_after_the_last_one()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(time);
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();
        SignInWithPassword(cut);

        Assert.True(cut.Find("#otp-resend").HasAttribute("disabled"));
        Assert.Equal("Send a new code in 30s", cut.Find("#otp-resend").TextContent.Trim());

        time.Advance(TimeSpan.FromSeconds(12));
        cut.WaitForAssertion(() => Assert.Equal("Send a new code in 18s", cut.Find("#otp-resend").TextContent.Trim()));

        time.Advance(TimeSpan.FromSeconds(18));
        cut.WaitForAssertion(() => Assert.False(cut.Find("#otp-resend").HasAttribute("disabled")));
        Assert.Equal("Send a new code", cut.Find("#otp-resend").TextContent.Trim());

        cut.Find("#otp-resend").Click();
        Assert.Equal("A new code was sent.", cut.Find("#otp-resent").TextContent);
        Assert.True(cut.Find("#otp-resend").HasAttribute("disabled"));
        Assert.Equal("Send a new code in 30s", cut.Find("#otp-resend").TextContent.Trim());
    }

    [Fact]
    public void Too_many_wrong_codes_send_the_user_back_to_the_password()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();
        SignInWithPassword(cut);

        for (var attempt = 0; attempt < LoginPage.MaxCodeAttempts; attempt++)
        {
            cut.Find("#login-otp").Change("000000");
            cut.Find("#otp-form").Submit();
        }

        Assert.NotNull(cut.Find("#login-password"));
        Assert.Equal("Too many wrong codes. Sign in again.", cut.Find("#login-error").TextContent);
    }

    [Theory]
    [InlineData("/reports?report=Quality", "reports?report=Quality")]
    [InlineData("https://evil.example/", "")]
    [InlineData("//evil.example/", "")]
    [InlineData("/login", "")]
    public void After_the_code_the_user_goes_back_to_the_page_they_asked_for_on_this_site(string returnUrl, string expected)
    {
        JSInterop.SetupVoid("ocrSession.start", _ => true).SetVoidResult();
        Navigation.NavigateTo("login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        var cut = Render<LoginPage>();
        SignInWithPassword(cut);

        cut.Find("#login-otp").Change("123456");
        cut.Find("#otp-form").Submit();

        Assert.Equal(Navigation.BaseUri + expected, Navigation.Uri);
    }

    private static void SignInWithPassword(IRenderedComponent<LoginPage> cut)
    {
        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("admin");
        cut.Find("#login-form").Submit();
    }

    [Fact]
    public async Task Dashboard_switches_between_day_week_and_month()
    {
        var facts = (await Services.GetRequiredService<DocumentCatalog>().SnapshotAsync(CancellationToken.None)).Facts;
        var cut = Render<DashboardPage>();
        cut.WaitForElement("#kpi-summary");

        // Pages per day is the daily average of the period; the day chart has a bar per working hour.
        Assert.Equal(16, cut.FindAll(".bar").Count);
        Assert.Equal(PagesPerDay(ReportPeriod.Day), cut.Find("#kpi-PagesPerDay .kpi-card-value").TextContent);
        Assert.Equal("true", cut.Find("#period-Day").GetAttribute("aria-pressed"));

        cut.Find("#period-Week").Click();
        Assert.Equal(7, cut.FindAll(".bar").Count);
        Assert.Equal(PagesPerDay(ReportPeriod.Week), cut.Find("#kpi-PagesPerDay .kpi-card-value").TextContent);
        Assert.Equal("true", cut.Find("#period-Week").GetAttribute("aria-pressed"));

        cut.Find("#period-Month").Click();
        Assert.Equal(30, cut.FindAll(".bar").Count);
        Assert.Equal(PagesPerDay(ReportPeriod.Month), cut.Find("#kpi-PagesPerDay .kpi-card-value").TextContent);

        string PagesPerDay(ReportPeriod period) =>
            ((int)Math.Round(ProjectReports.For(facts, period).PagesPerDay)).ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
    }

    [Fact]
    public void Documents_are_paged_and_filtered_by_status()
    {
        var cut = Render<DocumentsPage>();

        cut.WaitForAssertion(() => Assert.Contains("14 document(s)", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(10, cut.FindAll("tbody tr").Count);
        Assert.Equal("1–10 of 14", cut.Find("#doc-pager-range").TextContent);

        cut.Find("button[aria-label='Page 2']").Click();
        Assert.Equal(4, cut.FindAll("tbody tr").Count);
        Assert.Equal("11–14 of 14", cut.Find("#doc-pager-range").TextContent);

        cut.Find("#doc-pager-size").Change("20");
        Assert.Equal(14, cut.FindAll("tbody tr").Count);
        Assert.Empty(cut.FindAll(".pager-pages"));

        cut.Find("#status-Archived").Click();

        var files = cut.FindAll("tbody tr td:first-child a").Select(a => a.TextContent).ToList();
        Assert.Equal(
            ["ADM-2022-0310.pdf", "HR-2016-0187.pdf", "HR-2018-0044.pdf", "LG-2011-0032.pdf", "PO-2019-0388.pdf", "PV-2019-0413.pdf", "PV-2020-0021.pdf"],
            files.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Documents_can_be_searched_by_a_metadata_field()
    {
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        cut.Find("#doc-field").Change(nameof(DocumentSearchField.Reference));
        cut.Find("#doc-search").Input("2019/413");

        var row = Assert.Single(cut.FindAll("tbody tr"));
        Assert.Equal("PV-2019-0413.pdf", row.QuerySelector("td a")!.TextContent);
    }

    [Fact]
    public void Documents_read_the_search_from_the_query_string()
    {
        Navigation.NavigateTo($"documents?field=Keyword&q={Uri.EscapeDataString("صيانة")}");
        var cut = Render<DocumentsPage>();
        cut.WaitForElement("tbody tr");

        var files = cut.FindAll("tbody tr td:first-child a").Select(a => a.TextContent).Order(StringComparer.Ordinal);
        Assert.Equal(["CT-2020-0017.pdf", "PV-2024-0150.pdf"], files);
        Assert.Equal(nameof(DocumentSearchField.Keyword), cut.Find("#doc-field").GetAttribute("value"));
    }

    [Fact]
    public void Editing_a_document_saves_a_new_version()
    {
        var cut = RenderDocument("DOC-2026-000103");

        cut.Find("#edit-metadata").Click();
        cut.Find("#edit-title").Change("عقد صيانة أجهزة الحاسوب ٢٠٢٠ — معدّل");
        cut.Find("#edit-note").Change("Shortened the title.");
        cut.Find("#save-edit").Click();

        Assert.Contains("Saved as version 1.1.", cut.Markup, StringComparison.Ordinal);
        var document = Store.FindSample("DOC-2026-000103")!;
        Assert.Equal(["1.0", "1.1"], document.Versions.Select(v => v.Number));
        Assert.Equal("عقد صيانة أجهزة الحاسوب ٢٠٢٠ — معدّل", document.Metadata.Title);
        Assert.Equal("admin", document.CurrentVersion!.Author);
    }

    [Fact]
    public void Saving_without_a_title_is_refused()
    {
        var cut = RenderDocument("DOC-2026-000103");

        cut.Find("#edit-metadata").Click();
        cut.Find("#edit-title").Change(" ");
        cut.Find("#save-edit").Click();

        Assert.Contains("Title is required.", cut.Find("#edit-title-error").TextContent, StringComparison.Ordinal);
        Assert.Single(Store.FindSample("DOC-2026-000103")!.Versions);
    }

    [Fact]
    public void Restoring_an_old_version_adds_a_new_one()
    {
        // An archived document is final; sent back to review, its history can be used again.
        Store.ChangeStatus("DOC-2026-000104", DocumentStatus.NeedsReview, "admin");
        var cut = RenderDocument("DOC-2026-000104");

        cut.Find("#tab-versions").Click();
        cut.Find("button[aria-label='Restore version 1.0 as a new version']").Click();

        Assert.Contains("Version 1.0 restored as version 1.2.", cut.Markup, StringComparison.Ordinal);
        var document = Store.FindSample("DOC-2026-000104")!;
        Assert.Equal(DocumentVersionChange.Restored, document.CurrentVersion!.Change);
        Assert.Equal(document.Versions[0].PageTexts, document.PageTexts);
    }

    [Fact]
    public void Archived_documents_cannot_be_edited_or_restored()
    {
        var cut = RenderDocument("DOC-2026-000101");

        Assert.Empty(cut.FindAll("#action-edit, #edit-metadata"));
        Assert.Contains("Archived documents are final.", cut.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("#action-review"));

        cut.Find("#tab-versions").Click();
        Assert.Empty(cut.FindAll("button[aria-label^='Restore version']"));
        Assert.NotEmpty(cut.FindAll("button[aria-label^='View version']"));
    }

    [Fact]
    public void Approving_a_reviewed_document_archives_it()
    {
        var cut = RenderDocument("DOC-2026-000105");

        cut.Find("#action-approve").Click();

        Assert.Contains("Status changed to Archived.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal(DocumentStatus.Archived, Store.FindSample("DOC-2026-000105")!.Status);
        Assert.NotEmpty(cut.FindAll("#action-review"));
        Assert.Empty(cut.FindAll("#action-approve, #action-edit, #edit-metadata"));
    }

    [Fact]
    public void Metadata_values_link_to_a_search_for_documents_with_the_same_value()
    {
        var cut = RenderDocument("DOC-2026-000102");

        Assert.NotEmpty(cut.FindAll("a[href='documents?field=Reference&q=PO-2019-0388']"));
        Assert.NotEmpty(cut.FindAll("a[href='documents?department=Procurement']"));
    }

    [Fact]
    public void Unknown_document_shows_not_found()
    {
        var cut = RenderDocument("DOC-0000-000000");

        Assert.Contains("Document not found.", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_by_content_finds_text_inside_the_document()
    {
        var results = DocumentStore.Filter(Store.Samples(), new DocumentQuery("أثاث مكتبي", DocumentSearchField.Content));

        Assert.Equal("DOC-2026-000110", Assert.Single(results).Id);
    }

    [Fact]
    public void Date_range_filters_on_the_document_date()
    {
        var results = DocumentStore.Filter(Store.Samples(), new DocumentQuery(From: new DateOnly(2023, 1, 1), To: new DateOnly(2024, 12, 31)));

        Assert.Equal(["DOC-2026-000113", "DOC-2026-000114"], results.Select(d => d.Id).Order(StringComparer.Ordinal));
    }

    private IRenderedComponent<DocumentDetailPage> RenderDocument(string id)
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, id));
        cut.WaitForAssertion(() => Assert.DoesNotContain("Loading", cut.Markup, StringComparison.Ordinal));
        return cut;
    }
}
