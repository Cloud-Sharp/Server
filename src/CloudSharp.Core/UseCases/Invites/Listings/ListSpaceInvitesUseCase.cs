using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Listings;

public sealed class ListSpaceInvitesUseCase : IUseCase<ListSpaceInvitesQuery, SpaceInvitePageDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceInviteReader InviteReader { get; } = null!;
    private ITokenHasher TokenHasher { get; } = null!;

    public ListSpaceInvitesUseCase() { }

    public ListSpaceInvitesUseCase(
        ISpaceRepository spaceRepository,
        ISpaceInviteReader inviteReader,
        ITokenHasher tokenHasher)
    {
        SpaceRepository = spaceRepository;
        InviteReader = inviteReader;
        TokenHasher = tokenHasher;
    }

    public async Task<Result<SpaceInvitePageDto>> ExecuteAsync(
        ListSpaceInvitesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceInvitesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceInvitePageDto>();
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceInvitePageDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!query.Space.EffectivePermissions.Contains(SpacePermission.InvitesManage))
        {
            return Result.Fail<SpaceInvitePageDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (SpaceRepository is null || InviteReader is null || TokenHasher is null)
        {
            return Result.Fail<SpaceInvitePageDto>(CommonError.DependencyUnavailable());
        }

        var findResult = await SpaceRepository.FindByIdAsync(query.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceInvitePageDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceInvitePageDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceInvitePageDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var requesterUserId = query.Space.UserId;
        string? readerCursor = null;
        if (query.Cursor is { } cursor
            && !SpaceInviteListCursor.TryOpen(cursor, requesterUserId, TokenHasher, out readerCursor))
        {
            return Result.Fail<SpaceInvitePageDto>(SpaceError.ListCursorInvalid());
        }

        var readResult = await InviteReader.ReadPageAsync(
            space.Id,
            readerCursor,
            query.Limit,
            query.Sort,
            query.Descending,
            cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceInvitePageDto>(readResult.Errors);
        }

        var page = readResult.Value;
        var nextCursor = page.NextCursor is { } nextReaderCursor
            ? SpaceInviteListCursor.Issue(nextReaderCursor, requesterUserId, TokenHasher)
            : null;
        return Result.Ok(new SpaceInvitePageDto(
            page.Items.Select(SpaceInviteDto.FromInvite).ToArray(),
            nextCursor));
    }
}
