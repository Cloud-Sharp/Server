using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
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

    public async Task<Result<AcceptSpaceInviteResultDto>> ExecuteAsync(
        AcceptSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new AcceptSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<AcceptSpaceInviteResultDto>();
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(actorValidation.Errors);
        }

        if (TokenHasher is null || InviteRepository is null || SpaceRepository is null
            || RoleReader is null || UserRepository is null || MembershipRepository is null
            || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => AcceptInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<AcceptSpaceInviteResultDto>> AcceptInTransactionAsync(
        AcceptSpaceInviteCommand command,
        CancellationToken cancellationToken)
    {
        var hashResult = TokenHash.Create(TokenHasher.Hash(command.InviteToken));
        if (hashResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(hashResult.Errors);
        }

        var inviteResult = await InviteRepository.FindByTokenHashAsync(hashResult.Value, cancellationToken);
        if (inviteResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(inviteResult.Errors);
        }

        var invite = inviteResult.Value;
        if (invite is null)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(SpaceInviteError.NotFound());
        }

        var userResult = await UserRepository.FindByIdAsync(command.Actor.UserId, cancellationToken);
        if (userResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(userResult.Errors);
        }

        var user = userResult.Value;
        if (user is null || !user.CanLogin())
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(SessionError.AuthUserInactive());
        }

        var spaceResult = await SpaceRepository.FindByIdAsync(invite.SpaceId, cancellationToken);
        if (spaceResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(spaceResult.Errors);
        }

        var space = spaceResult.Value;
        if (space is null)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != invite.SpaceId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var now = Clock.UtcNow;
        var availability = invite.CanAccept(now);
        if (availability.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(availability.Errors);
        }

        if (space.OwnerUserId == user.Id)
        {
            return Result.Ok(new AcceptSpaceInviteResultDto(space.PublicId, null, true));
        }

        var membershipResult = await MembershipRepository.FindLatestByUserIdAsync(
            space.Id,
            user.Id,
            cancellationToken);
        if (membershipResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(membershipResult.Errors);
        }

        var existing = membershipResult.Value;
        if (existing is not null && (existing.SpaceId != space.Id || existing.UserId != user.Id))
        {
            throw new InvalidOperationException("Membership repository returned a different membership.");
        }

        if (existing?.Status == SpaceMembershipStatus.Active)
        {
            return Result.Ok(new AcceptSpaceInviteResultDto(space.PublicId, existing.PublicId, true));
        }

        if (existing?.Status == SpaceMembershipStatus.Suspended)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(
                SpaceMembershipError.InvalidState("A suspended membership cannot accept an invite."));
        }

        var rolesResult = await RoleReader.ReadAsync(space.Id, cancellationToken);
        if (rolesResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(rolesResult.Errors);
        }

        var viewers = rolesResult.Value
            .Where(role => role.Id > 0
                && role.SpaceId == space.Id
                && role.IsManaged
                && role.SystemKey == SpaceRoleSystemKey.Viewer
                && role.DeletedAt is null)
            .ToArray();
        if (viewers.Length != 1)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(
                SpaceMembershipError.RoleInvalid("Exactly one persisted managed VIEWER role is required."));
        }

        var membershipCreation = SpaceMembership.Create(
            space.Id,
            user.Id,
            [viewers[0]],
            invite.CreatedByUserId,
            now);
        if (membershipCreation.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(membershipCreation.Errors);
        }

        var membership = membershipCreation.Value;
        var observedVersion = invite.Version;
        var acceptance = invite.RecordAcceptance(now);
        if (acceptance.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(acceptance.Errors);
        }

        var addResult = await MembershipRepository.AddAsync(membership, cancellationToken);
        if (addResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(addResult.Errors);
        }

        var updateResult = await InviteRepository.UpdateAsync(invite, observedVersion, cancellationToken);
        if (updateResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(updateResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<AcceptSpaceInviteResultDto>(saveResult.Errors);
        }

        return Result.Ok(new AcceptSpaceInviteResultDto(space.PublicId, membership.PublicId, false));
    }
}
