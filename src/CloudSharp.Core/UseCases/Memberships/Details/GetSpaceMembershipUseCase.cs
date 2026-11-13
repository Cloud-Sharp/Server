using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
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

    public Task<Result<SpaceMembershipResultDto>> ExecuteAsync(
        GetSpaceMembershipQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new GetSpaceMembershipQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<SpaceMembershipResultDto>());
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<SpaceMembershipResultDto>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<SpaceMembershipResultDto>>(new NotImplementedException());
    }
}
