using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.Creations;

/// <summary>인증된 Session 사용자 소유의 Space를 생성한다. 허용 용량이 null이면 무제한이다.</summary>
public sealed record CreateSpaceCommand(
    CurrentActor Actor,
    string Name,
    string? Description,
    long? AllowedBytes);

internal sealed class CreateSpaceCommandValidator : AbstractValidator<CreateSpaceCommand>
{
    public CreateSpaceCommandValidator()
    {
        RuleFor(command => command.Actor)
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.ActorRequired)
            .WithMessage("Actor is required.");

        RuleFor(command => command.Name)
            .NotNull()
            .WithErrorCode(ErrorCodes.Space.NameInvalid)
            .WithMessage("Space name is required.")
            .SetValidator(new SpaceNameValidator());

        RuleFor(command => command.AllowedBytes!.Value)
            .SetValidator(new SpaceQuotaValidator())
            .When(command => command.AllowedBytes.HasValue)
            .OverridePropertyName(nameof(CreateSpaceCommand.AllowedBytes));
    }
}
