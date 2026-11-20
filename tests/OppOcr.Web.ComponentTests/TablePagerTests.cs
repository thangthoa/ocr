using Bunit;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups.Components;

namespace OppOcr.Web.ComponentTests;

/// <summary>The pager under every list table: page sizes 10/20/50/100 and the page buttons.</summary>
public sealed class TablePagerTests : BunitContext
{
    public TablePagerTests()
    {
        TestServices.SetCulture("en");
        Services.AddLocalization();
    }

    [Fact]
    public void A_new_page_size_keeps_the_first_row_of_the_page_on_screen()
    {
        var paging = new TablePaging();
        paging.GoTo(5, total: 103); // rows 41–50

        paging.Resize(20);
        Assert.Equal(3, paging.Page); // rows 41–60

        paging.Resize(30); // not offered
        Assert.Equal(20, paging.Size);

        paging.GoTo(99, total: 103);
        Assert.Equal(6, paging.Page);
        Assert.Equal(Enumerable.Range(101, 3), paging.Slice(Enumerable.Range(1, 103)));
    }

    [Fact]
    public void Offers_the_four_sizes_and_shows_the_rows_on_screen()
    {
        var paging = new TablePaging();
        paging.GoTo(5, total: 103);

        var cut = Render<TablePager>(p => p.Add(x => x.Paging, paging).Add(x => x.Total, 103).Add(x => x.Id, "t"));

        Assert.Equal(["10", "20", "50", "100"], cut.FindAll("#t-size option").Select(o => o.TextContent));
        Assert.Equal("41–50 of 103", cut.Find("#t-range").TextContent);
        Assert.Equal(["1", "…", "4", "5", "6", "…", "11"], PageButtons(cut));
        Assert.Equal("page", cut.Find("button[aria-label='Page 5']").GetAttribute("aria-current"));
    }

    [Fact]
    public void Pages_and_sizes_change_from_the_pager()
    {
        var paging = new TablePaging();
        var changes = 0;
        var cut = Render<TablePager>(p => p.Add(x => x.Paging, paging).Add(x => x.Total, 103).Add(x => x.Id, "t")
            .Add(x => x.OnChange, () => changes++));

        Assert.True(cut.Find(".pager-previous").HasAttribute("disabled"));
        Assert.Equal(["1", "2", "3", "4", "…", "11"], PageButtons(cut));

        cut.Find(".pager-next").Click();
        Assert.Equal(2, paging.Page);

        cut.Find("button[aria-label='Page 11']").Click();
        Assert.True(cut.Find(".pager-next").HasAttribute("disabled"));
        Assert.Equal("101–103 of 103", cut.Find("#t-range").TextContent);

        cut.Find("#t-size").Change("100");
        Assert.Equal((2, 100), (paging.Page, paging.Size));
        Assert.Equal(3, changes);
    }

    [Fact]
    public void Shows_nothing_for_an_empty_list_and_no_page_buttons_for_one_page()
    {
        Assert.Empty(Render<TablePager>(p => p.Add(x => x.Paging, new TablePaging()).Add(x => x.Total, 0).Add(x => x.Id, "t")).Markup.Trim());

        var single = Render<TablePager>(p => p.Add(x => x.Paging, new TablePaging()).Add(x => x.Total, 7).Add(x => x.Id, "t"));
        Assert.Equal("1–7 of 7", single.Find("#t-range").TextContent);
        Assert.Empty(single.FindAll(".pager-pages"));
    }

    private static List<string> PageButtons(IRenderedComponent<TablePager> cut) =>
        [.. cut.FindAll(".pager-page, .pager-gap").Select(e => e.TextContent.Trim())];
}
