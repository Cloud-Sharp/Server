using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Abstractions.Transactions;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Creations;

public sealed class CreateSpaceUseCase : IUseCase<CreateSpaceCommand, SpaceResultDto>
{
    private IUserRepository UserRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceBootstrapWriter BootstrapWriter { get; } = null!;
    private IUnitOfWork UnitOfWork { get; } = null!;
    private ITransactionExecutor TransactionExecutor { get; } = null!;
    private IClock Clock { get; } = null!;

    public CreateSpaceUseCase() { }

    public CreateSpaceUseCase(
        IUserRepository userRepository,
        ISpaceRepository spaceRepository,
        ISpaceBootstrapWriter bootstrapWriter,
        IUnitOfWork unitOfWork,
        ITransactionExecutor transactionExecutor,
        IClock clock)
    {
        UserRepository = userRepository;
        SpaceRepository = spaceRepository;
        BootstrapWriter = bootstrapWriter;
        UnitOfWork = unitOfWork;
        TransactionExecutor = transactionExecutor;
        Clock = clock;
    }

    public async Task<Result<SpaceResultDto>> ExecuteAsync(
        CreateSpaceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = new CreateSpaceCommandValidator().Validate(command);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceResultDto>();
        }

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(command.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(actorValidation.Errors);
        }

        if (UserRepository is null || SpaceRepository is null || BootstrapWriter is null
            || UnitOfWork is null || TransactionExecutor is null || Clock is null)
        {
            return Result.Fail<SpaceResultDto>(CommonError.DependencyUnavailable());
        }

        return await TransactionExecutor.ExecuteAsync(
            ct => CreateInTransactionAsync(command, ct),
            cancellationToken: cancellationToken);
    }

    private async Task<Result<SpaceResultDto>> CreateInTransactionAsync(
        CreateSpaceCommand command,
        CancellationToken cancellationToken)
    {
        var userResult = await UserRepository.FindByIdAsync(command.Actor.UserId, cancellationToken);
        if (userResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(userResult.Errors);
        }

        var user = userResult.Value;
        if (user is null || user.Status != UserStatus.Active)
        {
            return Result.Fail<SpaceResultDto>(SessionError.AuthUserInactive());
        }

        var now = Clock.UtcNow;
        var creation = Space.Create(
            user.Id,
            command.Name,
            command.Description,
            command.AllowedBytes,
            now);
        if (creation.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(creation.Errors);
        }

        var space = creation.Value;
        var addResult = await SpaceRepository.AddAsync(space, cancellationToken);
        if (addResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(addResult.Errors);
        }

        var firstSave = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (firstSave.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(firstSave.Errors);
        }

        var reloadResult = await SpaceRepository.FindByPublicIdAsync(space.PublicId, cancellationToken);
        if (reloadResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(reloadResult.Errors);
        }

        var savedSpace = reloadResult.Value;
        if (savedSpace is null || savedSpace.Id <= 0 || savedSpace.PublicId != space.PublicId)
        {
            throw new InvalidOperationException(
                "SpaceRepository returned a space without a generated id or with a mismatched identity.");
        }

        var roles = new List<SpaceRole>(3);
        foreach (var systemKey in new[]
                 {
                     SpaceRoleSystemKey.Admin,
                     SpaceRoleSystemKey.Member,
                     SpaceRoleSystemKey.Viewer,
                 })
        {
            var roleResult = SpaceRole.CreateManaged(savedSpace.Id, systemKey, now);
            if (roleResult.IsFailed)
            {
                return Result.Fail<SpaceResultDto>(roleResult.Errors);
            }

            roles.Add(roleResult.Value);
        }

        var bootstrapResult = await BootstrapWriter.AddAsync(
            savedSpace.Id,
            roles,
            now,
            cancellationToken);
        if (bootstrapResult.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(bootstrapResult.Errors);
        }

        var secondSave = await UnitOfWork.SaveChangesAsync(cancellationToken);
        if (secondSave.IsFailed)
        {
            return Result.Fail<SpaceResultDto>(secondSave.Errors);
        }

        var permissions = SpacePermissionPolicy.Evaluate(savedSpace, user.Id, null, roles);
        var dto = new SpaceDto(
            savedSpace.PublicId,
            user.PublicId,
            savedSpace.Name,
            savedSpace.Description,
            savedSpace.Status,
            new SpaceAccessDto(SpaceAuthorizationSource.Owner, permissions),
            savedSpace.CreatedAt,
            savedSpace.UpdatedAt);
        return Result.Ok(new SpaceResultDto(dto, savedSpace.Version));
    }
}
