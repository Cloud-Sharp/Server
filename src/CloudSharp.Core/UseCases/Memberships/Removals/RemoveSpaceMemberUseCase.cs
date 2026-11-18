using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Removals;

public sealed class RemoveSpaceMemberUseCase : IUseCase<RemoveSpaceMemberCommand, RemoveSpaceMemberResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RemoveSpaceMemberUseCase() { }

    public RemoveSpaceMemberUseCase(
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

    public async Task<Result<RemoveSpaceMemberResultDto>> ExecuteAsync(
        RemoveSpaceMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RemoveSpaceMemberCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<RemoveSpaceMemberResultDto>();
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!command.Space.EffectivePermissions.Contains(SpacePermission.MembersManage))
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (SpaceRepository is null || MembershipRepository is null || UnitOfWork is null
            || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => RemoveInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<RemoveSpaceMemberResultDto>> RemoveInTransactionAsync(
        RemoveSpaceMemberCommand command,
        CancellationToken cancellationToken)
    {
        var findResult = await SpaceRepository.FindByIdAsync(command.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != command.Space.SpaceId || space.PublicId != command.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var membershipResult = await MembershipRepository.FindByPublicIdAsync(
            space.Id,
            command.MembershipPublicId,
            cancellationToken);
        if (membershipResult.IsFailed)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(membershipResult.Errors);
        }

        var membership = membershipResult.Value;
        if (membership is null
            || membership.SpaceId != space.Id
            || membership.PublicId != command.MembershipPublicId
            || membership.UserId == space.OwnerUserId)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(SpaceMembershipError.NotFound());
        }

        if (membership.Version != command.ExpectedVersion)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(CommonError.PreconditionFailed());
        }

        var observedVersion = membership.Version;
        var removal = membership.Remove(Clock.UtcNow);
        if (removal.IsFailed)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(removal.Errors);
        }

        var updateResult = await MembershipRepository.UpdateAsync(
            membership,
            observedVersion,
            cancellationToken);
        if (updateResult.IsFailed)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(updateResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<RemoveSpaceMemberResultDto>(saveResult.Errors);
        }

        return Result.Ok(new RemoveSpaceMemberResultDto(membership.Version));
    }
}
