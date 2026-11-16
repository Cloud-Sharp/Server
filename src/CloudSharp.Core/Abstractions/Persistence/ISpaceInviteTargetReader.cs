using CloudSharp.Core.Domain.Spaces;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>초대 수락에 필요한 Space 상태를 쓰기 트랜잭션 안에서 조회한다.</summary>
public interface ISpaceInviteTargetReader
{
    /// <summary>
    /// Space 행을 잠근 뒤 최신 상태와 소유자를 읽는다. 행이 없으면 성공한 null을 반환한다.
    /// 잠금은 동일한 트랜잭션이 끝날 때까지 Space 변경과 삭제를 막는다.
    /// </summary>
    Task<Result<SpaceInviteTargetSnapshot?>> ReadLockedAsync(
        long spaceId,
        CancellationToken cancellationToken = default);
}

public sealed record SpaceInviteTargetSnapshot(long Id, Guid PublicId, long OwnerUserId, SpaceStatus Status);
