using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Creations;

public sealed class CreateSpaceInviteUseCase : IUseCase<CreateSpaceInviteCommand, CreateSpaceInviteResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private ITokenIssuer TokenIssuer { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateSpaceInviteUseCase(IClock clock) => Clock = clock;

    public CreateSpaceInviteUseCase(
        ISpaceRepository spaceRepository,
        ISpaceInviteRepository inviteRepository,
        ITokenIssuer tokenIssuer,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        SpaceRepository = spaceRepository;
        InviteRepository = inviteRepository;
        TokenIssuer = tokenIssuer;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<CreateSpaceInviteResultDto>> ExecuteAsync(
        CreateSpaceInviteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceInviteCommandValidator(Clock).Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<CreateSpaceInviteResultDto>();
        }

        if (command.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!command.Space.EffectivePermissions.Contains(SpacePermission.InvitesManage))
        {
            return Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (SpaceRepository is null || InviteRepository is null || TokenIssuer is null
            || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => CreateInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<CreateSpaceInviteResultDto>> CreateInTransactionAsync(
        CreateSpaceInviteCommand command,
        CancellationToken cancellationToken)
    {
        var context = command.Space;
        var findResult = await SpaceRepository.FindByIdAsync(context.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != context.SpaceId || space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status != SpaceStatus.Active)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var issued = TokenIssuer.Issue(TokenKind.SpaceInvite);
        var hashResult = TokenHash.Create(issued.HashedToken);
        if (hashResult.IsFailed)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(hashResult.Errors);
        }

        var creation = SpaceInvite.Create(
            space.Id,
            context.UserId,
            hashResult.Value,
            command.ExpiresAt,
            command.MaxUses,
            Clock.UtcNow);
        if (creation.IsFailed)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(creation.Errors);
        }

        var invite = creation.Value;
        var addResult = await InviteRepository.AddAsync(invite, cancellationToken);
        if (addResult.IsFailed)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(addResult.Errors);
        }

        var saveResult = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
        {
            return Result.Fail<CreateSpaceInviteResultDto>(saveResult.Errors);
        }

        return Result.Ok(new CreateSpaceInviteResultDto(
            invite.PublicId,
            issued.PlainToken,
            $"/public/v2/invites/{issued.PlainToken}",
            invite.ExpiresAt,
            invite.MaxUses,
            invite.Version));
    }
}
