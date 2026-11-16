using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Archives;

public sealed class ArchiveSpaceUseCase : IUseCase<ArchiveSpaceCommand, SpaceResultDto>
{
    private SpaceMutationWorkflow MutationWorkflow { get; }

    public ArchiveSpaceUseCase(
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock,
        ISpaceDetailsReader? detailsReader = null,
        ISpaceRepository? spaceRepository = null)
    {
        MutationWorkflow = new SpaceMutationWorkflow(
            detailsReader,
            spaceRepository,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<SpaceResultDto>> ExecuteAsync(
        ArchiveSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ArchiveSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceResultDto>();
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Space,
            command.ExpectedVersion,
            SpaceStatus.Active,
            (space, now) => space.Archive(now),
            cancellationToken);
    }
}
