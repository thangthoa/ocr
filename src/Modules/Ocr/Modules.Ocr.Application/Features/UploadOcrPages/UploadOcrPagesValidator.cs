using FluentValidation;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.Features.UploadOcrPages;

public sealed class UploadOcrPagesValidator : AbstractValidator<UploadOcrPagesCommand>
{
    public UploadOcrPagesValidator()
    {
        RuleFor(x => x.DocumentType)
            .NotNull().WithErrorCode(OcrErrorCodes.DocumentTypeRequired).WithMessage("Document type is required.")
            .IsInEnum().WithErrorCode(OcrErrorCodes.DocumentTypeRequired).WithMessage("Document type is invalid.");

        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(OcrErrorCodes.FileNameRequired).WithMessage("File name is required.")
            .Must(OcrRules.IsValidFileName).WithErrorCode(OcrErrorCodes.FileNameInvalid)
            .WithMessage("File name must not contain / \\ : * ? \" < > | or control characters.")
            .Must(name => UploadOcrPagesHandler.DocumentName(name).Length <= OcrRules.MaxFileNameLength)
            .WithErrorCode(OcrErrorCodes.FileNameTooLong)
            .WithMessage($"File name must not exceed {OcrRules.MaxFileNameLength} characters.");

        RuleFor(x => x.Pages)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(OcrErrorCodes.PagesRequired).WithMessage("At least one page is required.")
            .Must(pages => pages.Count <= OcrRules.MaxPagesPerDocument).WithErrorCode(OcrErrorCodes.TooManyPages)
            .WithMessage($"A document can have at most {OcrRules.MaxPagesPerDocument} pages.")
            .Must(pages => pages.Sum(p => p.Size) <= OcrRules.MaxSizeBytes).WithErrorCode(OcrErrorCodes.TooLarge)
            .WithMessage($"The pages together exceed {OcrRules.MaxSizeBytes} bytes.");

        RuleForEach(x => x.Pages).ChildRules(page =>
        {
            page.RuleFor(p => p.FileName)
                .Must(name => OcrFileFormats.FromFileName(name) is { } format && OcrFileFormats.PageFormats.Contains(format))
                .WithErrorCode(OcrErrorCodes.UnsupportedPageFormat)
                .WithMessage(_ => $"Pages must be images: {string.Join(", ", OcrFileFormats.PageExtensions)}.");
            page.RuleFor(p => p.Size)
                .GreaterThan(0).WithErrorCode(OcrErrorCodes.Empty).WithMessage("File is empty.");
        });
    }
}
