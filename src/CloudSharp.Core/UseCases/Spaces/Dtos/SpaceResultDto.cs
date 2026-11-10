namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>단건 Space 결과와 ETag로 변환할 버전이다.</summary>
public sealed record SpaceResultDto(SpaceDto Space, long Version);
