using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Mounts;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;

/// <summary>사용자의 직접 Mount 권한을 전체 교체한다. 빈 집합은 부여를 회수한다.</summary>
public sealed record ReplaceMountUserPermissionsCommand(
    CurrentActor Actor,
    Guid SpacePublicId,
    Guid MountPublicId,
    Guid UserPublicId,
    long ExpectedVersion,
    IReadOnlyList<MountPermission> Permissions);

internal sealed class ReplaceMountUserPermissionsCommandValidator : AbstractValidator<ReplaceMountUserPermissionsCommand>
{
    public ReplaceMountUserPermissionsCommandValidator()
    {
        RuleFor(command => command.Actor).RequireSessionActor();
        RuleFor(command => command.SpacePublicId).RequireSpacePublicId();
        RuleFor(command => command.MountPublicId).RequireMountPublicId();
        RuleFor(command => command.UserPublicId).RequireTargetPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();

        RuleFor(command => command.Permissions).RequirePermissions(allowEmpty: true);
    }
}
