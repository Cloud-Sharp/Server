using CloudSharp.Core.Common.Actors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Archives;

public sealed record ArchiveMountCommand(
    CurrentActor Actor,
    Guid SpacePublicId,
    Guid MountPublicId,
    long ExpectedVersion);

internal sealed class ArchiveMountCommandValidator : AbstractValidator<ArchiveMountCommand>
{
    public ArchiveMountCommandValidator()
    {
        RuleFor(command => command.Actor).RequireSessionActor();
        RuleFor(command => command.SpacePublicId).RequireSpacePublicId();
        RuleFor(command => command.MountPublicId).RequireMountPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
    }
}
