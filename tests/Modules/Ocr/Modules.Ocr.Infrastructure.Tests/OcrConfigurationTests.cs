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

    [Fact]
    public void File_links_and_callbacks_are_off_unless_configured()
    {
        var options = Resolve(Build(new() { ["OCR_API_BASE_URL"] = "http://x:1", ["OCR_API_KEY"] = "k" }));

        Assert.Null(options.AppUrl);
        Assert.False(options.CallbacksEnabled);
        Assert.Equal(15, options.FileLinkLifetimeMinutes);
    }

    [Fact]
    public void App_url_and_callback_secret_come_from_flat_env_variables()
    {
        var options = Resolve(Build(new()
        {
            ["OCR_API_BASE_URL"] = "http://x:1",
            ["OCR_API_KEY"] = "k",
            ["OCR_APP_URL"] = "https://ocr.dc1.merket.io",
            ["OCR_CALLBACK_SECRET"] = "s",
        }));

        Assert.Equal(new Uri("https://ocr.dc1.merket.io"), options.AppUrl);
        Assert.Equal("s", options.CallbackSecret);
        Assert.True(options.CallbacksEnabled);
    }

    [Fact]
    public void Plain_http_app_url_fails_validation()
    {
        // The OCR service only downloads from, and calls back to, https addresses.
        var configuration = Build(new() { ["OCR_API_BASE_URL"] = "http://x:1", ["OCR_API_KEY"] = "k", ["OCR_APP_URL"] = "http://ocr.dc1.merket.io" });

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
