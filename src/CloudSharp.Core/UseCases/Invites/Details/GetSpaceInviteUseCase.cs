using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Invites.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Invites.Details;

public sealed class GetSpaceInviteUseCase(
    ISpaceRepository spaceRepository,
    ISpaceInviteRepository inviteRepository) : IUseCase<GetSpaceInviteQuery, SpaceInviteResultDto>
{
    public async Task<Result<SpaceInviteResultDto>> ExecuteAsync(
        GetSpaceInviteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = new GetSpaceInviteQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceInviteResultDto>();
        }

        var context = query.Space;
        if (context.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceInviteResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!context.EffectivePermissions.Contains(SpacePermission.InvitesManage))
        {
            return Result.Fail<SpaceInviteResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        var spaceResult = await spaceRepository.FindByIdAsync(context.SpaceId, cancellationToken);
        if (spaceResult.IsFailed)
        {
            return Result.Fail<SpaceInviteResultDto>(spaceResult.Errors);
        }

        var space = spaceResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceInviteResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != context.SpaceId || space.PublicId != context.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceInviteResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var inviteResult = await inviteRepository.FindByPublicIdAsync(space.Id, query.InvitePublicId, cancellationToken);
        if (inviteResult.IsFailed)
        {
            return Result.Fail<SpaceInviteResultDto>(inviteResult.Errors);
        }

        var invite = inviteResult.Value;
        if (invite is null || invite.SpaceId != space.Id || invite.PublicId != query.InvitePublicId)
        {
            return Result.Fail<SpaceInviteResultDto>(SpaceInviteError.NotFound());
        }

        return Result.Ok(new SpaceInviteResultDto(SpaceInviteDto.FromInvite(invite), invite.Version));
    }
}
