using FluentValidation;
using Modules.Ocr.Application.Common;
using Modules.Ocr.Application.Contracts;
using Modules.Ocr.Domain.Jobs;

namespace Modules.Ocr.Application.Features.InsertOcrPages;

public sealed class InsertOcrPagesValidator : AbstractValidator<InsertOcrPagesCommand>
{
    public InsertOcrPagesValidator()
    {
        RuleFor(x => x.Files)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithErrorCode(OcrErrorCodes.PagesRequired).WithMessage("Choose at least one file.")
            .Must(files => files.Count <= OcrRules.MaxPagesPerDocument).WithErrorCode(OcrErrorCodes.TooManyPages)
            .WithMessage($"A document can have at most {OcrRules.MaxPagesPerDocument} pages.")
            // The same allowance as one upload: the files are held in memory while they are sent.
            .Must(files => files.Sum(f => f.Size) <= OcrRules.MaxSizeBytes).WithErrorCode(OcrErrorCodes.TooLarge)
            .WithMessage($"The files together exceed {OcrRules.MaxSizeBytes} bytes.");

        RuleForEach(x => x.Files).SetValidator(new UploadedFileValidator());

        RuleFor(x => x.Position)
            .GreaterThanOrEqualTo(1).When(x => x.Position is not null)
            .WithErrorCode(OcrErrorCodes.PageNotFound).WithMessage("Pages are numbered from 1.");
    }
}
