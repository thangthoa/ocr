using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Modules.Ocr.Domain.Jobs;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

public sealed class ReviewAndAdminPagesTests : BunitContext
{
    private readonly InMemoryOcr _ocr;
    private bool _ocrServiceHealthy;

    public ReviewAndAdminPagesTests()
    {
        TestServices.SetCulture("en");
        (_ocr, _) = Services.AddOcrAndDocuments();
        Services.AddHealthChecks().AddCheck("ocr-service",
            () => _ocrServiceHealthy ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(), [HealthCheckTags.Ready]);
    }

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    private DocumentStore Store => Services.GetRequiredService<DocumentStore>();

    [Fact]
    public void Review_queue_lists_real_documents_first_then_the_oldest()
    {
        _ocr.Seed(InMemoryOcr.NewJob("job_0000000000000001", "fresh.png", OcrJobStatus.Done) with
        {
            CreatedAtUtc = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc),
        });

        var cut = Render<ReviewPage>();

        cut.WaitForAssertion(() => Assert.Equal("5", cut.Find("#review-count").TextContent));
        var files = cut.FindAll("tbody tr td:first-child a").Select(a => a.TextContent).ToList();
        Assert.Equal(["fresh.png", "CT-2020-0017.pdf", "PO-2020-0102.pdf", "HR-2016-0190.pdf", "PV-2024-0150.pdf"], files);
        Assert.Equal("documents/job_0000000000000001", cut.Find("#review-start").GetAttribute("href"));
    }

    [Fact]
    public void Approving_a_waiting_document_offers_the_way_back_to_the_queue()
    {
        var cut = Render<DocumentDetailPage>(p => p.Add(x => x.Id, "DOC-2026-000105"));
        cut.WaitForElement("#action-approve").Click();

        Assert.NotEmpty(cut.FindAll("a[href='review']"));

        var queue = Render<ReviewPage>();
        queue.WaitForAssertion(() => Assert.Equal("3", queue.Find("#review-count").TextContent));
    }

    [Fact]
    public void Activity_log_shows_sign_in_and_document_changes()
    {
        Navigation.NavigateTo("login");
        var login = Render<LoginPage>();
        login.Find("#login-user").Change("admin");
        login.Find("#login-password").Change("admin");
        login.Find("form").Submit();
        Store.ChangeStatus("DOC-2026-000105", DocumentStatus.Archived, "admin");

        Navigation.NavigateTo("admin?tab=Audit");
        var cut = Render<AdminPage>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".audit-table tbody tr").Select(r => r.TextContent).ToList();
            Assert.Equal(2, rows.Count);
            Assert.Contains("Changed the status to Archived", rows[0], StringComparison.Ordinal);
            Assert.Contains("Signed in", rows[1], StringComparison.Ordinal);
        });
        Assert.NotEmpty(cut.FindAll(".audit-table a[href='documents/DOC-2026-000105']"));
    }

    [Fact]
    public void Failed_sign_in_is_logged_without_the_password()
    {
        Navigation.NavigateTo("login");
        var login = Render<LoginPage>();
        login.Find("#login-user").Change("someone");
        login.Find("#login-password").Change("secret-password");
        login.Find("form").Submit();

        var entry = Assert.Single(Services.GetRequiredService<AuditLog>().Latest());
        Assert.Equal((AuditAction.SignInFailed, "someone"), (entry.Action, entry.User));
        Assert.DoesNotContain("secret-password", entry.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "Not reachable")]
    [InlineData(true, "Connected")]
    public void Connected_systems_show_the_real_ocr_service_status(bool healthy, string expected)
    {
        _ocrServiceHealthy = healthy;
        var cut = Render<AdminPage>();

        cut.Find("#tab-Integrations").Click();

        cut.WaitForAssertion(() => Assert.Equal(expected, cut.Find("#ocr-health").TextContent));
        Assert.Equal(5, cut.FindAll(".badge.tag-purple").Count);
    }

    [Fact]
    public void Retention_rules_link_to_the_documents_they_cover()
    {
        var cut = Render<AdminPage>();

        cut.Find("#tab-Retention").Click();

        cut.WaitForAssertion(() => Assert.Equal("4", cut.Find("a[href='documents?department=Finance&kind=PaymentVoucher']").TextContent));
    }
}
