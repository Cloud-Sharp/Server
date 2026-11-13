using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Removals;

public sealed class RemoveSpaceMemberUseCase : IUseCase<RemoveSpaceMemberCommand, RemoveSpaceMemberResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RemoveSpaceMemberUseCase() { }

    internal RemoveSpaceMemberUseCase(
        ISpaceRepository spaceRepository,
        ISpaceMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        MembershipRepository = membershipRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<RemoveSpaceMemberResultDto>> ExecuteAsync(
        RemoveSpaceMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RemoveSpaceMemberCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<RemoveSpaceMemberResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<RemoveSpaceMemberResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<RemoveSpaceMemberResultDto>>(new NotImplementedException());
    }
}
