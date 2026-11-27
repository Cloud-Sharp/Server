using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>대상에게 설정한 허용·거부 집합이다. 덮어쓰기를 제거한 뒤에는 둘 다 빈 집합이다.</summary>
public sealed record MountPermissionOverwriteDto(
    MountOverwriteTargetType TargetType,
    Guid TargetPublicId,
    IReadOnlySet<SpacePermission> Allow,
    IReadOnlySet<SpacePermission> Deny);
