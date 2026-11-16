using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Acceptances;

public sealed class AcceptSpaceInviteUseCase : IUseCase<AcceptSpaceInviteCommand, AcceptSpaceInviteResultDto>
{
    private ITokenHasher TokenHasher { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;
    private IUserRepository UserRepository { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public AcceptSpaceInviteUseCase() { }

    internal AcceptSpaceInviteUseCase(
        ITokenHasher tokenHasher,
        ISpaceInviteRepository inviteRepository,
        ISpaceRepository spaceRepository,
        ISpaceRoleReader roleReader,
        IUserRepository userRepository,
        ISpaceMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        TokenHasher = tokenHasher;
        InviteRepository = inviteRepository;
        SpaceRepository = spaceRepository;
        RoleReader = roleReader;
        UserRepository = userRepository;
        MembershipRepository = membershipRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public Task<Result<AcceptSpaceInviteResultDto>> ExecuteAsync(
        AcceptSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new AcceptSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<AcceptSpaceInviteResultDto>());
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Task.FromResult(Result.Fail<AcceptSpaceInviteResultDto>(actorValidation.Errors));
        }

        return Task.FromException<Result<AcceptSpaceInviteResultDto>>(new NotImplementedException());
    }
}
