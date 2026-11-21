using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

public sealed class UsersAndRolesTests : BunitContext
{
    public UsersAndRolesTests()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments();
        Services.AddHealthChecks();
    }

    private UserDirectory Accounts => Services.GetRequiredService<UserDirectory>();

    private AuditLog Audit => Services.GetRequiredService<AuditLog>();

    [Fact]
    public void Settings_open_on_the_users_with_their_role_and_access()
    {
        var cut = Render<UsersPage>();

        Assert.Equal("Users", cut.Find("h1").TextContent);
        // The demo account, the IT administrator, the people of the simulated line (12 operators, 6 reviewers) and the office.
        Assert.Equal("25 users · 24 active · 1 disabled", cut.Find("#users-summary").TextContent.Trim());
        cut.Find("#user-search").Input("review02");
        var reviewer = cut.Find("[id='user-review02']").TextContent;
        Assert.Contains("OCR reviewer", reviewer, StringComparison.Ordinal);
        Assert.Contains("Finance, Procurement", reviewer, StringComparison.Ordinal);
        Assert.Contains("Active Directory", reviewer, StringComparison.Ordinal);
    }

    [Fact]
    public void Users_can_be_filtered_by_role_status_and_text()
    {
        var cut = Render<UsersPage>();

        cut.Find("#user-role-filter").Change("Reader");
        Assert.Equal(4, Rows(cut).Count);

        cut.Find("#user-search").Input("legal");
        Assert.Equal(["user-reader.legal"], Rows(cut));

        cut.Find("#user-search").Input("");
        cut.Find("#user-role-filter").Change("");
        cut.Find("#user-status-filter").Change("Disabled");
        Assert.Equal(["user-capture03"], Rows(cut));
    }

    [Fact]
    public void Adding_a_user_checks_the_form_then_saves_and_logs_it()
    {
        var cut = Render<UsersPage>();
        cut.Find("#user-add").Click();

        cut.Find("#user-username").Change("capture01");
        cut.Find("#user-email").Change("not-an-email");
        cut.Find("#user-form").Submit();

        Assert.Equal("This field is required.", cut.Find("#user-name-error").TextContent);
        Assert.Equal("Another account already uses this user name.", cut.Find("#user-username-error").TextContent);
        Assert.Equal("Enter an e-mail address such as name@example.org.", cut.Find("#user-email-error").TextContent);
        Assert.Equal("Choose at least one department, or All departments.", cut.Find("#user-departments-error").TextContent);
        Assert.Null(Accounts.Find("capture13"));

        cut.Find("#user-name").Change("Test Operator");
        cut.Find("#user-username").Change("capture13");
        cut.Find("#user-email").Change("capture13@example.org");
        cut.Find("#user-role").Change("CaptureOperator");
        cut.Find("#user-dept-Finance").Change(true);
        cut.Find("#user-form").Submit();

        Assert.Equal("Test Operator saved.", cut.Find("#users-message").TextContent);
        var saved = Accounts.Find("capture13")!;
        Assert.Equal((UserRole.CaptureOperator, UserSource.Local, UserStatus.Active), (saved.Role, saved.Source, saved.Status));
        Assert.Equal(new[] { DocumentDepartment.Finance }, saved.Departments);
        var entry = Audit.Latest()[0];
        Assert.Equal((AuditAction.UserAdded, "capture13"), (entry.Action, entry.Detail));
    }

    [Fact]
    public void Editing_a_directory_account_keeps_its_name_and_logs_the_change()
    {
        var cut = Render<UsersPage>();
        cut.Find("#user-search").Input("review02");
        cut.Find("[id='user-edit-review02']").Click();

        Assert.True(cut.Find("#user-name").HasAttribute("disabled"));
        Assert.True(cut.Find("#user-username").HasAttribute("disabled"));
        cut.Find("#user-role").Change("RecordsManager");
        cut.Find("#user-form").Submit();

        var saved = Accounts.Find("review02")!;
        Assert.Equal((UserRole.RecordsManager, UserSource.ActiveDirectory, "Omar Al Shamsi"), (saved.Role, saved.Source, saved.DisplayName));
        Services.GetRequiredService<NavigationManager>().NavigateTo("users/activity?user=admin");
        var activity = Render<ActivityLogPage>();
        Assert.Contains("Changed the user review02", activity.Find(".activity-table tbody").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_form_shows_what_the_user_would_be_allowed_to_see_and_do()
    {
        var cut = Render<UsersPage>();
        // The accounts take several pages of ten: find this one first.
        cut.Find("#user-search").Input("reader.finance");
        cut.Find("[id='user-edit-reader.finance']").Click();

        // Finance documents up to Confidential: 4 of the 14 samples, then 3 once Confidential is out of reach.
        cut.WaitForAssertion(() => Assert.Equal("Sees 4 of the 14 documents listed now.", cut.Find("#user-visible").TextContent));
        Assert.Equal(2, cut.FindAll("#user-permissions li.granted").Count);
        cut.Find("#user-clearance").Change("Internal");
        Assert.Equal("Sees 3 of the 14 documents listed now.", cut.Find("#user-visible").TextContent);

        cut.Find("#user-active").Change(false);
        Assert.NotNull(cut.Find("#user-access-disabled"));
        Assert.Empty(cut.FindAll("#user-permissions li.granted"));
        Assert.Equal("Sees 0 of the 14 documents listed now.", cut.Find("#user-visible").TextContent);
    }

    [Fact]
    public void The_last_active_administrator_cannot_be_disabled()
    {
        var other = Accounts.Find("it.admin")!;
        Assert.Empty(Accounts.Save(other with { Status = UserStatus.Disabled }, other.UserName, "admin"));
        var cut = Render<UsersPage>();
        cut.Find("[id='user-edit-admin']").Click();

        cut.Find("#user-active").Change(false);
        cut.Find("#user-form").Submit();

        Assert.Equal("This is the last active administrator: keep the role and the account active.", cut.Find("#user-role-error").TextContent);
        Assert.True(Accounts.Find("admin")!.IsActive);
    }

    [Fact]
    public void Role_permissions_are_saved_from_the_matrix_and_logged()
    {
        var cut = Render<RolesPage>();

        Assert.True(cut.Find("#perm-Administrator-ManageUsers").HasAttribute("disabled"));
        cut.Find("#perm-Reader-Download").Change(false);
        cut.Find("#perm-Reader-ViewReports").Change(true);
        cut.Find("#roles-form").Submit();

        Assert.NotNull(cut.Find("#roles-saved"));
        Assert.Equal(new[] { AccessRight.ViewDocuments, AccessRight.ViewReports }, Accounts.PermissionsOf(UserRole.Reader).Order());
        Assert.Equal(AuditAction.RolesChanged, Audit.Latest()[0].Action);
    }

    [Fact]
    public void Administrators_keep_user_management_whatever_is_saved()
    {
        Accounts.SavePermissions(new Dictionary<UserRole, IReadOnlySet<AccessRight>>(), "admin");

        Assert.Equal(new[] { AccessRight.ManageSettings, AccessRight.ManageUsers }, Accounts.PermissionsOf(UserRole.Administrator).Order());
        Assert.Empty(Accounts.PermissionsOf(UserRole.Reader));
    }

    private static List<string?> Rows(IRenderedComponent<UsersPage> cut) =>
        cut.FindAll(".users-table tbody tr").Select(r => r.GetAttribute("id")).ToList();
}
