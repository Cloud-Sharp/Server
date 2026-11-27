using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;

/// <summary>
/// 사용자의 Mount 권한 덮어쓰기를 전체 교체한다. 허용·거부가 모두 비어 있으면 덮어쓰기를 제거한다.
/// 바뀌는 권한은 요청자가 해당 Mount에서 가진 권한이어야 한다.
/// </summary>
public sealed record ReplaceMountUserPermissionsCommand(
    AuthorizedMountContext Mount,
    Guid UserPublicId,
    long ExpectedVersion,
    IReadOnlyList<SpacePermission> Allow,
    IReadOnlyList<SpacePermission> Deny);

internal sealed class ReplaceMountUserPermissionsCommandValidator : AbstractValidator<ReplaceMountUserPermissionsCommand>
{
    public ReplaceMountUserPermissionsCommandValidator()
    {
        RuleFor(command => command.Mount).RequireAuthorizedMountContext();
        RuleFor(command => command.UserPublicId).RequireTargetPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
        RuleFor(command => command.Allow).RequireMountScopedPermissions();
        RuleFor(command => command.Deny).RequireMountScopedPermissions();
    }
}
