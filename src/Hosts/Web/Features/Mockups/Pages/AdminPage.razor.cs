using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Quality;

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

    // Mockup: no authentication yet, so changes are made by the demo account.
    private const string CurrentUser = LoginPage.DemoUserName;

    private const string MinDocumentField = "min-document";
    private const string MinPageField = "min-page";

    private readonly CancellationTokenSource _disposed = new();
    private readonly Dictionary<string, string> _qualityErrors = [];
    private AdminTab _tab = AdminTab.Roles;
    private HealthStatus? _ocrHealth;
    private bool _checking;
    private DocumentList? _documents;
    private QualityForm _quality = new();
    private bool _qualitySaved;

    private enum AdminTab
    {
        Roles,
        Retention,
        Quality,
        Integrations,
        Audit,
    }

    [SupplyParameterFromQuery(Name = "tab")]
    public string? QueryTab { get; set; }

    [Inject] private AuditLog Audit { get; set; } = default!;

    [Inject] private QualitySettingsStore QualityStore { get; set; } = default!;

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
        if (tab == AdminTab.Quality && _tab != AdminTab.Quality)
            ShowSavedQuality();
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

    private void ShowSavedQuality()
    {
        _quality = QualityForm.From(QualityStore.Current);
        _qualityErrors.Clear();
        _qualitySaved = false;
    }

    /// <summary>Fills the form with the defaults; nothing changes until it is saved.</summary>
    private void QualityDefaults()
    {
        _quality = QualityForm.From(QualitySettings.Default);
        _qualityErrors.Clear();
        _qualitySaved = false;
    }

    private void SetRequired(RequiredMetadata field, bool required)
    {
        if (required)
            _quality.Required.Add(field);
        else
            _quality.Required.Remove(field);
        _qualitySaved = false;
    }

    private void SaveQuality()
    {
        _qualityErrors.Clear();
        _qualitySaved = false;
        if (!IsThreshold(_quality.MinDocument))
            _qualityErrors[MinDocumentField] = Text["QualitySettings.Range"];
        if (!IsThreshold(_quality.MinPage))
            _qualityErrors[MinPageField] = Text["QualitySettings.Range"];
        if (_qualityErrors.Count > 0)
            return;

        QualityStore.Save(_quality.ToSettings(), CurrentUser);
        _quality = QualityForm.From(QualityStore.Current);
        _qualitySaved = true;
    }

    private static bool IsThreshold(double? value) =>
        value is { } number && double.IsFinite(number)
        && number >= QualitySettings.MinThreshold && number <= QualitySettings.MaxThreshold;

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
        AuditAction.EditedInWord => Text["Audit.EditedInWord", entry.Version ?? "?"],
        AuditAction.Downloaded => Text["Audit.Downloaded", entry.Detail ?? "?", entry.Version ?? "?"],
        AuditAction.Exported => Text["Audit.Exported", entry.Detail ?? "?"],
        AuditAction.QualityChecked when entry.Status == DocumentStatus.Archived => Text["Audit.QualityApproved"],
        AuditAction.QualityChecked when entry.Detail == "0" => Text["Audit.QualityPassed"],
        AuditAction.QualityChecked => Text["Audit.QualityFlagged", entry.Detail ?? "?"],
        AuditAction.QualitySettingsChanged => Text["Audit.QualitySettingsChanged"],
        _ => entry.Action.ToString(),
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Administration UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);

    private sealed record RoleRow(string Key, string Group, int Users);

    /// <param name="Years">Null keeps the records permanently.</param>
    private sealed record RetentionRule(DocumentDepartment Department, DocumentKind Kind, int? Years, string From, string Then);

    /// <summary>The quality check settings while they are edited; thresholds are null when the box is empty.</summary>
    private sealed class QualityForm
    {
        public double? MinDocument { get; set; }

        public double? MinPage { get; set; }

        public bool FlagWarnings { get; set; }

        public bool FlagEmptyPages { get; set; }

        public HashSet<RequiredMetadata> Required { get; } = [];

        public bool AutoApprove { get; set; }

        public static QualityForm From(QualitySettings settings)
        {
            var form = new QualityForm
            {
                MinDocument = settings.MinDocumentConfidence,
                MinPage = settings.MinPageConfidence,
                FlagWarnings = settings.FlagWarnings,
                FlagEmptyPages = settings.FlagEmptyPages,
                AutoApprove = settings.AutoApprove,
            };
            form.Required.UnionWith(settings.RequiredMetadata);
            return form;
        }

        public QualitySettings ToSettings() => new(
            Math.Round(MinDocument!.Value, 1),
            Math.Round(MinPage!.Value, 1),
            FlagWarnings,
            FlagEmptyPages,
            [.. Required.Order()],
            AutoApprove);
    }
}
