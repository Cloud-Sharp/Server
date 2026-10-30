namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// 기본 Role의 안정된 시스템 식별자. 각 Space 생성 시 seed하는 ADMIN·MEMBER·VIEWER에만 부여한다.
/// 사용자 정의 Role은 SystemKey 없이 저장한다.
/// </summary>
/// <remarks>
/// <c>0</c>은 유효한 값이 아니므로 기본 인자값으로 사용할 수 없다. OWNER는 Role이 아니므로
/// 여기에 정의하지 않는다. ENUM 값 추가는 허용되지만 이름 변경·삭제는 금지한다.
/// </remarks>
public enum SpaceRoleSystemKey
{
    /// <summary>Space 전체 관리 Role. 정의된 모든 <see cref="SpacePermission"/>을 갖는다.</summary>
    Admin = 1,

    /// <summary>일반 참여자 Role.</summary>
    Member = 2,

    /// <summary>읽기 전용 Role.</summary>
    Viewer = 3,
}