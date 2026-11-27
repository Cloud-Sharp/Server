using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Api.Endpoints.Mounts.Responses;

internal sealed record MountResponse(
    Guid SpacePublicId,
    Guid MountPublicId,
    string Name,
    MountVisibility Visibility,
    MountStatus Status,
    MountAccessResponse MyAccess,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ArchivedAt);

internal sealed record MountSummaryResponse(
    Guid MountPublicId,
    string Name,
    MountVisibility Visibility,
    MountStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    MountAccessResponse MyAccess);

internal sealed record MountAccessResponse(IReadOnlyList<string> Permissions, bool CanManage);

internal sealed record MountPageResponse(
    IReadOnlyList<MountSummaryResponse> Items,
    string? NextCursor,
    bool CanCreateMount);
