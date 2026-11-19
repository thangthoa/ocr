using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Administration mockup. Roles and retention are sample settings; the OCR service status is a real
/// health check and the activity log records what was done in this app since it started.
/// </summary>
public sealed partial class AdminPage : IDisposable
{
    private static readonly RoleRow[] Roles =
    [
        new("CaptureOperator", "ECM-Capture", 12),
        new("Reviewer", "ECM-Review", 6),
        new("RecordsManager", "ECM-Records", 4),
        new("Reader", "ECM-Read-<department>", 86),
        new("Administrator", "ECM-Admin", 2),
    ];

    private static readonly RetentionRule[] Retention =
    [
        new(DocumentDepartment.Finance, DocumentKind.PaymentVoucher, 10, "FiscalYearEnd", "Review"),
        new(DocumentDepartment.Procurement, DocumentKind.PurchaseOrder, 7, "FiscalYearEnd", "Review"),
        new(DocumentDepartment.Legal, DocumentKind.Contract, 15, "ContractEnd", "Transfer"),
        new(DocumentDepartment.HumanResources, DocumentKind.Decision, null, "ServiceEnd", "Keep"),
        new(DocumentDepartment.Administration, DocumentKind.Correspondence, 5, "DocumentDate", "Destroy"),
    ];

    private static readonly string[] PlannedSystems = ["Ad", "SharePoint", "Cms", "Internal", "Siem"];

    private readonly CancellationTokenSource _disposed = new();
    private AdminTab _tab = AdminTab.Roles;
    private HealthStatus? _ocrHealth;
    private bool _checking;
    private DocumentList? _documents;

    private enum AdminTab
    {
        Roles,
        Retention,
        Integrations,
        Audit,
    }

    [SupplyParameterFromQuery(Name = "tab")]
    public string? QueryTab { get; set; }

    [Inject] private AuditLog Audit { get; set; } = default!;

    [Inject] private DocumentCatalog Catalog { get; set; } = default!;

    [Inject] private HealthCheckService Health { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    [Inject] private ILogger<AdminPage> Logger { get; set; } = default!;

    public void Dispose()
    {
        _disposed.Cancel();
        _disposed.Dispose();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (Enum.TryParse<AdminTab>(QueryTab, ignoreCase: true, out var tab) && Enum.IsDefined(tab))
            await SelectAsync(tab);
    }

    private async Task SelectAsync(AdminTab tab)
    {
        _tab = tab;
        if (tab == AdminTab.Integrations && _ocrHealth is null)
            await CheckAsync();
        if (tab == AdminTab.Retention && _documents is null)
            await LoadDocumentsAsync();
    }

    /// <summary>Runs the readiness checks; today that is the OCR service only.</summary>
    private async Task CheckAsync()
    {
        _checking = true;
        try
        {
            var report = await Health.CheckHealthAsync(c => c.Tags.Contains(HealthCheckTags.Ready), _disposed.Token);
            _ocrHealth = report.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "check the OCR service");
            _ocrHealth = HealthStatus.Unhealthy;
        }
        finally
        {
            _checking = false;
        }
    }

    private async Task LoadDocumentsAsync()
    {
        try
        {
            _documents = await Catalog.ListAsync(_disposed.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogUnexpected(Logger, ex, "count documents");
        }
    }

    private string DocumentsUnder(RetentionRule rule) =>
        _documents is null
            ? MockupTextExtensions.Missing
            : MockupTextExtensions.FormatNumber(_documents.Documents.Count(d => d.Metadata.Department == rule.Department && d.Metadata.Kind == rule.Kind));

    private string Describe(AuditEntry entry) => entry.Action switch
    {
        AuditAction.SignedIn => Text["Audit.SignedIn"],
        AuditAction.SignInFailed => Text["Audit.SignInFailed"],
        AuditAction.Uploaded => Text["Audit.Uploaded", entry.BatchId ?? MockupTextExtensions.Missing],
        AuditAction.Edited => Text["Audit.Edited", entry.Version ?? "?"],
        AuditAction.Restored => Text["Audit.Restored", entry.Version ?? "?"],
        AuditAction.StatusChanged => Text["Audit.StatusChanged", entry.Status is { } status ? Text.Status(status) : "?"],
        AuditAction.Deleted => Text["Audit.Deleted"],
        _ => entry.Action.ToString(),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Administration UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);

    private sealed record RoleRow(string Key, string Group, int Users);

    /// <param name="Years">Null keeps the records permanently.</param>
    private sealed record RetentionRule(DocumentDepartment Department, DocumentKind Kind, int? Years, string From, string Then);
}
