using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

/// <summary>MOUNT_MANAGE 인가를 받은 Mount에서 지정한 대상 유형의 권한 덮어쓰기를 페이지로 조회한다.</summary>
public sealed record ListMountPermissionsQuery(
    AuthorizedMountContext Mount,
    MountOverwriteTargetType TargetType,
    string? Cursor = null,
    int Limit = 50);

internal sealed class ListMountPermissionsQueryValidator : AbstractValidator<ListMountPermissionsQuery>
{
    public ListMountPermissionsQueryValidator()
    {
        RuleFor(query => query.Mount).RequireAuthorizedMountContext();
        RuleFor(query => query.Cursor).ValidateCursor();
        RuleFor(query => query.Limit).RequirePageLimit();

        RuleFor(query => query.TargetType)
            .IsInEnum()
            .WithErrorCode(ErrorCodes.Mount.TargetTypeInvalid)
            .WithMessage("Mount overwrite target type must be a defined value.");
    }
}
