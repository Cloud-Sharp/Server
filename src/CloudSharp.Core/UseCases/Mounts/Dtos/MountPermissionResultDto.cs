namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>대상의 덮어쓰기 결과와 ETag로 변환할 Mount 버전이다.</summary>
public sealed record MountPermissionResultDto(MountPermissionOverwriteDto Overwrite, long Version);
