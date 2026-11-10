using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces;

internal sealed class SpaceMutationWorkflow
{
    private ISpaceDetailsReader DetailsReader { get; }
    private ISpaceRepository SpaceRepository { get; }
    private IUnitOfWork UnitOfWork { get; }
    private ITransactionExecutor TransactionExecutor { get; }
    private IClock Clock { get; }

    internal SpaceMutationWorkflow(
        ISpaceDetailsReader detailsReader,
        ISpaceRepository spaceRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        DetailsReader = detailsReader;
        SpaceRepository = spaceRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    internal async Task<Result<SpaceResultDto>> ExecuteAsync(
        AuthorizedSpaceContext context,
        long expectedVersion,
        SpaceStatus requiredStatus,
        Func<Space, DateTimeOffset, Result> domainOperation,
        CancellationToken cancellationToken = default)
    {
        if (context.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!context.EffectivePermissions.Contains(SpacePermission.SpaceUpdate))
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (DetailsReader is null || SpaceRepository is null || UnitOfWork is null
            || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => ExecuteInTransactionAsync(
                context,
                expectedVersion,
                requiredStatus,
                domainOperation,
                ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<SpaceResultDto>> ExecuteInTransactionAsync(
        AuthorizedSpaceContext context,
        long expectedVersion,
        SpaceStatus requiredStatus,
        Func<Space, DateTimeOffset, Result> domainOperation,
        CancellationToken cancellationToken)
    {
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

        if (space.Status != requiredStatus)
        {
            return Result.Fail<SpaceResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        if (space.Version != expectedVersion)
        {
            return Result.Fail<SpaceResultDto>(CommonError.PreconditionFailed());
        }

        var observedVersion = space.Version;
        var mutationResult = domainOperation(space, Clock.UtcNow);
        if (mutationResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(mutationResult.Errors);
        }

        if (space.Version != observedVersion)
        {
            var updateResult = await SpaceRepository.UpdateAsync(space, observedVersion, cancellationToken);
            if (updateResult.IsFailed)
            {
                return Result.Fail<SpaceResultDto>(updateResult.Errors);
            }

            var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                return Result.Fail<SpaceResultDto>(saveResult.Errors);
            }
        }

        var dto = new SpaceDto(
            space.PublicId,
            snapshot.OwnerUserPublicId,
            space.Name,
            space.Description,
            space.Status,
            new SpaceAccessDto(context.AuthorizationSource, context.EffectivePermissions),
            snapshot.RootFolderPublicId,
            space.CreatedAt,
            space.UpdatedAt);
        return Result.Ok(new SpaceResultDto(dto, space.Version));
    }
}
