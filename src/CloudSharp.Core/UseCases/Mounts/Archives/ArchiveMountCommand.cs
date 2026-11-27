using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Archives;

/// <summary>MOUNT_MANAGE 인가를 받은 ACTIVE Space에서 ACTIVE Mount를 보관한다.</summary>
public sealed record ArchiveMountCommand(
    AuthorizedMountContext Mount,
    long ExpectedVersion);

internal sealed class ArchiveMountCommandValidator : AbstractValidator<ArchiveMountCommand>
{
    public ArchiveMountCommandValidator()
    {
        RuleFor(command => command.Mount).RequireAuthorizedMountContext();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
    }
}
