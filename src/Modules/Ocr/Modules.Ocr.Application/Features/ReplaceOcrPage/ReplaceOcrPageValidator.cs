using FluentValidation;
using Modules.Ocr.Application.Common;
using Modules.Ocr.Application.Contracts;

namespace Modules.Ocr.Application.Features.ReplaceOcrPage;

public sealed class ReplaceOcrPageValidator : AbstractValidator<ReplaceOcrPageCommand>
{
    public ReplaceOcrPageValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1).WithErrorCode(OcrErrorCodes.PageNotFound).WithMessage("Pages are numbered from 1.");

        RuleFor(x => x.File)
            .NotNull().WithErrorCode(OcrErrorCodes.PagesRequired).WithMessage("Choose the new image of the page.")
            .SetValidator(new UploadedFileValidator());
    }
}
