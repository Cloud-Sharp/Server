namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// Membership의 참여 수명주기. Left는 재활성화할 수 없는 최종 상태이며 재가입은 새 Membership으로 처리한다.
/// ENUM 값의 이름 변경·삭제는 호환성 migration 없이 허용하지 않는다.
/// </summary>
public enum SpaceMembershipStatus
{
    /// <summary>활성 참여. 부여된 Role의 권한으로 Space를 이용한다.</summary>
    Active = 0,

    /// <summary>일시 정지. 권한 평가에서 제외되지만 Role 교체는 허용한다.</summary>
    Suspended = 1,

    /// <summary>탈퇴·제거 완료. Role 할당 기록은 유지하되 어떤 변경도 허용하지 않는다.</summary>
    Left = 2,
}
