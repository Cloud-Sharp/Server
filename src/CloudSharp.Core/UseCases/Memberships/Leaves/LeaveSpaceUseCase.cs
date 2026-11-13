using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Leaves;

public sealed class LeaveSpaceUseCase : IUseCase<LeaveSpaceCommand, LeaveSpaceResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public LeaveSpaceUseCase() { }

    internal LeaveSpaceUseCase(
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

    public Task<Result<LeaveSpaceResultDto>> ExecuteAsync(
        LeaveSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new LeaveSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<LeaveSpaceResultDto>());
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Task.FromResult(Result.Fail<LeaveSpaceResultDto>(actorValidation.Errors));
        }

        return Task.FromException<Result<LeaveSpaceResultDto>>(new NotImplementedException());
    }
}
