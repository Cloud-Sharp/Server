using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountSummaryDto(
    Guid MountPublicId,
    string Name,
    MountVisibility Visibility,
    MountStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MountAccessDto MyAccess);
