using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Mounts.Reactivations;

/// <summary>MOUNT_MANAGE 인가를 받은 ACTIVE Space에서 보관된 Mount를 재활성화한다.</summary>
public sealed record ReactivateMountCommand(
    AuthorizedMountContext Mount,
    long ExpectedVersion);

internal sealed class ReactivateMountCommandValidator : AbstractValidator<ReactivateMountCommand>
{
    public ReactivateMountCommandValidator()
    {
        RuleFor(command => command.Mount).RequireAuthorizedMountContext();
        RuleFor(command => command.ExpectedVersion).RequireExpectedVersion();
    }
}
