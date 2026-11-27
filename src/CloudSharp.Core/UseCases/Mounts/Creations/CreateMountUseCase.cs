using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Creations;

public sealed class CreateMountUseCase : IUseCase<CreateMountCommand, MountResultDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountCreationGrantTargetReader TargetReader { get; } = null!;
    private IMountCreationWriter Writer { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateMountUseCase() { }

    public CreateMountUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountCreationGrantTargetReader targetReader,
        IMountCreationWriter writer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        AuthorizationReader = authorizationReader;
        TargetReader = targetReader;
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

        if (AuthorizationReader is null || TargetReader is null || Writer is null
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
        var snapshotResult = await MountWorkflowReads.ReadActiveSpaceAsync(
            AuthorizationReader, command.Space, cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(snapshotResult.Errors);
        }

        var snapshot = snapshotResult.Value;
        var space = snapshot.Space;
        var overwritesResult = await ResolveInitialOverwritesAsync(command, space, cancellationToken);
        if (overwritesResult.IsFailed)
        {
            return Result.Fail<MountResultDto>(overwritesResult.Errors);
        }

        var actorPermissions = command.Space.EffectivePermissions
            .Where(MountScopedPermissions.Contains)
            .ToHashSet();
        if (!overwritesResult.Value.All(overwrite =>
                MountPermissionPolicy.CanChangeOverwrite(actorPermissions, null, overwrite)))
        {
            return Result.Fail<MountResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var creation = Mount.Create(
            space.Id,
            command.Name,
            command.Visibility,
            Clock.UtcNow,
            overwritesResult.Value);
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

        var access = MountPermissionPolicy.Evaluate(snapshot, command.Space.UserId, mount).Permissions;
        return Result.Ok(new MountResultDto(MountDtoMapper.ToDto(space, mount, access), mount.Version));
    }

    private async Task<Result<IReadOnlyList<MountPermissionOverwrite>>> ResolveInitialOverwritesAsync(
        CreateMountCommand command,
        Space space,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<InitialMountRoleOverwrite> roleRequests =
            command.RoleOverwrites ?? Array.Empty<InitialMountRoleOverwrite>();
        IReadOnlyList<InitialMountUserOverwrite> userRequests =
            command.UserOverwrites ?? Array.Empty<InitialMountUserOverwrite>();
        var overwrites = new List<MountPermissionOverwrite>(roleRequests.Count + userRequests.Count);
        if (roleRequests.Count == 0 && userRequests.Count == 0)
        {
            return Result.Ok<IReadOnlyList<MountPermissionOverwrite>>(overwrites);
        }

        var targetsResult = await TargetReader.ReadAsync(
            space.Id,
            roleRequests.Select(request => request.RolePublicId).ToArray(),
            userRequests.Select(request => request.UserPublicId).ToArray(),
            cancellationToken);
        if (targetsResult.IsFailed)
        {
            return Result.Fail<IReadOnlyList<MountPermissionOverwrite>>(targetsResult.Errors);
        }

        var requests = roleRequests
            .Select(request => (MountOverwriteTargetType.Role, request.RolePublicId, request.Allow, request.Deny))
            .Concat(userRequests.Select(request =>
                (MountOverwriteTargetType.User, request.UserPublicId, request.Allow, request.Deny)));
        foreach (var (targetType, targetPublicId, allow, deny) in requests)
        {
            var targetId = MountOverwriteMutationWorkflow.ResolveTargetId(
                space,
                targetsResult.Value,
                targetType,
                targetPublicId,
                isRemoval: false);
            if (targetId.IsFailed)
            {
                return Result.Fail<IReadOnlyList<MountPermissionOverwrite>>(targetId.Errors);
            }

            var overwrite = MountPermissionOverwrite.Create(targetType, targetId.Value, allow, deny);
            if (overwrite.IsFailed)
            {
                return Result.Fail<IReadOnlyList<MountPermissionOverwrite>>(overwrite.Errors);
            }

            overwrites.Add(overwrite.Value);
        }

        return Result.Ok<IReadOnlyList<MountPermissionOverwrite>>(overwrites);
    }
}
