using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.Creations;

/// <summary>INVITES_MANAGE 인가를 받은 ACTIVE Space에서 VIEWER 초대를 발급한다. MaxUses가 null이면 무제한이다.</summary>
public sealed record CreateSpaceInviteCommand(
    AuthorizedSpaceContext Space,
    DateTimeOffset ExpiresAt,
    int? MaxUses);

internal sealed class CreateSpaceInviteCommandValidator : AbstractValidator<CreateSpaceInviteCommand>
{
    public CreateSpaceInviteCommandValidator(IClock clock)
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();

        RuleFor(command => command.ExpiresAt)
            .Must(expiresAt => expiresAt.Offset == TimeSpan.Zero && expiresAt > clock.UtcNow)
            .WithErrorCode(ErrorCodes.SpaceInvite.ExpiresAtInvalid)
            .WithMessage("Invite expiry must be a future UTC timestamp.");

        RuleFor(command => command.MaxUses)
            .Must(maxUses => maxUses is null or > 0)
            .WithErrorCode(ErrorCodes.SpaceInvite.MaxUsesInvalid)
            .WithMessage("Invite max uses must be positive when provided.");
    }
}
