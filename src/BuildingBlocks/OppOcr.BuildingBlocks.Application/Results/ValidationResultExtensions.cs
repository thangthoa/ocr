using FluentValidation.Results;

namespace OppOcr.BuildingBlocks.Application.Results;

public static class ValidationResultExtensions
{
    public static Error ToError(this ValidationResult result)
    {
        var first = result.Errors[0];
        var message = string.Join(" ", result.Errors.Select(e => e.ErrorMessage).Distinct());
        return Error.Validation(first.ErrorCode, message);
    }
}
