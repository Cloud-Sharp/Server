using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.PublicInfo;

public sealed class GetPublicSpaceInviteUseCase : IUseCase<GetPublicSpaceInviteQuery, PublicSpaceInviteDto>
{
    private ITokenHasher TokenHasher { get; } = null!;
    private ISpaceInviteRepository InviteRepository { get; } = null!;
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;
    private IClock Clock { get; } = null!;

    public GetPublicSpaceInviteUseCase() { }

    public GetPublicSpaceInviteUseCase(
        ITokenHasher tokenHasher,
        ISpaceInviteRepository inviteRepository,
        ISpaceRepository spaceRepository,
        ISpaceRoleReader roleReader,
        IClock clock)
    {
        TokenHasher = tokenHasher;
        InviteRepository = inviteRepository;
        SpaceRepository = spaceRepository;
        RoleReader = roleReader;
        Clock = clock;
    }

    public async Task<Result<PublicSpaceInviteDto>> ExecuteAsync(
        GetPublicSpaceInviteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetPublicSpaceInviteQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<PublicSpaceInviteDto>();
        }

        if (TokenHasher is null || InviteRepository is null || SpaceRepository is null
            || RoleReader is null || Clock is null)
        {
            return Result.Fail<PublicSpaceInviteDto>(CommonError.DependencyUnavailable());
        }

        var hashResult = TokenHash.Create(TokenHasher.Hash(query.InviteToken));
        if (hashResult.IsFailed)
        {
            return Result.Fail<PublicSpaceInviteDto>(hashResult.Errors);
        }

        var inviteResult = await InviteRepository.FindByTokenHashAsync(hashResult.Value, cancellationToken);
        if (inviteResult.IsFailed)
        {
            return Result.Fail<PublicSpaceInviteDto>(inviteResult.Errors);
        }

        var invite = inviteResult.Value;
        if (invite is null || invite.CanAccept(Clock.UtcNow).IsFailed)
        {
            return Result.Fail<PublicSpaceInviteDto>(SpaceInviteError.NotFound());
        }

        var spaceResult = await SpaceRepository.FindByIdAsync(invite.SpaceId, cancellationToken);
        if (spaceResult.IsFailed)
        {
            return Result.Fail<PublicSpaceInviteDto>(spaceResult.Errors);
        }

        var space = spaceResult.Value;
        if (space is null || space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<PublicSpaceInviteDto>(SpaceInviteError.NotFound());
        }

        if (space.Id != invite.SpaceId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        var rolesResult = await RoleReader.ReadAsync(space.Id, cancellationToken);
        if (rolesResult.IsFailed)
        {
            return Result.Fail<PublicSpaceInviteDto>(rolesResult.Errors);
        }

        var viewers = rolesResult.Value
            .Where(role => role.Id > 0
                && role.SpaceId == space.Id
                && role.IsManaged
                && role.SystemKey == SpaceRoleSystemKey.Viewer
                && role.DeletedAt is null)
            .ToArray();
        if (viewers.Length != 1)
        {
            return Result.Fail<PublicSpaceInviteDto>(SpaceInviteError.NotFound());
        }

        return Result.Ok(new PublicSpaceInviteDto(space.Name, viewers[0].Name, invite.ExpiresAt));
    }
}
