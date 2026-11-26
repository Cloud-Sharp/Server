using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

/// <summary>Mount 개설의 초기 부여 대상을 쓰기 트랜잭션 안에서 조회한다.</summary>
public interface IMountCreationGrantTargetReader
{
    /// <summary>
    /// Role은 삭제 상태를 포함해 요청한 Space 소속 snapshot만 반환한다. User는 계정 상태와
    /// 지정한 Space의 선택적 Membership을 보존하며, 누락된 key는 해당 범위에서 찾을 수 없음을 뜻한다.
    /// 사용자 직접 부여는 같은 Space·User의 자격 변경과 직렬화한 뒤 최신 자격을 읽어야 한다.
    /// </summary>
    Task<Result<MountCreationGrantTargets>> ReadAsync(
        long spaceId,
        IReadOnlyCollection<Guid> rolePublicIds,
        IReadOnlyCollection<Guid> userPublicIds,
        CancellationToken cancellationToken = default);
}

public sealed record MountCreationGrantTargets(
    IReadOnlyDictionary<Guid, SpaceRole> Roles,
    IReadOnlyDictionary<Guid, MountCreationUserTarget> Users);

public sealed record MountCreationUserTarget(User User, SpaceMembership? Membership);
