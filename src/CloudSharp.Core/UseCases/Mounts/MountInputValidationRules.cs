using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts;

internal static class MountInputValidationRules
{
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

    public static IRuleBuilderOptions<TInput, AuthorizedMountContext> RequireAuthorizedMountContext<TInput>(
        this IRuleBuilder<TInput, AuthorizedMountContext> ruleBuilder) =>
        ruleBuilder
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.AuthorizationContextRequired)
            .WithMessage("Authorized mount context is required.");

    /// <summary>덮어쓰기 입력의 허용·거부 목록은 Mount 범위 권한만 담아야 한다. 겹침과 빈 덮어쓰기는 도메인이 검사한다.</summary>
    public static IRuleBuilderOptions<TInput, IReadOnlyList<SpacePermission>> RequireMountScopedPermissions<TInput>(
        this IRuleBuilder<TInput, IReadOnlyList<SpacePermission>> ruleBuilder) =>
        ruleBuilder
            .Must(permissions => permissions is not null && permissions.All(MountScopedPermissions.Contains))
            .WithErrorCode(ErrorCodes.Mount.PermissionsInvalid)
            .WithMessage("Overwrite permissions are required and must contain only mount-scoped permissions.");

    public static bool IsPublicId(Guid id) => id.Version == 7 && id.Variant is >= 8 and <= 11;
}
