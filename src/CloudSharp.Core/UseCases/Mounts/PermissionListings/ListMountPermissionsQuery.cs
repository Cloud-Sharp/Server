using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.PermissionListings;

/// <summary>지정한 대상 유형의 명시적 Mount 권한 부여를 페이지로 조회한다.</summary>
public sealed record ListMountPermissionsQuery(
    CurrentActor Actor,
    Guid SpacePublicId,
    Guid MountPublicId,
    MountGrantTargetType TargetType,
    string? Cursor = null,
    int Limit = 50);

internal sealed class ListMountPermissionsQueryValidator : AbstractValidator<ListMountPermissionsQuery>
{
    public ListMountPermissionsQueryValidator()
    {
        RuleFor(query => query.Actor).RequireSessionActor();
        RuleFor(query => query.SpacePublicId).RequireSpacePublicId();
        RuleFor(query => query.MountPublicId).RequireMountPublicId();
        RuleFor(query => query.Cursor).ValidateCursor();
        RuleFor(query => query.Limit).RequirePageLimit();

        RuleFor(query => query.TargetType)
            .IsInEnum()
            .WithErrorCode(ErrorCodes.Mount.TargetTypeInvalid)
            .WithMessage("Mount grant target type must be a defined value.");
    }
}
