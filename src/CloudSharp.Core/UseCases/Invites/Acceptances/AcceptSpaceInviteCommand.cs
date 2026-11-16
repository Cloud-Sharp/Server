using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Invites.Acceptances;

/// <summary>인증된 Session 사용자가 초대 토큰으로 Space 가입을 요청한다.</summary>
public sealed record AcceptSpaceInviteCommand(CurrentActor Actor, string InviteToken)
{
    public override string ToString() => "AcceptSpaceInviteCommand { InviteToken = [redacted] }";
}

internal sealed class AcceptSpaceInviteCommandValidator : AbstractValidator<AcceptSpaceInviteCommand>
{
    public AcceptSpaceInviteCommandValidator()
    {
        RuleFor(command => command.Actor)
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.ActorRequired)
            .WithMessage("Actor is required.");

        RuleFor(command => command.InviteToken).RequireInviteToken();
    }
}
