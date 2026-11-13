using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Details;

public sealed class GetSpaceMembershipUseCase : IUseCase<GetSpaceMembershipQuery, SpaceMembershipResultDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;

    public GetSpaceMembershipUseCase() { }

    internal GetSpaceMembershipUseCase(ISpaceRepository spaceRepository, ISpaceMembershipReader membershipReader)
    {
        SpaceRepository = spaceRepository;
        MembershipReader = membershipReader;
    }

    public async Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        GetSpaceMembershipQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceMembershipQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceMembershipResultDto>();
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed());
        }

        if (!query.Space.EffectivePermissions.Contains(SpacePermission.MembersView))
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.PermissionDenied());
        }

        if (SpaceRepository is null || MembershipReader is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(CommonError.DependencyUnavailable());
        }

        var findResult = await SpaceRepository.FindByIdAsync(query.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipResultDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var readResult = await MembershipReader.ReadAsync(
            space.Id,
            query.MembershipPublicId,
            cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipResultDto>(readResult.Errors);
        }

        var snapshot = readResult.Value;
        if (snapshot is null)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceMembershipError.NotFound());
        }

        var membership = snapshot.Membership;
        if (membership.SpaceId != space.Id
            || membership.PublicId != query.MembershipPublicId
            || membership.UserId == space.OwnerUserId)
        {
            return Result.Fail<SpaceMembershipResultDto>(SpaceMembershipError.NotFound());
        }

        return Result.Ok(new SpaceMembershipResultDto(
            SpaceMembershipDto.FromSnapshot(snapshot),
            membership.Version));
    }
}
