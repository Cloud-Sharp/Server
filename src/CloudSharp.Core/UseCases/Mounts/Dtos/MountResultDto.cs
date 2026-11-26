namespace CloudSharp.Core.UseCases.Mounts.Dtos;

/// <summary>Mount 상세 결과와 ETag로 변환할 버전이다.</summary>
public sealed record MountResultDto(MountDto Mount, long Version);
