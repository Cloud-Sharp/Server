namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountPageDto(
    IReadOnlyList<MountSummaryDto> Items,
    string? NextCursor,
    bool CanCreateMount);
