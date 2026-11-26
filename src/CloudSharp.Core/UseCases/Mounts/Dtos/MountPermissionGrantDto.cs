using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>대상에게 명시적으로 부여한 집합이다. 회수 후에는 빈 집합이다.</summary>
public sealed record MountPermissionGrantDto(
    MountGrantTargetType TargetType,
    Guid TargetPublicId,
    IReadOnlySet<MountPermission> Permissions);
