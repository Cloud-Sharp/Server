using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;

/// <summary>
/// Role의 Mount 권한 덮어쓰기를 전체 교체한다. 허용·거부가 모두 비어 있으면 덮어쓰기를 제거한다.
/// 바뀌는 권한은 요청자가 해당 Mount에서 가진 권한이어야 한다.
/// </summary>
public sealed record ReplaceMountRolePermissionsCommand(
    AuthorizedMountContext Mount,
    Guid RolePublicId,
    long ExpectedVersion,
    IReadOnlyList<SpacePermission> Allow,
    IReadOnlyList<SpacePermission> Deny);

internal sealed class ReplaceMountRolePermissionsCommandValidator : AbstractValidator<ReplaceMountRolePermissionsCommand>
{
    public ReplaceMountRolePermissionsCommandValidator()
    {
        RuleFor(command => command.Mount).RequireAuthorizedMountContext();
        RuleFor(command => command.RolePublicId).RequireTargetPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
        RuleFor(command => command.Allow).RequireMountScopedPermissions();
        RuleFor(command => command.Deny).RequireMountScopedPermissions();
    }
}
