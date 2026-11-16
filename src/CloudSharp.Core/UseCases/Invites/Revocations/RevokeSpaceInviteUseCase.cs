using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Revocations;

public sealed class RevokeSpaceInviteUseCase : IUseCase<RevokeSpaceInviteCommand, RevokeSpaceInviteResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RevokeSpaceInviteUseCase() { }

    internal RevokeSpaceInviteUseCase(
        ISpaceRepository spaceRepository,
        ISpaceInviteRepository inviteRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        InviteRepository = inviteRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<RevokeSpaceInviteResultDto>> ExecuteAsync(
        RevokeSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RevokeSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<RevokeSpaceInviteResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<RevokeSpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<RevokeSpaceInviteResultDto>>(new NotImplementedException());
    }
}
