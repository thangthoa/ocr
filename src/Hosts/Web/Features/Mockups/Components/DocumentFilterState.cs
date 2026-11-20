using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.Components;

/// <summary>What <see cref="DocumentFilters"/> holds: a metadata search, department, type and a document date range.</summary>
public sealed class DocumentFilterState
{
    public string Search { get; set; } = string.Empty;

    public DocumentSearchField Field { get; set; } = DocumentSearchField.All;

    public DocumentDepartment? Department { get; set; }

    public DocumentKind? Kind { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public bool HasValues => !string.IsNullOrWhiteSpace(Search) || Field != DocumentSearchField.All
        || Department is not null || Kind is not null || From is not null || To is not null;

    public DocumentQuery ToQuery(DocumentStatus? status = null) => new(Search, Field, status, Department, Kind, From, To);

    public void Clear()
    {
        Search = string.Empty;
        Field = DocumentSearchField.All;
        Department = null;
        Kind = null;
        From = null;
        To = null;
    }
}
