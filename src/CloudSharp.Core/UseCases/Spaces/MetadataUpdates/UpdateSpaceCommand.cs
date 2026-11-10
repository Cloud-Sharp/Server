using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.MetadataUpdates;

/// <summary>Space 메타데이터를 부분 갱신한다. null인 이름 또는 설명 변경은 기존 값을 유지한다.</summary>
public sealed record UpdateSpaceCommand(
    AuthorizedSpaceContext Space,
    long ExpectedVersion,
    string? Name,
    SpaceDescriptionChange? Description);

internal sealed class UpdateSpaceCommandValidator : AbstractValidator<UpdateSpaceCommand>
{
    public UpdateSpaceCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();

        RuleFor(command => command.Name!)
            .SetValidator(new SpaceNameValidator())
            .When(command => command.Name is not null);
    }
}
