using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
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

    public LeaveSpaceUseCase(
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

    public async Task<Result<LeaveSpaceResultDto>> ExecuteAsync(
        LeaveSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new LeaveSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<LeaveSpaceResultDto>();
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(actorValidation.Errors);
        }

        if (SpaceRepository is null || MembershipRepository is null || UnitOfWork is null
            || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<LeaveSpaceResultDto>(CommonError.DependencyUnavailable());
        }

        var result = await TransactionExecutor.ExecuteAsync(
            ct => LeaveInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);

        return result.Errors.OfType<LeaveConcurrencyError>().SingleOrDefault() is { } conflict
            ? await ResolveConcurrencyAsync(conflict, command.Actor.UserId, cancellationToken)
            : result;
    }

    private async Task<Result<LeaveSpaceResultDto>> LeaveInTransactionAsync(
        LeaveSpaceCommand command,
        CancellationToken cancellationToken)
    {
        var findResult = await SpaceRepository.FindByPublicIdAsync(command.SpacePublicId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<LeaveSpaceResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.PublicId != command.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<LeaveSpaceResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        if (space.OwnerUserId == command.Actor.UserId)
        {
            return Result.Fail<LeaveSpaceResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var membershipResult = await MembershipRepository.FindLatestByUserIdAsync(
            space.Id,
            command.Actor.UserId,
            cancellationToken);
        if (membershipResult.IsFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(membershipResult.Errors);
        }

        var membership = membershipResult.Value;
        if (membership is null
            || membership.SpaceId != space.Id
            || membership.UserId != command.Actor.UserId)
        {
            return Result.Fail<LeaveSpaceResultDto>(SpaceMembershipError.NotFound());
        }

        if (membership.Status == SpaceMembershipStatus.Left)
        {
            return Result.Ok(new LeaveSpaceResultDto());
        }

        var observedVersion = membership.Version;
        var departure = membership.Leave(Clock.UtcNow);
        if (departure.IsFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(departure.Errors);
        }

        var updateResult = await MembershipRepository.UpdateAsync(
            membership,
            observedVersion,
            cancellationToken);
        if (updateResult.IsFailed)
        {
            return MapWriteFailure(updateResult.Errors, membership);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return MapWriteFailure(saveResult.Errors, membership);
        }

        return Result.Ok(new LeaveSpaceResultDto());
    }

    private async Task<Result<LeaveSpaceResultDto>> ResolveConcurrencyAsync(
        LeaveConcurrencyError conflict,
        long userId,
        CancellationToken cancellationToken)
    {
        var result = await MembershipRepository.FindByPublicIdAsync(
            conflict.SpaceId,
            conflict.MembershipPublicId,
            cancellationToken);
        if (result.IsFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(result.Errors);
        }

        var membership = result.Value;
        return membership is { Status: SpaceMembershipStatus.Left }
            && membership.SpaceId == conflict.SpaceId
            && membership.Id == conflict.MembershipId
            && membership.PublicId == conflict.MembershipPublicId
            && membership.UserId == userId
            ? Result.Ok(new LeaveSpaceResultDto())
            : Result.Fail<LeaveSpaceResultDto>(CommonError.RequestConflict());
    }

    private static Result<LeaveSpaceResultDto> MapWriteFailure(
        IReadOnlyList<IError> errors,
        SpaceMembership membership)
    {
        if (errors is [var error]
            && error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is ErrorCodes.Common.PreconditionFailed)
        {
            return Result.Fail<LeaveSpaceResultDto>(new LeaveConcurrencyError(membership));
        }

        return Result.Fail<LeaveSpaceResultDto>(errors);
    }

    private sealed class LeaveConcurrencyError(SpaceMembership membership)
        : Error("The Membership changed while saving the departure.")
    {
        internal long SpaceId { get; } = membership.SpaceId;
        internal long MembershipId { get; } = membership.Id;
        internal Guid MembershipPublicId { get; } = membership.PublicId;
    }
}
