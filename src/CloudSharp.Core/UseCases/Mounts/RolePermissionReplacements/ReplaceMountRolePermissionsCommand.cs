using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Mounts;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;

/// <summary>Role의 명시적 Mount 권한을 전체 교체한다. 빈 집합은 부여를 회수한다.</summary>
public sealed record ReplaceMountRolePermissionsCommand(
    CurrentActor Actor,
    Guid SpacePublicId,
    Guid MountPublicId,
    Guid RolePublicId,
    long ExpectedVersion,
    IReadOnlyList<MountPermission> Permissions);

internal sealed class ReplaceMountRolePermissionsCommandValidator : AbstractValidator<ReplaceMountRolePermissionsCommand>
{
    public ReplaceMountRolePermissionsCommandValidator()
    {
        RuleFor(command => command.Actor).RequireSessionActor();
        RuleFor(command => command.SpacePublicId).RequireSpacePublicId();
        RuleFor(command => command.MountPublicId).RequireMountPublicId();
        RuleFor(command => command.RolePublicId).RequireTargetPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();

        RuleFor(command => command.Permissions).RequirePermissions(allowEmpty: true);
    }
}
