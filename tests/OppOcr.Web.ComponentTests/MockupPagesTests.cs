using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
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
    public void Sign_in_with_the_demo_account_opens_the_dashboard()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();

        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("admin");
        cut.Find("form").Submit();

        Assert.Equal(Navigation.BaseUri, Navigation.Uri);
    }

    [Fact]
    public void Sign_in_offers_remember_me_unchecked_by_default()
    {
        Navigation.NavigateTo("login");
        var cut = Render<LoginPage>();

        var remember = cut.Find("#login-remember");
        Assert.Equal("checkbox", remember.GetAttribute("type"));
        Assert.False(remember.HasAttribute("checked"));
        Assert.Equal("Remember me", cut.Find("label[for=login-remember]").TextContent);

        cut.Find("#login-user").Change("admin");
        cut.Find("#login-password").Change("admin");
        cut.Find("#login-remember").Change(true);
        cut.Find("form").Submit();

        Assert.Equal(Navigation.BaseUri, Navigation.Uri);
    }

    [Fact]
    public void Dashboard_switches_between_day_week_and_month()
    {
        var cut = Render<DashboardPage>();

        Assert.Equal(16, cut.FindAll(".bar").Count);
        Assert.Equal("25,510", cut.Find("#kpi-pages").TextContent);
        Assert.Equal("true", cut.Find("#period-Day").GetAttribute("aria-pressed"));

        cut.Find("#period-Week").Click();
        Assert.Equal(7, cut.FindAll(".bar").Count);
        Assert.Equal("180,610", cut.Find("#kpi-pages").TextContent);
        Assert.Equal("true", cut.Find("#period-Week").GetAttribute("aria-pressed"));

        cut.Find("#period-Month").Click();
        Assert.Equal(30, cut.FindAll(".bar").Count);
        Assert.Equal("759,510", cut.Find("#kpi-pages").TextContent);
    }

    [Fact]
    public void Documents_are_paged_and_filtered_by_status()
    {
        var cut = Render<DocumentsPage>();

        cut.WaitForAssertion(() => Assert.Contains("14 document(s)", cut.Markup, StringComparison.Ordinal));
        Assert.Equal(DocumentsPage.PageSize, cut.FindAll("tbody tr").Count);
        Assert.Contains("Page 1 of 2", cut.Markup, StringComparison.Ordinal);

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

        cut.Find("#action-edit").Click();
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

        cut.Find("#action-edit").Click();
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

        Assert.Empty(cut.FindAll("#action-edit"));
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
        Assert.Empty(cut.FindAll("#action-approve, #action-edit"));
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
