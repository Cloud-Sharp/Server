using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Policies.Authorization;
using FluentValidation;

namespace CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;

public sealed record TransferSpaceOwnershipCommand(
    AuthorizedSpaceContext Space,
    long ExpectedVersion,
    Guid NewOwnerUserPublicId);

internal sealed class TransferSpaceOwnershipCommandValidator : AbstractValidator<TransferSpaceOwnershipCommand>
{
    public TransferSpaceOwnershipCommandValidator()
    {
        RuleFor(command => command.Space).RequireAuthorizedSpaceContext();
        RuleFor(command => command.ExpectedVersion).RequirePositiveExpectedVersion();

        RuleFor(command => command.NewOwnerUserPublicId)
            .Must(id => id.Version == 7 && id.Variant is >= 8 and <= 11)
            .WithErrorCode(ErrorCodes.Space.TransferTargetInvalid)
            .WithMessage("New owner user public ID must be an RFC-variant UUIDv7.");
    }
}
