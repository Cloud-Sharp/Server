using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

/// <summary>
/// Space의 MOUNT_MANAGE 인가를 받은 사용자가 Mount를 개설한다. 초기 덮어쓰기의 허용·거부 권한은
/// 요청자가 Space에서 가진 Mount 범위 권한이어야 한다.
/// </summary>
public sealed record CreateMountCommand(
    AuthorizedSpaceContext Space,
    string Name,
    MountVisibility Visibility,
    IReadOnlyList<InitialMountRoleOverwrite>? RoleOverwrites = null,
    IReadOnlyList<InitialMountUserOverwrite>? UserOverwrites = null);

public sealed record InitialMountRoleOverwrite(
    Guid RolePublicId,
    IReadOnlyList<SpacePermission> Allow,
    IReadOnlyList<SpacePermission> Deny);

public sealed record InitialMountUserOverwrite(
    Guid UserPublicId,
    IReadOnlyList<SpacePermission> Allow,
    IReadOnlyList<SpacePermission> Deny);

internal sealed class CreateMountCommandValidator : AbstractValidator<CreateMountCommand>
{
    public CreateMountCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();

        RuleFor(command => command.Name)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.NameInvalid)
            .WithMessage("Mount name is required.")
            .SetValidator(new MountNameValidator());

        RuleFor(command => command.Visibility).SetValidator(new MountVisibilityValidator());

        RuleFor(command => command.RoleOverwrites)
            .Must(overwrites => HaveUniqueTargets(overwrites, overwrite => overwrite.RolePublicId))
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Each initial role overwrite must target a unique role.");

        RuleForEach(command => command.RoleOverwrites)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Initial role overwrite is required.")
            .ChildRules(overwrite =>
            {
                overwrite.RuleFor(value => value.RolePublicId).RequireTargetPublicId();
                overwrite.RuleFor(value => value.Allow).RequireMountScopedPermissions();
                overwrite.RuleFor(value => value.Deny).RequireMountScopedPermissions();
            });

        RuleFor(command => command.UserOverwrites)
            .Must(overwrites => HaveUniqueTargets(overwrites, overwrite => overwrite.UserPublicId))
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Each initial user overwrite must target a unique user.");

        RuleForEach(command => command.UserOverwrites)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Initial user overwrite is required.")
            .ChildRules(overwrite =>
            {
                overwrite.RuleFor(value => value.UserPublicId).RequireTargetPublicId();
                overwrite.RuleFor(value => value.Allow).RequireMountScopedPermissions();
                overwrite.RuleFor(value => value.Deny).RequireMountScopedPermissions();
            });
    }

    private static bool HaveUniqueTargets<TOverwrite>(
        IReadOnlyList<TOverwrite>? overwrites,
        Func<TOverwrite, Guid> getTargetId)
        where TOverwrite : class =>
        overwrites is null || overwrites.OfType<TOverwrite>().GroupBy(getTargetId).All(group => group.Count() == 1);
}
