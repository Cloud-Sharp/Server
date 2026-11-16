using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Listings;

public sealed class ListSpaceInvitesUseCase : IUseCase<ListSpaceInvitesQuery, SpaceInvitePageDto>
{
    public Task<Result<SpaceInvitePageDto>> ExecuteAsync(
        ListSpaceInvitesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceInvitesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceInvitePageDto>());
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<SpaceInvitePageDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<SpaceInvitePageDto>>(new NotImplementedException());
    }
}
