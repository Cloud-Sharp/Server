using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.QuotaChanges;

public sealed class ChangeSpaceQuotaUseCase : IUseCase<ChangeSpaceQuotaCommand, SpaceQuotaDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public ChangeSpaceQuotaUseCase() { }

    internal ChangeSpaceQuotaUseCase(
        ISpaceRepository spaceRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<SpaceQuotaDto>> ExecuteAsync(
        ChangeSpaceQuotaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ChangeSpaceQuotaCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceQuotaDto>();
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (SpaceRepository is null || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceQuotaDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => ChangeQuotaInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<SpaceQuotaDto>> ChangeQuotaInTransactionAsync(
        ChangeSpaceQuotaCommand command,
        CancellationToken cancellationToken)
    {
        var findResult = await SpaceRepository.FindByIdAsync(command.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceQuotaDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != command.Space.SpaceId || space.PublicId != command.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.OwnerUserId != command.Space.UserId)
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<SpaceQuotaDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        if (space.Version != command.ExpectedVersion)
        {
            return Result.Fail<SpaceQuotaDto>(CommonError.PreconditionFailed());
        }

        var observedVersion = space.Version;
        var changeResult = space.ChangeQuota(command.AllowedBytes, Clock.UtcNow);
        if (changeResult.IsFailed)
        {
            return Result.Fail<SpaceQuotaDto>(changeResult.Errors);
        }

        if (space.Version != observedVersion)
        {
            var updateResult = await SpaceRepository.UpdateAsync(space, observedVersion, cancellationToken);
            if (updateResult.IsFailed)
            {
                return Result.Fail<SpaceQuotaDto>(updateResult.Errors);
            }

            var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailed)
            {
                return Result.Fail<SpaceQuotaDto>(saveResult.Errors);
            }
        }

        return Result.Ok(SpaceQuotaDto.FromSpace(space));
    }
}
