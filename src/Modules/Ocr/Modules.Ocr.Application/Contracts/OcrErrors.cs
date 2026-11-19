using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Contracts;

/// <summary>Codes returned by OCR use cases. The presentation layer localizes them.</summary>
public static class OcrErrorCodes
{
    public const string FileNameRequired = "ocr.file_name_required";
    public const string FileNameTooLong = "ocr.file_name_too_long";
    public const string UnsupportedFormat = "ocr.unsupported_format";
    public const string Empty = "ocr.empty";
    public const string TooLarge = "ocr.too_large";
    public const string ContentMismatch = "ocr.content_mismatch";
    public const string DocumentTypeRequired = "ocr.document_type_required";
    public const string NotFound = "ocr.not_found";
    public const string NotReady = "ocr.not_ready";
    public const string ServiceUnavailable = "ocr.service_unavailable";
}

/// <summary>
/// Codes coming from the OCR service (API doc §5) are passed through unchanged; these are the
/// ones the module adds itself.
/// </summary>
public static class OcrServiceErrorCodes
{
    public const string ServiceUnavailable = "service_unavailable";
    public const string RemoteJobLost = "remote_job_lost";

    /// <summary>The service does not offer <c>GET /v1/jobs</c> yet.</summary>
    public const string ListNotSupported = "list_not_supported";
}

public static class OcrErrors
{
    public static readonly Error NotFound = Error.NotFound(OcrErrorCodes.NotFound, "OCR job not found.");

    public static readonly Error NotReady = Error.Conflict(OcrErrorCodes.NotReady, "The OCR result is not ready yet.");

    public static readonly Error ContentMismatch = Error.Validation(
        OcrErrorCodes.ContentMismatch, "File content is not a supported document or does not match its extension.");

    public static readonly Error TooLarge = Error.Validation(OcrErrorCodes.TooLarge, $"File exceeds {OcrRules.MaxSizeBytes} bytes.");

    public static Error ServiceUnavailable(Error cause) =>
        Error.Failure(OcrErrorCodes.ServiceUnavailable, $"OCR service request failed: {cause.Code}.");

    /// <summary>Maps a service error for a request about one job.</summary>
    public static Error FromService(Error error) => error.Type switch
    {
        ErrorType.NotFound => NotFound,
        ErrorType.Failure => ServiceUnavailable(error),
        _ => error,
    };
}
