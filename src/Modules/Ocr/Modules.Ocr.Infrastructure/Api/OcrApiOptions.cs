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
    public const string AppUrlVariable = "OCR_APP_URL";

    /// <summary>The name the OCR service's guide uses; its value is handed over separately by the AI team.</summary>
    public const string CallbackSecretVariable = "OCR_CALLBACK_SECRET";

    public Uri? BaseUrl { get; set; }

    /// <summary>Kept server-side only (user-secrets / environment); never sent to the browser.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Uploading up to 25 MB to the service can take a while on a slow link. With <see cref="AppUrl"/> the
    /// service downloads the file during this request instead (about 5 minutes for 25 MB).
    /// </summary>
    [Range(10, 1800)]
    public int RequestTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Where the OCR service reaches this application (https; the service only allows hosts it was told
    /// about). When set, the service downloads each uploaded file from a short-lived link instead of
    /// receiving it in the request (ADR 0006). When not set, files are uploaded as before.
    /// </summary>
    public Uri? AppUrl { get; set; }

    /// <summary>
    /// Shared with the OCR service, which signs the results it posts to this application. Callbacks are
    /// requested only when this and <see cref="AppUrl"/> are set. Server-side only.
    /// </summary>
    public string? CallbackSecret { get; set; }

    /// <summary>The service's guide suggests links that expire after about 15 minutes.</summary>
    [Range(1, 60)]
    public int FileLinkLifetimeMinutes { get; set; } = 15;

    public bool CallbacksEnabled => AppUrl is not null && !string.IsNullOrEmpty(CallbackSecret);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BaseUrl is null || !BaseUrl.IsAbsoluteUri)
            yield return new ValidationResult($"{BaseUrlVariable} (Ocr:Api:BaseUrl) must be an absolute URL.", [nameof(BaseUrl)]);
        if (string.IsNullOrWhiteSpace(ApiKey))
            yield return new ValidationResult($"{ApiKeyVariable} (Ocr:Api:ApiKey) is required.", [nameof(ApiKey)]);
        if (AppUrl is not null && (!AppUrl.IsAbsoluteUri || AppUrl.Scheme != Uri.UriSchemeHttps))
            yield return new ValidationResult($"{AppUrlVariable} (Ocr:Api:AppUrl) must be an absolute https URL.", [nameof(AppUrl)]);
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
