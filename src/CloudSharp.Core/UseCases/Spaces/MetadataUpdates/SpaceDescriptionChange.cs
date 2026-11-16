namespace CloudSharp.Core.UseCases.Spaces.MetadataUpdates;

/// <summary>새 설명을 지정한다. 값이 null이면 기존 설명을 지운다.</summary>
public sealed record SpaceDescriptionChange(string? Value);
