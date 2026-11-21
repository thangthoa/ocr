using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using OppOcr.Web.Features.Mockups.Access;
using OppOcr.Web.Features.Mockups.Activity;
using OppOcr.Web.Features.Mockups.Components;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.History;

namespace OppOcr.Web.Features.Mockups.Pages;

/// <summary>
/// Activity log mockup: a month of sample history plus what was done in this app, filtered, a page at a time, with
/// the integrity of the hash chain checked on every load. Filters can come from the address
/// (<c>?user=admin&amp;category=Security&amp;result=Failed&amp;from=2026-11-01&amp;to=2026-11-20&amp;q=…</c>).
/// </summary>
public sealed partial class ActivityLogPage
{
    private readonly TablePaging _paging = new();
    private readonly HashSet<long> _expanded = [];
    private IReadOnlyList<ActivityRow> _rows = [];
    private IReadOnlyList<string> _users = [];
    private long? _brokenAt;
    private DateTime _checkedAtUtc;
    private string? _text;
    private string? _user;
    private ActivityCategory? _category;
    private ActivityResult? _result;
    private DateOnly? _from;
    private DateOnly? _to;

    [SupplyParameterFromQuery(Name = "q")]
    public string? QueryText { get; set; }

    [SupplyParameterFromQuery(Name = "user")]
    public string? QueryUser { get; set; }

    [SupplyParameterFromQuery(Name = "category")]
    public string? QueryCategory { get; set; }

    [SupplyParameterFromQuery(Name = "result")]
    public string? QueryResult { get; set; }

    [SupplyParameterFromQuery(Name = "from")]
    public string? QueryFrom { get; set; }

    [SupplyParameterFromQuery(Name = "to")]
    public string? QueryTo { get; set; }

    [Inject] private ActivityTrail Trail { get; set; } = default!;

    [Inject] private UserDirectory Accounts { get; set; } = default!;

    [Inject] private ProjectHistory History { get; set; } = default!;

    [Inject] private TimeProvider Time { get; set; } = default!;

    [Inject] private IStringLocalizer<MockupText> Text { get; set; } = default!;

    private ActivityQuery Query => new(_text, string.IsNullOrEmpty(_user) ? null : _user, _category, _result, _from, _to);

    private IReadOnlyList<ActivityRow> Filtered => Trail.Filter(_rows, Query);

    protected override void OnInitialized()
    {
        _paging.Resize(TablePaging.Sizes[1]);
        Load();
    }

    protected override void OnParametersSet()
    {
        var query = ActivityEndpoints.ParseQuery(QueryText, QueryUser, QueryCategory, QueryResult, QueryFrom, QueryTo);
        (_text, _user, _category, _result, _from, _to) = (query.Text, query.User, query.Category, query.Result, query.From, query.To);
        _paging.Reset();
    }

    /// <summary>Reads the log again (new entries included) and checks the whole hash chain.</summary>
    private void Load()
    {
        _rows = Trail.Load();
        _users = _rows.Select(r => r.Entry.User).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        _brokenAt = ActivityTrail.FirstBrokenLink(_rows);
        _checkedAtUtc = Time.GetUtcNow().UtcDateTime;
    }

    /// <summary>A summary figure shows the entries behind it.</summary>
    private void Show(ActivityCategory? category, ActivityResult? result)
    {
        (_category, _result) = (category, result);
        _paging.Reset();
    }

    private void Clear()
    {
        (_text, _user, _category, _result, _from, _to) = (null, null, null, null, null, null);
        _paging.Reset();
    }

    private void Toggle(long sequence)
    {
        if (!_expanded.Remove(sequence))
            _expanded.Add(sequence);
    }

    private string? DisplayName(string userName) => Accounts.Find(userName)?.DisplayName;

    /// <summary>
    /// A link only where the document can be opened: this app's own entries, and the history documents of the sample
    /// month when the history is on (otherwise they exist only in the log).
    /// </summary>
    private bool CanOpen(ActivityRow row) =>
        row.Entry.Action != AuditAction.Deleted && (!row.IsSample || History.Contains(row.Entry.DocumentId));

    private static string ResultClass(ActivityResult result) => result switch
    {
        ActivityResult.Success => "tag-green",
        ActivityResult.Failed => "tag-red",
        _ => "tag-magenta",
    };
}
