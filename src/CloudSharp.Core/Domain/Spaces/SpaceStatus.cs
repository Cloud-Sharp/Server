namespace CloudSharp.Core.Domain.Spaces;

/// <summary>
/// Space의 수명주기. Deleted는 복원할 수 없는 최종 상태다.
/// ENUM 값의 이름 변경·삭제는 호환성 migration 없이 허용하지 않는다.
/// </summary>
public enum SpaceStatus
{
    Active = 0,
    Archived = 1,
    Deleting = 2,
    Deleted = 3,
}
