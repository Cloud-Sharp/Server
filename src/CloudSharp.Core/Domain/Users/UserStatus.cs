namespace CloudSharp.Core.Domain.Users;

/// <summary>
/// 계정 상태. <see cref="Deleted"/>는 terminal이며 Production v1에서는 복원하지 않는다.
/// ENUM 값의 추가는 허용하나, 이름 변경·삭제는 호환성 migration 없이 금지한다.
/// </summary>
public enum UserStatus
{
    Active = 0,
    Suspended = 1,
    Deleted = 2,
}