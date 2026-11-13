using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Suspensions;

public sealed class SuspendSpaceMemberUseCase : IUseCase<SuspendSpaceMemberCommand, SpaceMembershipResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;
    private SpaceMembershipMutationWorkflow MutationWorkflow { get; } = null!;

    public SuspendSpaceMemberUseCase() { }

    internal SuspendSpaceMemberUseCase(
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
        MutationWorkflow = new SpaceMembershipMutationWorkflow(
            spaceRepository,
            membershipReader,
            membershipRepository,
            unitOfWork,
            transactionExecutor,
            clock);
    }

    public async Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        SuspendSpaceMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new SuspendSpaceMemberCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceMembershipResultDto>();
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!command.Space.EffectivePermissions.Contains(SpacePermission.MembersManage))
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (SpaceRepository is null || MembershipReader is null || MembershipRepository is null
            || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(CommonError.DependencyUnavailable());
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Space,
            command.MembershipPublicId,
            command.ExpectedVersion,
            SuspendAsync,
            cancellationToken);
    }

    private static Task<Result<SpaceMembershipReadSnapshot>> SuspendAsync(
        SpaceMembershipReadSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken _)
    {
        var suspension = snapshot.Membership.Suspend(now);
        return Task.FromResult(suspension.IsFailed
            ? Result.Fail<SpaceMembershipReadSnapshot>(suspension.Errors)
            : Result.Ok(snapshot));
    }
}
