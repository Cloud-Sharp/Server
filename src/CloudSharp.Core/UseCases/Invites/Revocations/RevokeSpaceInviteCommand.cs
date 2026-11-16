using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.Revocations;

/// <summary>INVITES_MANAGE 인가를 받은 ACTIVE Space에서 초대를 폐기한다.</summary>
public sealed record RevokeSpaceInviteCommand(
    AuthorizedSpaceContext Space,
    Guid InvitePublicId,
    long ExpectedVersion);

internal sealed class RevokeSpaceInviteCommandValidator : AbstractValidator<RevokeSpaceInviteCommand>
{
    public RevokeSpaceInviteCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();

        RuleFor(command => command.InvitePublicId)
            .Must(id => id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithErrorCode(ErrorCodes.SpaceInvite.PublicIdInvalid)
            .WithMessage("Invite public ID must be an RFC-variant UUIDv7.");

        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();
    }
}
