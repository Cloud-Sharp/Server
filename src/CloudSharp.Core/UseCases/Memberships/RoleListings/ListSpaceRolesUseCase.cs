using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Memberships.RoleListings;

public sealed class ListSpaceRolesUseCase : IUseCase<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>>
{
    private ISpaceRepository SpaceRepository { get; } = null!;
    private ISpaceRoleReader RoleReader { get; } = null!;

    public ListSpaceRolesUseCase() { }

    public ListSpaceRolesUseCase(ISpaceRepository spaceRepository, ISpaceRoleReader roleReader)
    {
        SpaceRepository = spaceRepository;
        RoleReader = roleReader;
    }

    public async Task<Result<IReadOnlyList<SpaceRoleDto>>> ExecuteAsync(
        ListSpaceRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpaceRolesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<IReadOnlyList<SpaceRoleDto>>();
        }

        if (SpaceRepository is null || RoleReader is null)
        {
            return Result.Fail<IReadOnlyList<SpaceRoleDto>>(CommonError.DependencyUnavailable());
        }

        var findResult = await SpaceRepository.FindByIdAsync(query.Space.SpaceId, cancellationToken);
        if (findResult.IsFailed)
        {
            return Result.Fail<IReadOnlyList<SpaceRoleDto>>(findResult.Errors);
        }

        var space = findResult.Value;
        if (space is null)
        {
            return Result.Fail<IReadOnlyList<SpaceRoleDto>>(SpaceAuthorizationError.SpaceNotFound());
        }

        if (space.Id != query.Space.SpaceId || space.PublicId != query.Space.SpacePublicId)
        {
            throw new InvalidOperationException("Space repository returned a different space.");
        }

        if (space.Status is not (SpaceStatus.Active or SpaceStatus.Archived))
        {
            return Result.Fail<IReadOnlyList<SpaceRoleDto>>(SpaceAuthorizationError.StateNotAllowed(space.Status));
        }

        var readResult = await RoleReader.ReadAsync(space.Id, cancellationToken);
        if (readResult.IsFailed)
        {
            return Result.Fail<IReadOnlyList<SpaceRoleDto>>(readResult.Errors);
        }

        IReadOnlyList<SpaceRoleDto> roles = readResult.Value
            .Where(role => role.IsActiveManagedIn(space.Id))
            .Select(role => new SpaceRoleDto(
                role.PublicId,
                role.SystemKey!.Value,
                role.Name,
                role.IsManaged,
                role.Permissions))
            .ToArray();
        return Result.Ok(roles);
    }
}
