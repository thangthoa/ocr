namespace OppOcr.Web.Features.Mockups.Components;

/// <summary>Which rows of a list a table shows: the page, and how many rows a page has (one of <see cref="Sizes"/>).</summary>
public sealed class TablePaging
{
    public static readonly IReadOnlyList<int> Sizes = [10, 20, 50, 100];

    public int Page { get; private set; } = 1;

    public int Size { get; private set; } = Sizes[0];

    public int PageCount(int total) => Math.Max(1, (total + Size - 1) / Size);

    public IEnumerable<T> Slice<T>(IEnumerable<T> items) => items.Skip((Page - 1) * Size).Take(Size);

    public void GoTo(int page, int total) => Page = Math.Clamp(page, 1, PageCount(total));

    /// <summary>A new size keeps the first row of the current page on screen. Sizes not offered are ignored.</summary>
    public void Resize(int size)
    {
        if (!Sizes.Contains(size))
            return;

        var firstRow = (Page - 1) * Size;
        Size = size;
        Page = (firstRow / size) + 1;
    }

    /// <summary>Back to the first page, for a new filter.</summary>
    public void Reset() => Page = 1;

    /// <summary>Stays on the current page unless the list got shorter than that.</summary>
    public void Clamp(int total) => Page = Math.Min(Page, PageCount(total));
}
