using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts;

internal static class MountInputValidationRules
{
    public static IRuleBuilderOptions<TInput, CurrentActor> RequireSessionActor<TInput>(
        this IRuleBuilderInitial<TInput, CurrentActor> ruleBuilder) =>
        ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithErrorCode(ErrorCodes.Auth.TokenRequired)
            .WithMessage("A Session actor is required.")
            .Must(actor => actor.ActorType == ActorType.Session)
            .WithErrorCode(ErrorCodes.Auth.TokenTypeNotAllowed)
            .WithMessage("Only Session actors can access mounts.")
            .Must(actor => SessionSpaceAuthorizationPolicy.ValidateActor(actor).IsSuccess)
            .WithErrorCode(ErrorCodes.Auth.TokenInvalid)
            .WithMessage("The Session actor is invalid.");

    public static IRuleBuilderOptions<TInput, Guid> RequireSpacePublicId<TInput>(
        this IRuleBuilder<TInput, Guid> ruleBuilder) =>
        ruleBuilder
            .Must(IsPublicId)
            .WithErrorCode(ErrorCodes.Space.PublicIdInvalid)
            .WithMessage("Space public ID must be an RFC-variant UUIDv7.");

    public static IRuleBuilderOptions<TInput, Guid> RequireMountPublicId<TInput>(
        this IRuleBuilder<TInput, Guid> ruleBuilder) =>
        ruleBuilder
            .Must(IsPublicId)
            .WithErrorCode(ErrorCodes.Mount.PublicIdInvalid)
            .WithMessage("Mount public ID must be an RFC-variant UUIDv7.");

    public static IRuleBuilderOptions<TInput, Guid> RequireTargetPublicId<TInput>(
        this IRuleBuilder<TInput, Guid> ruleBuilder) =>
        ruleBuilder
            .Must(IsPublicId)
            .WithErrorCode(ErrorCodes.Mount.TargetIdInvalid)
            .WithMessage("Target public ID must be an RFC-variant UUIDv7.");

    public static IRuleBuilderOptions<TInput, long> RequireExpectedVersion<TInput>(
        this IRuleBuilder<TInput, long> ruleBuilder) =>
        ruleBuilder
            .GreaterThanOrEqualTo(1)
            .WithErrorCode(ErrorCodes.Mount.ExpectedVersionInvalid)
            .WithMessage("Expected version must be at least 1.");

    public static IRuleBuilderOptions<TInput, int> RequirePageLimit<TInput>(
        this IRuleBuilder<TInput, int> ruleBuilder) =>
        ruleBuilder
            .InclusiveBetween(1, 100)
            .WithErrorCode(ErrorCodes.Mount.ListLimitInvalid)
            .WithMessage("Limit must be between 1 and 100.");

    public static IRuleBuilderOptions<TInput, string?> ValidateCursor<TInput>(
        this IRuleBuilder<TInput, string?> ruleBuilder) =>
        ruleBuilder
            .Must(cursor => cursor is null || !string.IsNullOrWhiteSpace(cursor))
            .WithErrorCode(ErrorCodes.Common.CursorInvalid)
            .WithMessage("Cursor must not be empty or whitespace.");

    public static IRuleBuilderOptions<TInput, IReadOnlyList<MountPermission>> RequirePermissions<TInput>(
        this IRuleBuilder<TInput, IReadOnlyList<MountPermission>> ruleBuilder,
        bool allowEmpty) =>
        ruleBuilder
            .Must(permissions => permissions is not null
                && (allowEmpty || permissions.Count > 0)
                && permissions.All(Enum.IsDefined))
            .WithErrorCode(ErrorCodes.Mount.PermissionsInvalid)
            .WithMessage(allowEmpty
                ? "Permissions are required and must contain only defined Mount permission values."
                : "Initial mount grants require at least one defined permission.");

    public static bool IsPublicId(Guid id) => id.Version == 7 && id.Variant is >= 8 and <= 11;
}
