using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>Space 범위에서 Membership과 표시용 사용자·Role 정보를 조회한다.</summary>
public interface ISpaceMembershipReader
{
    /// <summary>현재 OWNER를 제외한 뒤 정렬과 cursor 페이지 제한을 적용한다.</summary>
    /// <remarks>Cursor는 저장 계층의 위치 토큰이며, 요청자·조회 조건 묶음과 서명은 API가 맡는다.</remarks>
    Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
        long spaceId,
        long ownerUserId,
        string? cursor,
        int limit,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default);

    /// <summary>해당 Space에 Membership이 없으면 null을 반환한다.</summary>
    Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
        long spaceId,
        Guid membershipPublicId,
        CancellationToken cancellationToken = default);
}
