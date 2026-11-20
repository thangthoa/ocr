using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Mockups.Data;
using OppOcr.Web.Features.Mockups.Pages;

namespace OppOcr.Web.ComponentTests;

/// <summary>The app lists only real OCR jobs: the sample documents are off unless configured.</summary>
public sealed class SampleDocumentsTests : BunitContext
{
    [Fact]
    public void Without_configuration_the_store_has_no_sample_documents()
    {
        var services = new ServiceCollection().AddLogging().AddLocalization();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(new TestHostEnvironment(Path.GetTempPath()));
        services.AddMockups(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetRequiredService<DocumentStore>().Samples());
        Assert.False(new DocumentSampleOptions().Enabled);
    }

    [Fact]
    public void With_samples_off_the_lists_show_no_sample_documents()
    {
        TestServices.SetCulture("en");
        Services.AddOcrAndDocuments(new Dictionary<string, string?> { [TestServices.SamplesEnabled] = "false" });

        var documents = Render<DocumentsPage>();
        documents.WaitForAssertion(() => Assert.Contains("0 document(s)", documents.Markup, StringComparison.Ordinal));
        Assert.Empty(documents.FindAll("tbody tr"));
        Assert.DoesNotContain(">Sample<", documents.Markup, StringComparison.Ordinal);
        Assert.Contains("No documents yet.", documents.Find(".empty-state").TextContent, StringComparison.Ordinal);

        var review = Render<ReviewPage>();
        review.WaitForAssertion(() => Assert.NotNull(review.Find(".empty-state")));

        var archive = Render<ApprovedPage>();
        archive.WaitForAssertion(() => Assert.Contains("No archived documents yet.", archive.Markup, StringComparison.Ordinal));
    }
}
