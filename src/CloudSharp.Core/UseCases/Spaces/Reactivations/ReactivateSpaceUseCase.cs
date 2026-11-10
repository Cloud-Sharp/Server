using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Reactivations;

public sealed class ReactivateSpaceUseCase : IUseCase<ReactivateSpaceCommand, SpaceResultDto>
{
    private SpaceMutationWorkflow MutationWorkflow { get; }

    internal ReactivateSpaceUseCase(
        ISpaceDetailsReader detailsReader,
        ISpaceRepository spaceRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        MutationWorkflow = new SpaceMutationWorkflow(
            detailsReader,
            spaceRepository,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<SpaceResultDto>> ExecuteAsync(
        ReactivateSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReactivateSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Space,
            command.ExpectedVersion,
            SpaceStatus.Archived,
            (space, now) => space.Reactivate(now),
            cancellationToken);
    }
}
