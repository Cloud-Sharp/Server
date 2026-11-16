using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships;

internal static class MembershipInputValidationRules
{
    public static IRuleBuilderOptions<TInput, Guid> RequireMembershipPublicId<TInput>(
        this IRuleBuilder<TInput, Guid> ruleBuilder) =>
        ruleBuilder
            .Must(IsPublicId)
            .WithErrorCode(ErrorCodes.SpaceMembership.PublicIdInvalid)
            .WithMessage("Membership public ID must be an RFC-variant UUIDv7.");

    public static bool IsPublicId(Guid id) =>
        id.Version == 7 && id.Variant is >= 8 and <= 11;
}
