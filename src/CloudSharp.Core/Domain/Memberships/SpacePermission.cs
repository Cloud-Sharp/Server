namespace CloudSharp.Core.Domain.Memberships;

/// <summary>
/// Space 단위의 개별 권한. 각 값은 서로 독립된 권한이며 권한 간 자동 포함 관계는 없다.
/// 기본 Role 매핑, OWNER 권한 합성, 권한 평가는 별도의 후속 작업으로 다룬다.
/// </summary>
/// <remarks>
/// <c>0</c>은 유효한 권한이 아니다. 외부 API와 영속 저장의 식별자는
/// <see cref="SpacePermissionCodes.Get"/>이 반환하는 코드 문자열이며,
/// enum 정수값이나 <c>ToString()</c> 결과에는 의존하지 않는다.
/// ENUM 값 추가는 허용되지만 이름 변경·삭제는 금지한다.
/// </remarks>
public enum SpacePermission
{
    /// <summary>Space 정보 조회.</summary>
    SpaceView = 1,

    /// <summary>Space 정보 수정. quota 변경, Space 삭제, 소유권 이전은 포함하지 않는다.</summary>
    SpaceUpdate = 2,

    /// <summary>파일·폴더 목록과 메타데이터 조회.</summary>
    FilesRead = 3,

    /// <summary>파일·폴더 생성, 수정, 삭제.</summary>
    FilesWrite = 4,

    /// <summary>파일 원문 다운로드.</summary>
    FilesDownload = 5,

    /// <summary>공유 링크 목록과 상세 조회.</summary>
    SharesView = 6,

    /// <summary>공유 링크 생성, 수정, 삭제.</summary>
    SharesManage = 7,

    /// <summary>태그 생성, 수정, 삭제와 파일 태그 지정.</summary>
    TagsManage = 8,

    /// <summary>Space 초대 생성과 초대 관리.</summary>
    InvitesManage = 9,

    /// <summary>멤버 목록과 멤버 정보 조회.</summary>
    MembersView = 10,

    /// <summary>멤버 역할 변경, 정지, 제거.</summary>
    MembersManage = 11,
}