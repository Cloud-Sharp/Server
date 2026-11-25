using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>Space 생성 시점의 응답을 복원하기 위한 영속 스냅샷이다.</summary>
public sealed record SpaceCreationReplayDto(
    Guid SpacePublicId,
    Guid OwnerUserPublicId,
    string Name,
    string? Description,
    SpaceStatus Status,
    SpaceAuthorizationSource AccessSource,
    SpacePermission[] Permissions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version)
{
    public static SpaceCreationReplayDto FromResult(SpaceResultDto result)
    {
        var space = result.Space;
        return new SpaceCreationReplayDto(
            space.SpacePublicId,
            space.OwnerUserPublicId,
            space.Name,
            space.Description,
            space.Status,
            space.MyAccess.Source,
            space.MyAccess.Permissions.Order().ToArray(),
            space.CreatedAt,
            space.UpdatedAt,
            result.Version);
    }

    public SpaceResultDto ToResult() => new(
        new SpaceDto(
            SpacePublicId,
            OwnerUserPublicId,
            Name,
            Description,
            Status,
            new SpaceAccessDto(AccessSource, Permissions.ToHashSet()),
            CreatedAt,
            UpdatedAt),
        Version);
}
