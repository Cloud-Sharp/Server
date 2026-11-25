using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Activations;

public sealed class ActivateSpaceMemberUseCase : IUseCase<ActivateSpaceMemberCommand, SpaceMembershipResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;
    private SpaceMembershipMutationWorkflow MutationWorkflow { get; } = null!;

    public ActivateSpaceMemberUseCase() { }

    public ActivateSpaceMemberUseCase(
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
        ActivateSpaceMemberCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ActivateSpaceMemberCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceMembershipResultDto>();
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
            ActivateAsync,
            cancellationToken);
    }

    private static Task<Result<SpaceMembershipReadSnapshot>> ActivateAsync(
        SpaceMembershipReadSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken _)
    {
        var activation = snapshot.Membership.Activate(now);
        return Task.FromResult(activation.IsFailed
            ? Result.Fail<SpaceMembershipReadSnapshot>(activation.Errors)
            : Result.Ok(snapshot));
    }
}
