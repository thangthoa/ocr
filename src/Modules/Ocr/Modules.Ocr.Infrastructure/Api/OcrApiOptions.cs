using System.ComponentModel.DataAnnotations;

namespace Modules.Ocr.Infrastructure.Api;

internal sealed class OcrApiOptions : IValidatableObject
{
    public const string SectionName = "Ocr:Api";

    /// <summary>
    /// Flat environment variable names (as used in <c>.env</c>) accepted in addition to the
    /// hierarchical <c>Ocr__Api__*</c> keys. When set, they win over appsettings.
    /// </summary>
    public const string BaseUrlVariable = "OCR_API_BASE_URL";
    public const string ApiKeyVariable = "OCR_API_KEY";

    public Uri? BaseUrl { get; set; }

    /// <summary>Kept server-side only (user-secrets / environment); never sent to the browser.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Uploading up to 25 MB to the service can take a while on a slow link.</summary>
    [Range(10, 1800)]
    public int RequestTimeoutSeconds { get; set; } = 300;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri)
            yield return new ValidationResult($"{BaseUrlVariable} (Ocr:Api:BaseUrl) must be an absolute URL.", [nameof(BaseUrl)]);
        if (string.IsNullOrWhiteSpace(ApiKey))
            yield return new ValidationResult($"{ApiKeyVariable} (Ocr:Api:ApiKey) is required.", [nameof(ApiKey)]);
    }
}

internal sealed class OcrWorkerOptions
{
    public const string SectionName = "Ocr:Worker";

    public bool Enabled { get; set; } = true;

    /// <summary>The service guide suggests polling every 2–3 seconds.</summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>How many recent jobs the in-memory list keeps until the service can list jobs.</summary>
    [Range(10, 10000)]
    public int RegistryCapacity { get; set; } = 500;
}
