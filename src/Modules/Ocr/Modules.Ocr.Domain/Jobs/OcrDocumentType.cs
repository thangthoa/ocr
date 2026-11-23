namespace Modules.Ocr.Domain.Jobs;

/// <summary>
/// How a page is written. The OCR service recognizes it for each page and reads the page with the matching engine
/// (<c>doc_type=auto</c>); the user does not choose it.
/// </summary>
public enum OcrDocumentType
{
    Printed = 1,
    Handwritten = 2,
}
