using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;

public sealed class TransferSpaceOwnershipUseCase : IUseCase<TransferSpaceOwnershipCommand, SpaceResultDto>
{
    private ISpaceDetailsReader DetailsReader { get; } = null!;
    private IUserRepository UserRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceOwnershipMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public TransferSpaceOwnershipUseCase() { }

    public TransferSpaceOwnershipUseCase(
        ISpaceDetailsReader detailsReader,
        IUserRepository userRepository,
        ISpaceRepository spaceRepository,
        ISpaceOwnershipMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        DetailsReader = detailsReader;
        UserRepository = userRepository;
        SpaceRepository = spaceRepository;
        MembershipRepository = membershipRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<SpaceResultDto>> ExecuteAsync(
        TransferSpaceOwnershipCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new TransferSpaceOwnershipCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceResultDto>();
        }

        if (DetailsReader is null || UserRepository is null || SpaceRepository is null
            || MembershipRepository is null || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => TransferInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<SpaceResultDto>> TransferInTransactionAsync(
        TransferSpaceOwnershipCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Space;
        var readResult = await DetailsReader.ReadAsync(context.SpaceId, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(readResult.Errors);
        }

        var snapshot = readResult.Value;
        if (snapshot is null)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        var space = snapshot.Space;
        if (space.Id != context.SpaceId || space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Space details reader returned a different space.");
        }

        if (space.OwnerUserId != context.UserId)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        if (space.Version != command.ExpectedVersion)
        {
            return Result.Fail<SpaceResultDto>(CommonError.PreconditionFailed());
        }

        var currentOwnerResult = await UserRepository.FindByIdAsync(context.UserId, cancellationToken);
        if (currentOwnerResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(currentOwnerResult.Errors);
        }

        var currentOwner = currentOwnerResult.Value;
        if (currentOwner is null || !currentOwner.CanLogin())
        {
            return Result.Fail<SpaceResultDto>(SessionError.AuthUserInactive());
        }

        if (command.NewOwnerUserPublicId == currentOwner.PublicId)
        {
            return Result.Ok(CreateResult(
                snapshot,
                snapshot.OwnerUserPublicId,
                context.AuthorizationSource,
                context.EffectivePermissions));
        }

        var newOwnerResult = await UserRepository.FindByPublicIdAsync(
            command.NewOwnerUserPublicId,
            cancellationToken);
        if (newOwnerResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(newOwnerResult.Errors);
        }

        var newOwner = newOwnerResult.Value;
        if (newOwner is null || !newOwner.CanLogin())
        {
            return Result.Fail<SpaceResultDto>(SpaceError.TransferTargetInvalid());
        }

        var membershipResult = await MembershipRepository.FindByUserIdAsync(
            space.Id,
            newOwner.Id,
            cancellationToken);
        if (membershipResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(membershipResult.Errors);
        }

        var newOwnerMembership = membershipResult.Value;
        if (newOwnerMembership is null
            || newOwnerMembership.Status != SpaceMembershipStatus.Active
            || newOwnerMembership.SpaceId != space.Id
            || newOwnerMembership.UserId != newOwner.Id)
        {
            return Result.Fail<SpaceResultDto>(SpaceError.TransferTargetInvalid());
        }

        var roleResult = await MembershipRepository.FindManagedAdminRoleAsync(space.Id, cancellationToken);
        if (roleResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(roleResult.Errors);
        }

        var managedAdminRole = roleResult.Value;
        if (managedAdminRole is null
            || managedAdminRole.Id <= 0
            || managedAdminRole.SpaceId != space.Id
            || !managedAdminRole.IsManaged
            || managedAdminRole.SystemKey != SpaceRoleSystemKey.Admin
            || managedAdminRole.DeletedAt is not null)
        {
            return Result.Fail<SpaceResultDto>(
                SpaceMembershipError.RoleInvalid("A persisted managed ADMIN role is required."));
        }

        var observedSpaceVersion = space.Version;
        var observedMembershipVersion = newOwnerMembership.Version;
        var now = Clock.UtcNow;
        var transferResult = space.TransferOwnership(newOwner.Id, now);
        if (transferResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(transferResult.Errors);
        }

        var membershipCreation = SpaceMembership.Create(
            space.Id,
            currentOwner.Id,
            [managedAdminRole],
            currentOwner.Id,
            now);
        if (membershipCreation.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(membershipCreation.Errors);
        }

        var deleteResult = await MembershipRepository.DeleteAsync(
            newOwnerMembership,
            observedMembershipVersion,
            cancellationToken);
        if (deleteResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(deleteResult.Errors);
        }

        var updateResult = await SpaceRepository.UpdateAsync(space, observedSpaceVersion, cancellationToken);
        if (updateResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(updateResult.Errors);
        }

        var addResult = await MembershipRepository.AddAsync(membershipCreation.Value, cancellationToken);
        if (addResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(addResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(saveResult.Errors);
        }

        var accessSource = context.AuthorizationSource == SpaceAuthorizationSource.SystemAdminBypass
            && currentOwner.SystemRole == SystemRole.SystemAdmin
                ? SpaceAuthorizationSource.SystemAdminBypass
                : SpaceAuthorizationSource.Membership;
        return Result.Ok(CreateResult(
            snapshot,
            newOwner.PublicId,
            accessSource,
            managedAdminRole.Permissions));
    }

    private static SpaceResultDto CreateResult(
        SpaceDetailsSnapshot snapshot,
        Guid ownerUserPublicId,
        SpaceAuthorizationSource authorizationSource,
        IReadOnlySet<SpacePermission> permissions)
    {
        var space = snapshot.Space;
        var dto = new SpaceDto(
            space.PublicId,
            ownerUserPublicId,
            space.Name,
            space.Description,
            space.Status,
            new SpaceAccessDto(authorizationSource, permissions),
            space.CreatedAt,
            space.UpdatedAt);
        return new SpaceResultDto(dto, space.Version);
    }
}
