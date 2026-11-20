using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Kpi;
using OppOcr.Web.Features.Mockups.Quality;
using OppOcr.Web.Features.Mockups.Reports;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Administration mockup. Users, role permissions, quality and KPI settings are kept in memory; retention is sample
/// data; the OCR service status is a real health check and the activity log records what was done in this app since
/// it started.
/// </summary>
public sealed partial class AdminPage : IDisposable
{
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
    private const string ProjectPagesField = "project-pages";
    private const string ProjectStartField = "project-start";
    private const string ProjectEndField = "project-end";

    private readonly CancellationTokenSource _disposed = new();
    private readonly Dictionary<string, string> _qualityErrors = [];
    private AdminTab _tab = AdminTab.Retention;
    private HealthStatus? _ocrHealth;
    private bool _checking;
    private DocumentList? _documents;
    private QualityForm _quality = new();
    private bool _qualitySaved;
    private readonly Dictionary<string, string> _kpiErrors = [];
    private KpiForm _kpi = new();
    private bool _kpiSaved;

    private enum AdminTab
    {
        Retention,
        Quality,
        Kpi,
        Integrations,
    }

    [SupplyParameterFromQuery(Name = "tab")]
    public string? QueryTab { get; set; }

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Inject] private QualitySettingsStore QualityStore { get; set; } = default!;

    [Inject] private KpiSettingsStore KpiStore { get; set; } = default!;

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
        // Users, roles and the activity log moved to their own menu; old links still lead there.
        if (MovedTab(QueryTab) is { } moved)
        {
            Navigation.NavigateTo(moved, replace: true);
            return;
        }

        // The first tab (retention) counts documents, so it loads them even when it opens by default.
        await SelectAsync(Enum.TryParse<AdminTab>(QueryTab, ignoreCase: true, out var tab) && Enum.IsDefined(tab) ? tab : _tab);
    }

    private async Task SelectAsync(AdminTab tab)
    {
        if (tab == AdminTab.Quality && _tab != AdminTab.Quality)
            ShowSavedQuality();
        if (tab == AdminTab.Kpi && (_tab != AdminTab.Kpi || _kpi.Rows.Count == 0))
            ShowSavedKpis();
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

    private void ShowSavedKpis()
    {
        _kpi = KpiForm.From(KpiStore.Current);
        _kpiErrors.Clear();
        _kpiSaved = false;
    }

    /// <summary>Fills the form with the defaults; nothing changes until it is saved.</summary>
    private void KpiDefaults()
    {
        _kpi = KpiForm.From(KpiStore.Defaults);
        _kpiErrors.Clear();
        _kpiSaved = false;
    }

    private void SaveKpis()
    {
        _kpiErrors.Clear();
        _kpiSaved = false;
        foreach (var row in _kpi.Rows)
        {
            var (targetError, alertError) = KpiRules.Check(row.Kind, row.Target, row.Alert);
            if (targetError is { } target)
                _kpiErrors[TargetField(row.Kind)] = Describe(row.Kind, target);
            if (alertError is { } alert)
                _kpiErrors[AlertField(row.Kind)] = Describe(row.Kind, alert);
        }

        if (KpiRules.Check(_kpi.TotalPages, _kpi.Start, _kpi.End) is { } projectError)
        {
            var field = projectError switch
            {
                ProjectPlanError.PagesOutOfRange => ProjectPagesField,
                ProjectPlanError.DatesMissing when _kpi.Start is null => ProjectStartField,
                _ => ProjectEndField,
            };
            _kpiErrors[field] = projectError == ProjectPlanError.PagesOutOfRange
                ? Text["KpiSettings.Error.Pages", MockupTextExtensions.FormatNumber(KpiRules.MaxProjectPages)]
                : Text[$"KpiSettings.Error.{projectError}"];
        }

        if (_kpiErrors.Count > 0)
            return;

        KpiStore.Save(_kpi.ToSettings(), CurrentUser);
        _kpi = KpiForm.From(KpiStore.Current);
        _kpiSaved = true;
    }

    private string Describe(KpiKind kind, KpiTargetError error) => error switch
    {
        KpiTargetError.OutOfRange => Text["KpiSettings.Error.Range", Text.KpiValue(kind, KpiRules.MaxValue(KpiRules.Unit(kind)))],
        _ => Text[$"KpiSettings.Error.{error}"],
    };

    private static string TargetField(KpiKind kind) => $"kpi-{kind}-target";

    private static string AlertField(KpiKind kind) => $"kpi-{kind}-alert";

    private string? ErrorId(string field) => _kpiErrors.ContainsKey(field) ? field + "-error" : null;

    private static bool IsThreshold(double? value) =>
        value is { } number && double.IsFinite(number)
        && number >= QualitySettings.MinThreshold && number <= QualitySettings.MaxThreshold;

    private string DocumentsUnder(RetentionRule rule) =>
        _documents is null
            ? MockupTextExtensions.Missing
            : MockupTextExtensions.FormatNumber(_documents.Documents.Count(d => d.Metadata.Department == rule.Department && d.Metadata.Kind == rule.Kind));

    private static string? MovedTab(string? tab) => tab?.ToUpperInvariant() switch
    {
        "USERS" => "users",
        "ROLES" => "users/roles",
        "AUDIT" => "users/activity",
        _ => null,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Administration UI: failed to {Operation}")]
    private static partial void LogUnexpected(ILogger logger, Exception exception, string operation);

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

    /// <summary>The KPI targets and the project plan while they are edited; numbers are null when the box is empty.</summary>
    private sealed class KpiForm
    {
        public List<KpiRow> Rows { get; } = [];

        public int? TotalPages { get; set; }

        public DateOnly? Start { get; set; }

        public DateOnly? End { get; set; }

        public static KpiForm From(KpiSettings settings)
        {
            var form = new KpiForm
            {
                TotalPages = settings.Project.TotalPages,
                Start = settings.Project.Start,
                End = settings.Project.End,
            };
            form.Rows.AddRange(settings.Targets.Select(t => new KpiRow
            {
                Kind = t.Kind,
                Target = t.Target,
                Alert = t.Alert,
                Show = t.ShowOnDashboard,
            }));
            return form;
        }

        public KpiSettings ToSettings() => new(
            [.. Rows.Select(r => new KpiTarget(r.Kind, Math.Round(r.Target!.Value, 1), Math.Round(r.Alert!.Value, 1), r.Show))],
            new ProjectPlan(TotalPages!.Value, Start!.Value, End!.Value));
    }

    private sealed class KpiRow
    {
        public KpiKind Kind { get; init; }

        public double? Target { get; set; }

        public double? Alert { get; set; }

        public bool Show { get; set; }
    }
}
