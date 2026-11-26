namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountPermissionPageDto(
    IReadOnlyList<MountPermissionGrantDto> Items,
    string? NextCursor);
