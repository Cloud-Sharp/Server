using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.DeletionRequests;

public sealed class RequestSpaceDeletionUseCase : IUseCase<RequestSpaceDeletionCommand, SpaceDeletionAcceptedDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public RequestSpaceDeletionUseCase() { }

    public RequestSpaceDeletionUseCase(
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

    public async Task<Result<SpaceDeletionAcceptedDto>> ExecuteAsync(
        RequestSpaceDeletionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new RequestSpaceDeletionCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceDeletionAcceptedDto>();
        }

        if (SpaceRepository is null || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => RequestDeletionInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<SpaceDeletionAcceptedDto>> RequestDeletionInTransactionAsync(
        RequestSpaceDeletionCommand command,
        CancellationToken cancellationToken)
    {
        var findResult = await SpaceRepository.FindByIdAsync(command.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.OwnerUserId != command.Space.UserId)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        if (space.Version != command.ExpectedVersion)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(CommonError.PreconditionFailed());
        }

        var expectedVersion = space.Version;
        var deletion = space.BeginDelete(Clock.UtcNow);
        if (deletion.IsFailed)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(deletion.Errors);
        }

        var updateResult = await SpaceRepository.UpdateAsync(space, expectedVersion, cancellationToken);
        if (updateResult.IsFailed)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(updateResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<SpaceDeletionAcceptedDto>(saveResult.Errors);
        }

        return Result.Ok(new SpaceDeletionAcceptedDto(
            space.PublicId,
            space.Status,
            space.DeletingAt!.Value,
            space.Version));
    }
}
