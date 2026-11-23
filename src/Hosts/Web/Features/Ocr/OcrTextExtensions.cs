using Microsoft.Extensions.Localization;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace OppOcr.Web.Features.Ocr;

internal static class OcrTextExtensions
{
    // Fixed English strings from the service (API doc §4.1), matched by prefix because some
    // contain variable detail. Each maps to a translated resource key.
    private static readonly (string Prefix, string Key)[] KnownWarnings =
    [
        ("the full-page read repeated itself", "Warning.Repeated"),
        ("every full-page read repeated itself", "Warning.AllRepeated"),
        ("the layout model failed at the usual image size", "Warning.LayoutSmaller"),
        ("no text block was found by the layout model", "Warning.NoBlocks"),
        ("no block could be read either", "Warning.NothingReadable"),
        ("line detection (Kraken) failed", "Warning.KrakenFailed"),
        ("page layout ran out of GPU memory", "Warning.LayoutOom"),
        ("one of the four readings (Chandra) could not run", "Warning.ChandraFailed"),
        ("a GPU worker failed while reading this page", "Warning.GpuRetry"),
    ];

    public static string SupportedFormatsLabel => FormatsLabel(OcrFileFormats.SupportedExtensions);

    /// <summary>The image types that can be combined into one multi-page document.</summary>
    public static string PageFormatsLabel => FormatsLabel(OcrFileFormats.PageExtensions);

    /// <summary>
    /// Translates a use-case error. Errors the service returned (e.g. <c>too_many_pages</c>)
    /// keep the service's code and are translated with the job error texts.
    /// </summary>
    public static string Localize(this IStringLocalizer<OcrText> text, Error error)
    {
        var localized = error.Code switch
        {
            OcrErrorCodes.TooLarge => text[error.Code, OcrRules.MaxSizeBytes / 1024 / 1024],
            OcrErrorCodes.FileNameTooLong => text[error.Code, OcrRules.MaxFileNameLength],
            OcrErrorCodes.UnsupportedFormat => text[error.Code, SupportedFormatsLabel],
            OcrErrorCodes.UnsupportedPageFormat => text[error.Code, PageFormatsLabel],
            OcrErrorCodes.TooManyPages => text[error.Code, OcrRules.MaxPagesPerDocument],
            _ => text[error.Code],
        };
        if (!localized.ResourceNotFound)
            return localized.Value;

        var serviceError = text[$"JobError.{error.Code}"];
        return serviceError.ResourceNotFound ? text["UnexpectedError"] : serviceError.Value;
    }

    /// <summary>Translates a failed job's code (the service's or this module's own).</summary>
    public static string JobError(this IStringLocalizer<OcrText> text, string? code)
    {
        var localized = text[$"JobError.{code}"];
        return localized.ResourceNotFound ? text["JobError.unknown", code ?? "-"] : localized.Value;
    }

    public static string Warning(this IStringLocalizer<OcrText> text, string warning)
    {
        foreach (var (prefix, key) in KnownWarnings)
        {
            if (warning.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return text[key];
        }

        return warning;
    }

    public static string Status(this IStringLocalizer<OcrText> text, OcrJobStatus status) => text[$"Status.{status}"];

    public static string DocumentType(this IStringLocalizer<OcrText> text, OcrDocumentType type) => text[$"DocumentType.{type}"];

    /// <summary>The kinds of a job's pages, for example "Printed / typed + Handwritten"; empty while none is known.</summary>
    public static string DocumentTypes(this IStringLocalizer<OcrText> text, IReadOnlyList<OcrDocumentType> types) =>
        string.Join(" + ", types.Select(text.DocumentType));

    private static string FormatsLabel(IEnumerable<string> extensions) =>
        string.Join(", ", extensions.Select(e => e.TrimStart('.').ToUpperInvariant()));
}
