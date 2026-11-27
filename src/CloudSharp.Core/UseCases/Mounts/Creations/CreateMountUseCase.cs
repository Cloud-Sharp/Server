using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

public sealed class CreateMountUseCase : IUseCase<CreateMountCommand, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountCreationGrantTargetReader GrantTargetReader { get; } = null!;
    private IMountCreationWriter Writer { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateMountUseCase() { }

    public CreateMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountCreationGrantTargetReader grantTargetReader,
        IMountCreationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        AuthorizationReader = authorizationReader;
        GrantTargetReader = grantTargetReader;
        Writer = writer;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<MountResultDto>> ExecuteAsync(
        CreateMountCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateMountCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountResultDto>();
        }

        if (AuthorizationReader is null || GrantTargetReader is null || Writer is null
            || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<MountResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => CreateInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<MountResultDto>> CreateInTransactionAsync(
        CreateMountCommand command,
        CancellationToken cancellationToken)
    {
        var snapshotResult = await AuthorizationReader.ReadAsync(
            command.SpacePublicId,
            command.Actor.UserId,
            cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(snapshotResult.Errors);
        }

        var authorization = SessionSpaceAuthorizationPolicy.Evaluate(
            new SpaceAuthorizationQuery(
                command.Actor,
                command.SpacePublicId,
                Array.Empty<SpacePermission>(),
                new[] { SpaceStatus.Active },
                allowSystemAdminBypass: false),
            snapshotResult.Value);
        if (authorization.IsFailed)
        {
            return Result.Fail<MountResultDto>(authorization.Errors);
        }

        var snapshot = snapshotResult.Value!;
        var space = snapshot.Space;
        var actorRoleIds = GetCurrentRoleIds(snapshot, command.Actor.UserId);
        var isManager = space.OwnerUserId == command.Actor.UserId
            || snapshot.Roles.Any(role => actorRoleIds.Contains(role.Id)
                && role.SpaceId == space.Id
                && role.DeletedAt is null
                && role.SystemKey == SpaceRoleSystemKey.Admin);
        if (!isManager)
        {
            return Result.Fail<MountResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var grantsResult = await ResolveInitialGrantsAsync(command, space, cancellationToken);
        if (grantsResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(grantsResult.Errors);
        }

        var creation = Mount.Create(
            space.Id,
            command.Name,
            command.Visibility,
            Clock.UtcNow,
            grantsResult.Value);
        if (creation.IsFailed)
        {
            return Result.Fail<MountResultDto>(creation.Errors);
        }

        var mount = creation.Value;
        var registration = await Writer.AddAsync(mount, cancellationToken);
        if (registration.IsFailed)
        {
            return Result.Fail<MountResultDto>(registration.Errors);
        }

        var save = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed)
        {
            return Result.Fail<MountResultDto>(save.Errors);
        }

        var permissions = new HashSet<MountPermission>
        {
            MountPermission.FilesRead,
            MountPermission.FilesDownload,
        };
        foreach (var grant in mount.Grants.Where(grant =>
                     (grant.TargetType == MountGrantTargetType.Role && actorRoleIds.Contains(grant.TargetId))
                     || (grant.TargetType == MountGrantTargetType.User && grant.TargetId == command.Actor.UserId)))
        {
            permissions.UnionWith(grant.Permissions);
        }

        var dto = new MountDto(
            space.PublicId,
            mount.PublicId,
            mount.Name,
            mount.Visibility,
            mount.Status,
            new MountAccessDto(permissions, CanManage: true),
            mount.CreatedAt,
            mount.UpdatedAt,
            mount.ArchivedAt);
        return Result.Ok(new MountResultDto(dto, mount.Version));
    }

    private async Task<Result<IReadOnlyList<MountAccessGrant>>> ResolveInitialGrantsAsync(
        CreateMountCommand command,
        Space space,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InitialMountRoleGrant> roleRequests =
            command.RoleGrants ?? Array.Empty<InitialMountRoleGrant>();
        IReadOnlyList<InitialMountUserGrant> userRequests =
            command.UserGrants ?? Array.Empty<InitialMountUserGrant>();
        var grants = new List<MountAccessGrant>(roleRequests.Count + userRequests.Count);
        if (roleRequests.Count == 0 && userRequests.Count == 0)
        {
            return Result.Ok<IReadOnlyList<MountAccessGrant>>(grants);
        }

        var targetsResult = await GrantTargetReader.ReadAsync(
            space.Id,
            roleRequests.Select(request => request.RolePublicId).ToArray(),
            userRequests.Select(request => request.UserPublicId).ToArray(),
            cancellationToken);
        if (targetsResult.IsFailed)
        {
            return Result.Fail<IReadOnlyList<MountAccessGrant>>(targetsResult.Errors);
        }

        var targets = targetsResult.Value;
        foreach (var request in roleRequests)
        {
            if (!targets.Roles.TryGetValue(request.RolePublicId, out var role) || role.SpaceId != space.Id)
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(CommonError.ResourceNotFound());
            }

            if (role.DeletedAt is not null)
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(CommonError.RequestConflict());
            }

            var grant = MountAccessGrant.Create(MountGrantTargetType.Role, role.Id, request.Permissions);
            if (grant.IsFailed)
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(grant.Errors);
            }

            grants.Add(grant.Value);
        }

        foreach (var request in userRequests)
        {
            if (!targets.Users.TryGetValue(request.UserPublicId, out var target))
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(CommonError.ResourceNotFound());
            }

            var user = target.User;
            var isOwner = user.Id == space.OwnerUserId;
            var membership = target.Membership;
            var hasMatchingMembership = membership is not null
                && membership.SpaceId == space.Id
                && membership.UserId == user.Id;
            if (!isOwner && !hasMatchingMembership)
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(CommonError.ResourceNotFound());
            }

            if (user.Status != UserStatus.Active
                || (!isOwner && (membership!.Id <= 0 || membership.Status != SpaceMembershipStatus.Active)))
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(CommonError.RequestConflict());
            }

            var grant = MountAccessGrant.Create(MountGrantTargetType.User, user.Id, request.Permissions);
            if (grant.IsFailed)
            {
                return Result.Fail<IReadOnlyList<MountAccessGrant>>(grant.Errors);
            }

            grants.Add(grant.Value);
        }

        return Result.Ok<IReadOnlyList<MountAccessGrant>>(grants);
    }

    private static HashSet<long> GetCurrentRoleIds(SpaceAuthorizationSnapshot snapshot, long userId)
    {
        var membership = snapshot.Membership;
        if (membership is null || membership.Id <= 0 || membership.SpaceId != snapshot.Space.Id
            || membership.UserId != userId || membership.Status != SpaceMembershipStatus.Active)
        {
            return new HashSet<long>();
        }

        return membership.RoleAssignments.Select(assignment => assignment.RoleId).ToHashSet();
    }
}
