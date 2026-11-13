using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.RoleListings;

public sealed class ListSpaceRolesUseCase : IUseCase<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;

    public ListSpaceRolesUseCase() { }

    internal ListSpaceRolesUseCase(ISpaceRepository spaceRepository, ISpaceRoleReader roleReader)
    {
        SpaceRepository = spaceRepository;
        RoleReader = roleReader;
    }

    public Task<Result<IReadOnlyList<SpaceRoleDto>>> ExecuteAsync(
        ListSpaceRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceRolesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return Task.FromResult(validation.ToFailureResult<IReadOnlyList<SpaceRoleDto>>());
        }

        if (query.Space.ActorType != ActorType.Session)
        {
            return Task.FromResult(Result.Fail<IReadOnlyList<SpaceRoleDto>>(SpaceAuthorizationError.TokenTypeNotAllowed()));
        }

        return Task.FromException<Result<IReadOnlyList<SpaceRoleDto>>>(new NotImplementedException());
    }
}
