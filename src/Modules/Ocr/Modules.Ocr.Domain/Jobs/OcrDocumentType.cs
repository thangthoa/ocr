namespace Modules.Ocr.Domain.Jobs;

/// <summary>The service cannot detect this itself, so the user must choose (API doc §1).</summary>
public enum OcrDocumentType
{
    Printed = 1,
    Handwritten = 2,
}
