using CloudSharp.Core.Common.Actors;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Reactivations;

public sealed record ReactivateMountCommand(
    CurrentActor Actor,
    Guid SpacePublicId,
    Guid MountPublicId,
    long ExpectedVersion);

internal sealed class ReactivateMountCommandValidator : AbstractValidator<ReactivateMountCommand>
{
    public ReactivateMountCommandValidator()
    {
        RuleFor(command => command.Actor).RequireSessionActor();
        RuleFor(command => command.SpacePublicId).RequireSpacePublicId();
        RuleFor(command => command.MountPublicId).RequireMountPublicId();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
    }
}
