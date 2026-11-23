using FluentValidation;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.Features.UploadOcrFile;

public sealed class UploadOcrFileValidator : AbstractValidator<UploadOcrFileCommand>
{
    public UploadOcrFileValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty().WithErrorCode(OcrErrorCodes.FileNameRequired).WithMessage("File name is required.")
            .MaximumLength(OcrRules.MaxFileNameLength).WithErrorCode(OcrErrorCodes.FileNameTooLong)
            .WithMessage($"File name must not exceed {OcrRules.MaxFileNameLength} characters.")
            .Must(OcrRules.IsValidFileName).WithErrorCode(OcrErrorCodes.FileNameInvalid)
            .WithMessage("File name must not contain / \\ : * ? \" < > | or control characters.")
            .Must(name => OcrFileFormats.FromFileName(name) is not null).WithErrorCode(OcrErrorCodes.UnsupportedFormat)
            .WithMessage(_ => $"Supported extensions: {string.Join(", ", OcrFileFormats.SupportedExtensions)}.");

        RuleFor(x => x.Size)
            .GreaterThan(0).WithErrorCode(OcrErrorCodes.Empty).WithMessage("File is empty.")
            .LessThanOrEqualTo(OcrRules.MaxSizeBytes).WithErrorCode(OcrErrorCodes.TooLarge)
            .WithMessage($"File exceeds {OcrRules.MaxSizeBytes} bytes.");
    }
}
