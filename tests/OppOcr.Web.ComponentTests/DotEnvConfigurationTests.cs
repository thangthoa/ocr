using OppOcr.Web.Extensions;

namespace OppOcr.Web.ComponentTests;

public sealed class DotEnvConfigurationTests
{
    [Fact]
    public void Parses_keys_values_quotes_and_skips_comments_and_empty_values()
    {
        string[] lines =
        [
            "# comment",
            "",
            "OCR_API_BASE_URL=http://n2.ckey.vn:1436",
            "export OCR_API_KEY=\"abc=def\"",
            "POSTGRES_PORT = 5440",
            "EMPTY=",
            "not a pair",
        ];

        var values = DotEnvConfiguration.Parse(lines).ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["OCR_API_BASE_URL"] = "http://n2.ckey.vn:1436",
                ["OCR_API_KEY"] = "abc=def",
                ["POSTGRES_PORT"] = "5440",
            },
            values);
    }
}
