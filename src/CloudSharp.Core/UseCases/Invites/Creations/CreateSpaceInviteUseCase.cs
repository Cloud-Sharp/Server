using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Creations;

public sealed class CreateSpaceInviteUseCase : IUseCase<CreateSpaceInviteCommand, CreateSpaceInviteResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private ITokenIssuer TokenIssuer { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateSpaceInviteUseCase(IClock clock) => Clock = clock;

    internal CreateSpaceInviteUseCase(
        ISpaceRepository spaceRepository,
        ISpaceInviteRepository inviteRepository,
        ITokenIssuer tokenIssuer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        InviteRepository = inviteRepository;
        TokenIssuer = tokenIssuer;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<CreateSpaceInviteResultDto>> ExecuteAsync(
        CreateSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceInviteCommandValidator(Clock).Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<CreateSpaceInviteResultDto>());
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<CreateSpaceInviteResultDto>>(new NotImplementedException());
    }
}
