using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Listings;

public sealed class ListSpaceMembershipsUseCase : IUseCase<ListSpaceMembershipsQuery, SpaceMembershipPageDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;

    public ListSpaceMembershipsUseCase() { }

    public ListSpaceMembershipsUseCase(ISpaceRepository spaceRepository, ISpaceMembershipReader membershipReader)
    {
        SpaceRepository = spaceRepository;
        MembershipReader = membershipReader;
    }

    public async Task<Result<SpaceMembershipPageDto>> ExecuteAsync(
        ListSpaceMembershipsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceMembershipsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpaceMembershipPageDto>();
        }

        if (SpaceRepository is null || MembershipReader is null)
        {
            return Result.Fail<SpaceMembershipPageDto>(CommonError.DependencyUnavailable());
        }

        var findResult = await SpaceRepository.FindByIdAsync(query.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipPageDto>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<SpaceMembershipPageDto>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<SpaceMembershipPageDto>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var readResult = await MembershipReader.ReadPageAsync(
            space.Id,
            space.OwnerUserId,
            query.Cursor,
            query.Limit,
            query.Sort,
            query.Descending,
            cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<SpaceMembershipPageDto>(readResult.Errors);
        }

        var page = readResult.Value;
        return Result.Ok(new SpaceMembershipPageDto(
            page.Items.Select(SpaceMembershipDto.FromSnapshot).ToArray(),
            page.NextCursor));
    }
}
