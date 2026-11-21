using Modules.Ocr.Domain.Jobs;
using OppOcr.BuildingBlocks.Application.Results;

namespace Modules.Ocr.Application.Contracts;

/// <summary>Codes returned by OCR use cases. The presentation layer localizes them.</summary>
public static class OcrErrorCodes
{
    public const string FileNameRequired = "ocr.file_name_required";
    public const string FileNameTooLong = "ocr.file_name_too_long";
    public const string FileNameInvalid = "ocr.file_name_invalid";
    public const string UnsupportedFormat = "ocr.unsupported_format";
    public const string Empty = "ocr.empty";
    public const string TooLarge = "ocr.too_large";
    public const string ContentMismatch = "ocr.content_mismatch";
    public const string DocumentTypeRequired = "ocr.document_type_required";
    public const string NotFound = "ocr.not_found";
    public const string NotReady = "ocr.not_ready";
    public const string ServiceUnavailable = "ocr.service_unavailable";
    public const string PagesRequired = "ocr.pages_required";
    public const string TooManyPages = "ocr.too_many_pages";
    public const string UnsupportedPageFormat = "ocr.unsupported_page_format";
    public const string ImageNotSupported = "ocr.image_not_supported";
    public const string JobBusy = "ocr.job_busy";
    public const string LastPage = "ocr.last_page";
    public const string PageNotFound = "ocr.page_not_found";
    public const string PagesNotEditable = "ocr.pages_not_editable";
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

    /// <summary>Sent by the service: it could not download the file from the link it was given (ADR 0006).</summary>
    public const string FileDownloadFailed = "file_download_failed";

    /// <summary>A callback without a valid signature, too old, or received while callbacks are not configured.</summary>
    public const string CallbackRejected = "callback_rejected";

    /// <summary>A correctly signed callback whose body is not a job.</summary>
    public const string CallbackInvalid = "callback_invalid";

    /// <summary>Sent by the service: pages cannot change while it reads them or builds the Word file (API doc §3.8).</summary>
    public const string JobBusy = "job_busy";

    /// <summary>Sent by the service: the only page left cannot be deleted.</summary>
    public const string LastPage = "last_page";
}

public static class OcrErrors
{
    public static readonly Error NotFound = Error.NotFound(OcrErrorCodes.NotFound, "OCR job not found.");

    public static readonly Error NotReady = Error.Conflict(OcrErrorCodes.NotReady, "The OCR result is not ready yet.");

    public static readonly Error ContentMismatch = Error.Validation(
        OcrErrorCodes.ContentMismatch, "File content is not a supported document or does not match its extension.");

    public static readonly Error TooLarge = Error.Validation(OcrErrorCodes.TooLarge, $"File exceeds {OcrRules.MaxSizeBytes} bytes.");

    public static readonly Error ImageNotSupported = Error.Validation(
        OcrErrorCodes.ImageNotSupported, "An image cannot be placed on a PDF page (for example an interlaced PNG).");

    public static readonly Error JobBusy = Error.Conflict(
        OcrErrorCodes.JobBusy, "Pages cannot change while the OCR service reads them or builds the Word file; try again when it is done.");

    public static readonly Error LastPage = Error.Conflict(OcrErrorCodes.LastPage, "The only page of a document cannot be deleted.");

    public static readonly Error PageNotFound = Error.NotFound(OcrErrorCodes.PageNotFound, "The document has no such page.");

    public static readonly Error PagesNotEditable = Error.Conflict(OcrErrorCodes.PagesNotEditable, "The pages of a cancelled job cannot change.");

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
