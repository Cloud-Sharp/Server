namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>대상의 부여 결과와 ETag로 변환할 Mount 버전이다.</summary>
public sealed record MountPermissionResultDto(MountPermissionGrantDto Grant, long Version);
