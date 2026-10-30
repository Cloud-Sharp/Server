namespace CloudSharp.Core.Domain.Invites;

/// <summary>
/// 재사용 초대 링크의 수명주기. REVOKED·EXPIRED는 terminal이며 재활성화하지 않는다.
/// ENUM 값의 이름 변경·삭제는 호환성 migration 없이 허용하지 않는다.
/// </summary>
public enum SpaceInviteStatus
{
    /// <summary>만료·폐기 전 상태. 만료 시각 전과 사용 한도 내에서 수락을 허용한다.</summary>
    Active = 0,

    /// <summary>폐기 완료. 이후 어떤 수락·변경도 허용하지 않는다.</summary>
    Revoked = 1,

    /// <summary>만료 시각 도달 또는 사용 한도 소진 완료. 이후 어떤 수락·변경도 허용하지 않는다.</summary>
    Expired = 2,
}