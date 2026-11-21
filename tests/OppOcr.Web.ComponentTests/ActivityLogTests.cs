using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups.Activity;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

public sealed class ActivityLogTests : BunitContext
{
    public ActivityLogTests()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments();
        Services.AddHealthChecks();
    }

    private ActivityTrail Trail => Services.GetRequiredService<ActivityTrail>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    private NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    [Fact]
    public void The_sample_month_covers_what_the_customer_asked_about()
    {
        var entries = Services.GetRequiredService<SampleActivity>().Entries;

        Assert.True(entries.Count > 1_000, $"Only {entries.Count} sample entries.");
        AuditAction[] expected =
        [
            AuditAction.SignedIn, AuditAction.SignedOut, AuditAction.SignInFailed, AuditAction.OtpFailed, AuditAction.AccountLocked,
            AuditAction.AccessDenied, AuditAction.Viewed, AuditAction.Searched, AuditAction.Downloaded, AuditAction.Printed,
            AuditAction.Uploaded, AuditAction.EditedInWord, AuditAction.StatusChanged, AuditAction.QualityChecked,
            AuditAction.OcrFailed, AuditAction.OcrRetried, AuditAction.UserChanged,
        ];
        Assert.All(expected, action => Assert.Contains(entries, e => e.Action == action));
        Assert.All(Enum.GetValues<ActivityCategory>(), category => Assert.Contains(entries, e => ActivityRules.Category(e.Action) == category));
        // Every sample entry says where it came from, and the demo account's own actions are kept apart.
        Assert.All(entries, e => Assert.False(string.IsNullOrEmpty(e.IpAddress)));
        Assert.DoesNotContain(entries, e => e.User == "admin");
        Assert.Equal(entries.OrderBy(e => e.AtUtc), entries);
    }

    [Fact]
    public void The_sample_month_is_the_same_on_every_run()
    {
        var history = Services.GetRequiredService<ProjectHistory>();
        var first = new SampleActivity(TimeProvider.System, history).Entries;
        var second = new SampleActivity(TimeProvider.System, history).Entries;

        // Nothing is simulated today, so the month is the same whatever the time of day.
        Assert.Equal(first, second);
    }

    [Fact]
    public void The_hash_chain_shows_where_the_log_was_changed()
    {
        Audit.Record("admin", AuditAction.Viewed, "DOC-2026-000101", "PV-2019-0413.pdf");
        var rows = Trail.Load();

        Assert.Null(ActivityTrail.FirstBrokenLink(rows));
        Assert.Equal(rows[1].Hash, rows[0].PreviousHash);

        // Rewrite what an older entry says, as someone editing the stored log would.
        var tampered = rows.ToList();
        var index = rows.Count / 2;
        tampered[index] = rows[index] with { Entry = rows[index].Entry with { User = "someone-else" } };
        Assert.Equal(rows[index].Sequence, ActivityTrail.FirstBrokenLink(tampered));
    }

    [Fact]
    public void The_page_shows_the_newest_entries_with_the_integrity_check()
    {
        var cut = Render<ActivityLogPage>();

        Assert.StartsWith("Integrity verified:", cut.Find("#integrity-status").TextContent.Trim(), StringComparison.Ordinal);
        Assert.Equal(20, cut.FindAll(".activity-table tbody tr").Count);
        Assert.Equal(Trail.Load().Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), cut.Find("#stat-entries").TextContent);
    }

    [Fact]
    public void Live_actions_are_marked_and_link_to_their_document()
    {
        Audit.Record("admin", AuditAction.Downloaded, "DOC-2026-000104", "HR-2016-0187.pdf", version: "1.1", detail: "PDF");
        Navigation.NavigateTo("users/activity?user=admin");

        var cut = Render<ActivityLogPage>();

        var row = Assert.Single(cut.FindAll(".activity-table tbody tr"));
        Assert.Contains("Live", row.TextContent, StringComparison.Ordinal);
        Assert.Contains("Downloaded final version 1.1 as PDF", row.TextContent, StringComparison.Ordinal);
        Assert.NotNull(row.QuerySelector("a[href='documents/DOC-2026-000104']"));
    }

    [Fact]
    public void The_summary_figures_filter_the_log()
    {
        var cut = Render<ActivityLogPage>();

        cut.Find("#stat-failed").Click();

        Assert.Equal("Security", cut.Find("#activity-category").GetAttribute("value"));
        Assert.All(cut.FindAll(".activity-table tbody tr"), r => Assert.Contains("Failed", r.TextContent, StringComparison.Ordinal));

        cut.Find("#stat-denied").Click();
        Assert.All(cut.FindAll(".activity-table tbody tr"), r => Assert.Contains("Opening refused", r.TextContent, StringComparison.Ordinal));

        cut.Find("#activity-clear").Click();
        Assert.Equal(20, cut.FindAll(".activity-table tbody tr").Count);
    }

    [Fact]
    public void An_entry_opens_to_show_its_place_in_the_chain()
    {
        var cut = Render<ActivityLogPage>();
        var newest = Trail.Load()[0];

        cut.Find($"#activity-toggle-{newest.Sequence}").Click();

        var details = cut.Find($"#activity-details-{newest.Sequence}").TextContent;
        Assert.Contains(newest.Hash, details, StringComparison.Ordinal);
        Assert.Contains(newest.PreviousHash, details, StringComparison.Ordinal);
        Assert.Contains(newest.SiemEventId, details, StringComparison.Ordinal);
    }

    [Fact]
    public void Export_writes_the_filtered_log_as_csv_and_logs_it()
    {
        var file = Trail.Export(new ActivityQuery(Result: ActivityResult.Denied), "admin");

        var lines = Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("#,Time,User,Category,Action,Document,IP address,Client,Result,SIEM event,Hash", lines[0], StringComparison.Ordinal);
        Assert.True(lines.Length > 1);
        Assert.All(lines.Skip(1), line => Assert.Contains(",Refused,", line, StringComparison.Ordinal));
        var entry = Audit.Latest()[0];
        Assert.Equal((AuditAction.ActivityExported, (lines.Length - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)), (entry.Action, entry.Detail));
    }

    [Fact]
    public void Export_link_keeps_the_filters_of_the_page()
    {
        var query = new ActivityQuery("CT-2020", "reader.legal", ActivityCategory.DocumentAccess, ActivityResult.Success,
            new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 20));
        var url = new Uri("http://localhost" + ActivityEndpoints.ExportUrl(query));
        var values = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);

        Assert.Equal("/users/activity/export.csv", url.AbsolutePath);
        Assert.Equal(query, ActivityEndpoints.ParseQuery(values["q"], values["user"], values["category"], values["result"], values["from"], values["to"]));
    }

    [Theory]
    [InlineData("Users", "users")]
    [InlineData("Roles", "users/roles")]
    [InlineData("Audit", "users/activity")]
    public void Old_settings_links_lead_to_the_users_menu(string tab, string expected)
    {
        Navigation.NavigateTo($"admin?tab={tab}");

        Render<AdminPage>();

        Assert.Equal(Navigation.BaseUri + expected, Navigation.Uri);
    }
}
