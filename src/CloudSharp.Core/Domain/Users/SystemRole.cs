namespace CloudSharp.Core.Domain.Users;

/// <summary>
/// 시스템 전역 역할. Production v1은 <see cref="User"/>와 <see cref="SystemAdmin"/> 두 값만 지원한다.
/// 사용자 정의 역할은 지원하지 않는다. ENUM 값 추가는 허용하나, 이름 변경·삭제는 금지한다.
/// </summary>
public enum SystemRole
{
    User = 0,
    SystemAdmin = 1,
}