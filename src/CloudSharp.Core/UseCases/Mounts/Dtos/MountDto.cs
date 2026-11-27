using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountDto(
    Guid SpacePublicId,
    Guid MountPublicId,
    string Name,
    MountVisibility Visibility,
    MountStatus Status,
    MountAccessDto MyAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);
