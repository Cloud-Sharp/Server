using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.PermissionPreviews;

/// <summary>MOUNT_MANAGE 인가를 받은 Mount에서 지정한 사용자의 권한 또는 제안한 공개 범위의 영향을 조회한다.</summary>
public sealed record PreviewMountPermissionsQuery(
    AuthorizedMountContext Mount,
    Guid? UserPublicId = null,
    MountVisibility? Visibility = null,
    string? Cursor = null,
    int Limit = 50);

internal sealed class PreviewMountPermissionsQueryValidator : AbstractValidator<PreviewMountPermissionsQuery>
{
    public PreviewMountPermissionsQueryValidator()
    {
        RuleFor(query => query.Mount).RequireAuthorizedMountContext();
        RuleFor(query => query.Cursor).ValidateCursor();
        RuleFor(query => query.Limit).RequirePageLimit();

        RuleFor(query => query.UserPublicId)
            .Must((query, userPublicId) => userPublicId.HasValue || query.Visibility.HasValue)
            .WithErrorCode(ErrorCodes.Mount.PreviewInputRequired)
            .WithMessage("A user public ID or proposed visibility is required.");

        RuleFor(query => query.UserPublicId!.Value)
            .RequireTargetPublicId()
            .When(query => query.UserPublicId.HasValue)
            .OverridePropertyName(nameof(PreviewMountPermissionsQuery.UserPublicId));

        RuleFor(query => query.Visibility!.Value)
            .SetValidator(new MountVisibilityValidator())
            .When(query => query.Visibility.HasValue)
            .OverridePropertyName(nameof(PreviewMountPermissionsQuery.Visibility));
    }
}
