using OppOcr.Web.Features.Mockups.Data;

namespace OppOcr.Web.Features.Mockups.History;

/// <summary>
/// The free-text search of the document lists, over the details of the simulated documents: id, file name, title,
/// reference and keywords, written into a stack buffer so a million documents can be searched as people type. Their
/// text is not searched (it is only made up when a document is opened).
/// </summary>
internal sealed class HistorySearch
{
    private const string Extension = ".pdf";

    private readonly string _term;
    private readonly DocumentSearchField _field;
    private readonly bool[][] _keywords;

    private HistorySearch(string term, DocumentSearchField field)
    {
        _term = term;
        _field = field;
        _keywords = HistoryText.KeywordMatches(term);
    }

    /// <summary>Null when there is nothing to search for.</summary>
    public static HistorySearch? For(string? text, DocumentSearchField field) =>
        string.IsNullOrWhiteSpace(text) ? null : new HistorySearch(text.Trim(), field);

    /// <summary>False for a search in the text only, which the simulated documents never match.</summary>
    public bool CanMatch => _field != DocumentSearchField.Content;

    public bool Matches(int index, in HistoryEntry entry) => _field switch
    {
        DocumentSearchField.FileName => HasFileName(index, entry) || HasId(index),
        DocumentSearchField.Title => HasTitle(index, entry),
        DocumentSearchField.Reference => HasReference(index, entry),
        DocumentSearchField.Keyword => HistoryText.HasKeyword(index, entry.Kind, _keywords),
        DocumentSearchField.Content => false,
        // The reference is the file name without its extension, so the file name covers both.
        _ => HasFileName(index, entry) || HasId(index) || HasTitle(index, entry) || HistoryText.HasKeyword(index, entry.Kind, _keywords),
    };

    private bool HasId(int index)
    {
        Span<char> buffer = stackalloc char[16];
        return Has(buffer[..HistoryText.WriteId(buffer, index)]);
    }

    private bool HasReference(int index, in HistoryEntry entry)
    {
        Span<char> buffer = stackalloc char[24];
        return Has(buffer[..HistoryText.WriteReference(buffer, index, entry.Department, entry.Kind)]);
    }

    private bool HasFileName(int index, in HistoryEntry entry)
    {
        Span<char> buffer = stackalloc char[32];
        var length = HistoryText.WriteReference(buffer, index, entry.Department, entry.Kind);
        Extension.AsSpan().CopyTo(buffer[length..]);
        return Has(buffer[..(length + Extension.Length)]);
    }

    private bool HasTitle(int index, in HistoryEntry entry)
    {
        Span<char> buffer = stackalloc char[160];
        return Has(buffer[..HistoryText.WriteTitle(buffer, index, entry.Kind)]);
    }

    private bool Has(ReadOnlySpan<char> value) => value.Contains(_term, StringComparison.OrdinalIgnoreCase);
}
