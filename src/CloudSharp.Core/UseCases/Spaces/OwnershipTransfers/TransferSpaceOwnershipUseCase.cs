using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;

public sealed class TransferSpaceOwnershipUseCase : IUseCase<TransferSpaceOwnershipCommand, SpaceResultDto>
{
    public Task<Result<SpaceResultDto>> ExecuteAsync(
        TransferSpaceOwnershipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new TransferSpaceOwnershipCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceResultDto>());
        }

        throw new NotImplementedException();
    }
}
