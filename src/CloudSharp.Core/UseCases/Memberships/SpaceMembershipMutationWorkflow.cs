using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships;

internal sealed class SpaceMembershipMutationWorkflow
{
    private ISpaceRepository SpaceRepository { get; }
    private ISpaceMembershipReader MembershipReader { get; }
    private ISpaceMembershipRepository MembershipRepository { get; }
    private IUnitOfWork UnitOfWork { get; }
    private ITransactionExecutor TransactionExecutor { get; }
    private IClock Clock { get; }

    internal SpaceMembershipMutationWorkflow(
        ISpaceRepository spaceRepository,
        ISpaceMembershipReader membershipReader,
        ISpaceMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        MembershipReader = membershipReader;
        MembershipRepository = membershipRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    internal async Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        AuthorizedSpaceContext context,
        Guid membershipPublicId,
        long expectedVersion,
        Func<SpaceMembershipReadSnapshot, DateTimeOffset, CancellationToken,
            Task<Result<SpaceMembershipReadSnapshot>>> operation,
        CancellationToken cancellationToken = default) =>
        await TransactionExecutor.ExecuteAsync(
            ct => ExecuteInTransactionAsync(context, membershipPublicId, expectedVersion, operation, ct),
            cancellationToken: cancellationToken);

    private async Task<Result<SpaceMembershipResultDto>> ExecuteInTransactionAsync(
        AuthorizedSpaceContext context,
        Guid membershipPublicId,
        long expectedVersion,
        Func<SpaceMembershipReadSnapshot, DateTimeOffset, CancellationToken,
            Task<Result<SpaceMembershipReadSnapshot>>> operation,
        CancellationToken cancellationToken)
    {
        var findResult = await SpaceRepository.FindByIdAsync(context.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != context.SpaceId || space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var readResult = await MembershipReader.ReadAsync(space.Id, membershipPublicId, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipResultDto>(readResult.Errors);
        }

        var snapshot = readResult.Value;
        if (snapshot is null
            || snapshot.Membership.SpaceId != space.Id
            || snapshot.Membership.PublicId != membershipPublicId
            || snapshot.Membership.UserId == space.OwnerUserId)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceMembershipError.NotFound());
        }

        var membership = snapshot.Membership;
        if (membership.Version != expectedVersion)
        {
            return Result.Fail<SpaceMembershipResultDto>(CommonError.PreconditionFailed());
        }

        if (membership.Status == SpaceMembershipStatus.Left)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceMembershipError.Inactive());
        }

        var observedVersion = membership.Version;
        var operationResult = await operation(snapshot, Clock.UtcNow, cancellationToken);
        if (operationResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipResultDto>(operationResult.Errors);
        }

        if (membership.Version > observedVersion)
        {
            var updateResult = await MembershipRepository.UpdateAsync(
                membership,
                observedVersion,
                cancellationToken);
            if (updateResult.IsFailed)
            {
                return Result.Fail<SpaceMembershipResultDto>(updateResult.Errors);
            }

            var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                return Result.Fail<SpaceMembershipResultDto>(saveResult.Errors);
            }
        }

        return Result.Ok(new SpaceMembershipResultDto(
            SpaceMembershipDto.FromSnapshot(operationResult.Value),
            membership.Version));
    }
}
