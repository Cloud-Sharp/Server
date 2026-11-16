namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>Space 목록의 한 페이지다. 다음 페이지가 없으면 NextCursor는 null이다.</summary>
public sealed record SpacePageDto(IReadOnlyList<SpaceDto> Items, string? NextCursor);
