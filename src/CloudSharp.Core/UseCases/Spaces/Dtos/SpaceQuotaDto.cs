using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>Space 용량과 ETag로 변환할 Space 버전이다. 무제한이면 허용·가용 용량은 null이다.</summary>
public sealed record SpaceQuotaDto(
    Guid SpacePublicId,
    long? AllowedBytes,
    long UsedBytes,
    long ReservedBytes,
    long? AvailableBytes,
    long Version)
{
    internal static SpaceQuotaDto FromSpace(Space space) =>
        new(
            space.PublicId,
            space.AllowedBytes,
            space.UsedBytes,
            space.ReservedBytes,
            space.AvailableBytes,
            space.Version);
}
