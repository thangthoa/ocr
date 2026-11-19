using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Ocr.Infrastructure.Api;

namespace Modules.Ocr.Infrastructure.Tests;

public sealed class OcrConfigurationTests
{
    [Fact]
    public void Flat_env_variables_override_appsettings()
    {
        var configuration = Build(new()
        {
            ["Ocr:Api:BaseUrl"] = "http://from-appsettings:1",
            ["OCR_API_BASE_URL"] = "http://n2.ckey.vn:1436",
            ["OCR_API_KEY"] = "k",
        });

        var options = Resolve(configuration);

        Assert.Equal(new Uri("http://n2.ckey.vn:1436"), options.BaseUrl);
        Assert.Equal("k", options.ApiKey);
    }

    [Fact]
    public void Hierarchical_keys_still_work_without_aliases()
    {
        var configuration = Build(new() { ["Ocr:Api:BaseUrl"] = "http://x:1", ["Ocr:Api:ApiKey"] = "k" });

        Assert.Equal(new Uri("http://x:1"), Resolve(configuration).BaseUrl);
    }

    [Fact]
    public void Missing_key_fails_validation()
    {
        var configuration = Build(new() { ["OCR_API_BASE_URL"] = "http://x:1" });

        Assert.Throws<OptionsValidationException>(() => Resolve(configuration));
    }

    private static IConfiguration Build(Dictionary<string, string?> values)
    {
        values["Ocr:Worker:Enabled"] = "false";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static OcrApiOptions Resolve(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddOcrInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<OcrApiOptions>>().Value;
    }
}
