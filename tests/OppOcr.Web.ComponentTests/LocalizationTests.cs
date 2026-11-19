using System.Collections;
using System.Globalization;
using System.Resources;
using Modules.Ocr.Application.Contracts;
using OppOcr.Web.Features.Mockups;
using OppOcr.Web.Features.Ocr;
using OppOcr.Web.Localization;

namespace OppOcr.Web.ComponentTests;

public sealed class LocalizationTests
{
    public static TheoryData<string> ResourceTypes => [typeof(SharedText).FullName!, typeof(OcrText).FullName!, typeof(MockupText).FullName!];

    [Theory]
    [MemberData(nameof(ResourceTypes))]
    public void Every_supported_culture_defines_exactly_the_default_keys(string resourceName)
    {
        var manager = new ResourceManager(resourceName, typeof(OcrText).Assembly);
        var defaultKeys = Keys(manager, CultureInfo.InvariantCulture);

        Assert.NotEmpty(defaultKeys);
        foreach (var culture in LocalizationSetup.SupportedCultures.Where(c => c != LocalizationSetup.DefaultCulture))
            Assert.Equal(defaultKeys, Keys(manager, new CultureInfo(culture)));
    }

    [Fact]
    public void Every_ocr_error_code_has_a_translation()
    {
        var manager = new ResourceManager(typeof(OcrText).FullName!, typeof(OcrText).Assembly);
        var keys = Keys(manager, CultureInfo.InvariantCulture);
        var codes = typeof(OcrErrorCodes).GetFields().Select(f => (string)f.GetRawConstantValue()!);

        Assert.All(codes, code => Assert.Contains(code, keys));
    }

    [Fact]
    public void Every_job_failure_code_has_a_translation()
    {
        var manager = new ResourceManager(typeof(OcrText).FullName!, typeof(OcrText).Assembly);
        var keys = Keys(manager, CultureInfo.InvariantCulture);
        string[] serviceCodes = ["unauthorized", "not_found", "file_too_large", "too_many_pages", "unsupported_file", "bad_image", "invalid_request", "internal_error", "processing_error"];
        string[] moduleCodes = [OcrServiceErrorCodes.ServiceUnavailable, OcrServiceErrorCodes.RemoteJobLost];

        Assert.All(serviceCodes.Concat(moduleCodes), code => Assert.Contains($"JobError.{code}", keys));
    }

    [Fact]
    public void Supported_languages_are_english_by_default_and_arabic()
    {
        Assert.Equal(["en", "ar"], LocalizationSetup.SupportedCultures);
        Assert.Equal("en", LocalizationSetup.DefaultCulture);
    }

    [Fact]
    public void Arabic_is_right_to_left_and_uses_the_gregorian_calendar()
    {
        var arabic = LocalizationSetup.CreateCulture("ar");

        Assert.True(arabic.TextInfo.IsRightToLeft);
        Assert.IsType<GregorianCalendar>(arabic.DateTimeFormat.Calendar);
        Assert.Contains("2026", new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc).ToString("g", arabic), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/", true)]
    [InlineData("/images?page=2", true)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("https://evil.example", false)]
    [InlineData("images", false)]
    [InlineData(null, false)]
    public void Culture_redirect_only_allows_local_paths(string? uri, bool expected) =>
        Assert.Equal(expected, LocalizationSetup.IsLocalPath(uri));

    private static SortedSet<string> Keys(ResourceManager manager, CultureInfo culture)
    {
        using var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for '{culture.Name}'.");
        return new SortedSet<string>(set.Cast<DictionaryEntry>().Select(e => (string)e.Key), StringComparer.Ordinal);
    }
}
