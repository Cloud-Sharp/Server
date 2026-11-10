using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces;

internal static class SpaceInputValidationRules
{
    public static IRuleBuilderOptions<TInput, AuthorizedSpaceContext> RequireAuthorizedSpaceContext<TInput>(
        this IRuleBuilder<TInput, AuthorizedSpaceContext> ruleBuilder) =>
        ruleBuilder
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.AuthorizationContextRequired)
            .WithMessage("Authorized space context is required.");

    public static IRuleBuilderOptions<TInput, long> RequirePositiveExpectedVersion<TInput>(
        this IRuleBuilder<TInput, long> ruleBuilder) =>
        ruleBuilder
            .GreaterThanOrEqualTo(1)
            .WithErrorCode(ErrorCodes.Space.ExpectedVersionInvalid)
            .WithMessage("Expected version must be at least 1.");
}
