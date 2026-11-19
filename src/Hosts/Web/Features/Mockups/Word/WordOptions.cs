using System.ComponentModel.DataAnnotations;

namespace OppOcr.Web.Features.Mockups.Word;

/// <summary>
/// The OnlyOffice Docs server that edits Word files in the browser (ADR 0004). Editing is offered
/// only when <see cref="Enabled"/>: the rest of the app works without a document server.
/// </summary>
public sealed class WordEditorOptions : IValidatableObject
{
    public const string SectionName = "Documents:Editor";

    // Flat names as used in .env files; they win over appsettings, like OCR_API_*.
    public const string PublicUrlVariable = "DOCS_EDITOR_PUBLIC_URL";
    public const string InternalUrlVariable = "DOCS_EDITOR_INTERNAL_URL";
    public const string AppUrlVariable = "DOCS_EDITOR_APP_URL";
    public const string JwtSecretVariable = "DOCS_EDITOR_JWT_SECRET";

    /// <summary>OnlyOffice's HS256 tokens need a long secret; shorter ones are refused.</summary>
    public const int MinSecretLength = 32;

    /// <summary>Where browsers reach the document server (its api.js is loaded from here).</summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>Where this app reaches the document server; defaults to <see cref="PublicUrl"/>.</summary>
    public Uri? InternalUrl { get; set; }

    /// <summary>Where the document server reaches this app, to fetch files and post saves.</summary>
    public Uri? AppUrl { get; set; }

    /// <summary>Shared with the document server (its JWT_SECRET). Server-side only.</summary>
    public string? JwtSecret { get; set; }

    /// <summary>How long the signed links handed to the document server stay valid.</summary>
    [Range(5, 1440)]
    public int LinkLifetimeMinutes { get; set; } = 240;

    /// <summary>Largest edited file accepted back from the document server.</summary>
    [Range(1, 500)]
    public int MaxFileSizeMegabytes { get; set; } = 50;

    public bool Enabled => PublicUrl is not null && AppUrl is not null && !string.IsNullOrWhiteSpace(JwtSecret);

    /// <summary>Null until configured. A plain property: options validation reads every property.</summary>
    public Uri? DocumentServerUrl => InternalUrl ?? PublicUrl;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(JwtSecret) && JwtSecret.Length < MinSecretLength)
            yield return new ValidationResult($"{JwtSecretVariable} must be at least {MinSecretLength} characters.", [nameof(JwtSecret)]);
        foreach (var (url, name) in new[] { (PublicUrl, PublicUrlVariable), (InternalUrl, InternalUrlVariable), (AppUrl, AppUrlVariable) })
        {
            if (url is not null && (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https")))
                yield return new ValidationResult($"{name} must be an absolute http(s) URL.");
        }
    }
}

/// <summary>Where edited Word files are kept (a Docker volume in deployments, ADR 0004).</summary>
public sealed class WordStorageOptions
{
    public const string SectionName = "Documents:Storage";
    public const string PathVariable = "DOCS_STORAGE_PATH";

    /// <summary>Absolute, or relative to the app's content root.</summary>
    public string Path { get; set; } = "App_Data/documents";
}
