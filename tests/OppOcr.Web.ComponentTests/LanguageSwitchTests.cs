using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Components.Layout;

namespace OppOcr.Web.ComponentTests;

/// <summary>The language dropdown in the header: every language, the current one marked.</summary>
public sealed class LanguageSwitchTests : BunitContext
{
    public LanguageSwitchTests()
    {
        Services.AddLocalization();
    }

    [Fact]
    public void Lists_every_language_and_marks_the_current_one()
    {
        TestServices.SetCulture("en");
        Services.GetRequiredService<NavigationManager>().NavigateTo("documents?status=Failed");

        var cut = Render<LanguageSwitch>();

        Assert.Contains("English", cut.Find("details.popup-menu > summary").TextContent, StringComparison.Ordinal);
        var items = cut.FindAll(".language-panel a.language-item");
        Assert.Equal(
            [("en", "English", "true"), ("ar", "العربية", null)],
            items.Select(a => (a.GetAttribute("lang"), a.TextContent.Trim(), a.GetAttribute("aria-current"))));
        Assert.Equal(
            "culture/set?culture=ar&redirectUri=%2Fdocuments%3Fstatus%3DFailed",
            items[1].GetAttribute("href"));
    }

    [Fact]
    public void Shows_arabic_as_current_on_an_arabic_page()
    {
        TestServices.SetCulture("ar");

        var cut = Render<LanguageSwitch>();

        Assert.Contains("العربية", cut.Find("summary").TextContent, StringComparison.Ordinal);
        Assert.Equal("ar", cut.Find("a[aria-current=true]").GetAttribute("lang"));
    }
}
