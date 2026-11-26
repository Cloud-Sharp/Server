using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

public sealed record CreateMountCommand(
    CurrentActor Actor,
    Guid SpacePublicId,
    string Name,
    MountVisibility Visibility,
    IReadOnlyList<InitialMountRoleGrant>? RoleGrants = null,
    IReadOnlyList<InitialMountUserGrant>? UserGrants = null);

public sealed record InitialMountRoleGrant(Guid RolePublicId, IReadOnlyList<MountPermission> Permissions);

public sealed record InitialMountUserGrant(Guid UserPublicId, IReadOnlyList<MountPermission> Permissions);

internal sealed class CreateMountCommandValidator : AbstractValidator<CreateMountCommand>
{
    public CreateMountCommandValidator()
    {
        RuleFor(command => command.Actor).RequireSessionActor();
        RuleFor(command => command.SpacePublicId).RequireSpacePublicId();

        RuleFor(command => command.Name)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.NameInvalid)
            .WithMessage("Mount name is required.")
            .SetValidator(new MountNameValidator());

        RuleFor(command => command.Visibility).SetValidator(new MountVisibilityValidator());

        RuleFor(command => command.RoleGrants)
            .Must(grants => HaveUniqueTargets(grants, grant => grant.RolePublicId))
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Each initial role grant must target a unique role.");

        RuleForEach(command => command.RoleGrants)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Initial role grant is required.")
            .ChildRules(grant =>
            {
                grant.RuleFor(value => value.RolePublicId).RequireTargetPublicId();
                grant.RuleFor(value => value.Permissions).RequirePermissions(allowEmpty: false);
            });

        RuleFor(command => command.UserGrants)
            .Must(grants => HaveUniqueTargets(grants, grant => grant.UserPublicId))
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Each initial user grant must target a unique user.");

        RuleForEach(command => command.UserGrants)
            .NotNull()
            .WithErrorCode(ErrorCodes.Mount.GrantsInvalid)
            .WithMessage("Initial user grant is required.")
            .ChildRules(grant =>
            {
                grant.RuleFor(value => value.UserPublicId).RequireTargetPublicId();
                grant.RuleFor(value => value.Permissions).RequirePermissions(allowEmpty: false);
            });
    }

    private static bool HaveUniqueTargets<TGrant>(IReadOnlyList<TGrant>? grants, Func<TGrant, Guid> getTargetId)
        where TGrant : class =>
        grants is null || grants.OfType<TGrant>().GroupBy(getTargetId).All(group => group.Count() == 1);
}
