namespace CloudSharp.Core.UseCases.Mounts.Dtos;

public sealed record MountPermissionPageDto(
    IReadOnlyList<MountPermissionOverwriteDto> Items,
    string? NextCursor);
