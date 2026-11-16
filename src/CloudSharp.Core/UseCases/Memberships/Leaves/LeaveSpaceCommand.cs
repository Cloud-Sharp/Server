using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Memberships.Leaves;

/// <summary>
/// ACTIVE·ARCHIVED Space에서 Session 사용자가 탈퇴한다.
/// 본인의 ACTIVE·SUSPENDED Membership과 기존 LEFT 기록을 관계 조건으로 사용한다.
/// </summary>
public sealed record LeaveSpaceCommand(CurrentActor Actor, Guid SpacePublicId);

internal sealed class LeaveSpaceCommandValidator : AbstractValidator<LeaveSpaceCommand>
{
    public LeaveSpaceCommandValidator()
    {
        RuleFor(command => command.Actor)
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.ActorRequired)
            .WithMessage("Actor is required.");

        RuleFor(command => command.SpacePublicId)
            .Must(MembershipInputValidationRules.IsPublicId)
            .WithErrorCode(ErrorCodes.Space.PublicIdInvalid)
            .WithMessage("Space public ID must be an RFC-variant UUIDv7.");
    }
}
