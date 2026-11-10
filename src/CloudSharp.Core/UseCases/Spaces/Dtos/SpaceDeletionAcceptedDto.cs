using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.UseCases.Spaces.Dtos;

/// <summary>삭제 진행 상태로 전환된 Space와 요청 접수 시각을 담는다.</summary>
public sealed record SpaceDeletionAcceptedDto(
    Guid SpacePublicId,
    SpaceStatus Status,
    DateTimeOffset AcceptedAt,
    long Version);
