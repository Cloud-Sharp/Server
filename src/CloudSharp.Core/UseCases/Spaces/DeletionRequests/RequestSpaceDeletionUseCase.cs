using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.DeletionRequests;

public sealed class RequestSpaceDeletionUseCase : IUseCase<RequestSpaceDeletionCommand, SpaceDeletionAcceptedDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RequestSpaceDeletionUseCase() { }

    internal RequestSpaceDeletionUseCase(
        ISpaceRepository spaceRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<SpaceDeletionAcceptedDto>> ExecuteAsync(
        RequestSpaceDeletionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RequestSpaceDeletionCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceDeletionAcceptedDto>());
        }

        throw new NotImplementedException();
    }
}
