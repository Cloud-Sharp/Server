using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.Listings;

public sealed class ListSpaceMembershipsUseCase : IUseCase<ListSpaceMembershipsQuery, SpaceMembershipPageDto>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceMembershipReader MembershipReader { get; } = null!;

    public ListSpaceMembershipsUseCase() { }

    internal ListSpaceMembershipsUseCase(ISpaceRepository spaceRepository, ISpaceMembershipReader membershipReader)
    {
        SpaceRepository = spaceRepository;
        MembershipReader = membershipReader;
    }

    public Task<Result<SpaceMembershipPageDto>> ExecuteAsync(
        ListSpaceMembershipsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceMembershipsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceMembershipPageDto>());
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<SpaceMembershipPageDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<SpaceMembershipPageDto>>(new NotImplementedException());
    }
}
