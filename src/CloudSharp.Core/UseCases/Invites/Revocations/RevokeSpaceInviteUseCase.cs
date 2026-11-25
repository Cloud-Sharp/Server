using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Revocations;

public sealed class RevokeSpaceInviteUseCase : IUseCase<RevokeSpaceInviteCommand, RevokeSpaceInviteResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RevokeSpaceInviteUseCase() { }

    public RevokeSpaceInviteUseCase(
        ISpaceRepository spaceRepository,
        ISpaceInviteRepository inviteRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        InviteRepository = inviteRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<RevokeSpaceInviteResultDto>> ExecuteAsync(
        RevokeSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RevokeSpaceInviteCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<RevokeSpaceInviteResultDto>();
        }

        if (SpaceRepository is null || InviteRepository is null || UnitOfWork is null
            || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => RevokeInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<RevokeSpaceInviteResultDto>> RevokeInTransactionAsync(
        RevokeSpaceInviteCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Space;
        var findResult = await SpaceRepository.FindByIdAsync(context.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != context.SpaceId || space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var inviteResult = await InviteRepository.FindByPublicIdAsync(
            space.Id,
            command.InvitePublicId,
            cancellationToken);
        if (inviteResult.IsFailed)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(inviteResult.Errors);
        }

        var invite = inviteResult.Value;
        if (invite is null || invite.SpaceId != space.Id || invite.PublicId != command.InvitePublicId)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(SpaceInviteError.NotFound());
        }

        if (invite.Version != command.ExpectedVersion)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(CommonError.PreconditionFailed());
        }

        var observedVersion = invite.Version;
        var revocation = invite.Revoke(Clock.UtcNow);
        if (revocation.IsFailed)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(revocation.Errors);
        }

        var updateResult = await InviteRepository.UpdateAsync(invite, observedVersion, cancellationToken);
        if (updateResult.IsFailed)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(updateResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<RevokeSpaceInviteResultDto>(saveResult.Errors);
        }

        return Result.Ok(new RevokeSpaceInviteResultDto(invite.Version));
    }
}
