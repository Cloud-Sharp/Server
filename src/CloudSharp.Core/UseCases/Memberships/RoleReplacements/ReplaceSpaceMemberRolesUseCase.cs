using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.RoleReplacements;

public sealed class ReplaceSpaceMemberRolesUseCase : IUseCase<ReplaceSpaceMemberRolesCommand, SpaceMembershipResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;
    private ISpaceMembershipRepository MembershipRepository { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;
    private SpaceMembershipMutationWorkflow MutationWorkflow { get; } = null!;

    public ReplaceSpaceMemberRolesUseCase() { }

    public ReplaceSpaceMemberRolesUseCase(
        ISpaceRepository spaceRepository,
        ISpaceMembershipReader membershipReader,
        ISpaceRoleReader roleReader,
        ISpaceMembershipRepository membershipRepository,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        MembershipReader = membershipReader;
        RoleReader = roleReader;
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
        ReplaceSpaceMemberRolesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new ReplaceSpaceMemberRolesCommandValidator().Validate(command);
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

        if (SpaceRepository is null || MembershipReader is null || RoleReader is null
            || MembershipRepository is null || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(CommonError.DependencyUnavailable());
        }

        return await MutationWorkflow.ExecuteAsync(
            command.Space,
            command.MembershipPublicId,
            command.ExpectedVersion,
            (snapshot, now, ct) => ReplaceRolesAsync(command, snapshot, now, ct),
            cancellationToken);
    }

    private async Task<Result<SpaceMembershipReadSnapshot>> ReplaceRolesAsync(
        ReplaceSpaceMemberRolesCommand command,
        SpaceMembershipReadSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var readResult = await RoleReader.ReadAsync(snapshot.Membership.SpaceId, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipReadSnapshot>(readResult.Errors);
        }

        var resolvedRoles = new List<SpaceRole>(command.RolePublicIds.Count);
        foreach (var rolePublicId in command.RolePublicIds)
        {
            var matches = readResult.Value
                .Where(role => role.PublicId == rolePublicId)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                return Result.Fail<SpaceMembershipReadSnapshot>(
                    SpaceMembershipError.RoleInvalid("Each role public ID must resolve to exactly one role."));
            }

            var role = matches[0];
            if (role.Id <= 0
                || role.SpaceId != snapshot.Membership.SpaceId
                || role.DeletedAt is not null)
            {
                return Result.Fail<SpaceMembershipReadSnapshot>(
                    SpaceMembershipError.RoleInvalid("Only persisted, non-deleted roles in the target space can be assigned."));
            }

            resolvedRoles.Add(role);
        }

        var replacement = snapshot.Membership.ReplaceRoles(resolvedRoles, command.Space.UserId, now);
        if (replacement.IsFailed)
        {
            return Result.Fail<SpaceMembershipReadSnapshot>(replacement.Errors);
        }

        return Result.Ok(snapshot with { Roles = resolvedRoles });
    }
}
