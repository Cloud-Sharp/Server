using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

/// <summary>
/// 비밀번호 변경 use case 테스트용 <see cref="PasswordChangeCommand"/> builder.
/// 식별 정보는 seed한 사용자·세션과 반드시 일치해야 하므로 무작위 값을 만들지 않고
/// 테스트에서 깨뜨릴 값만 override한다. 비밀번호 기본값은 harness가 hash로 seed한 값과 짝을 이룬다.
/// </summary>
public static class TestPasswordChangeCommands
{
    public const string DefaultCurrentPassword = "current-password";

    public const string DefaultNewPassword = "new-strong-password";

    public static PasswordChangeCommand For(
        User user,
        UserSession session,
        string? currentPassword = null,
        string? newPassword = null,
        long? expectedVersion = null)
        => new(
            user.Id,
            session.SessionId,
            session.TokenHash.Value,
            expectedVersion ?? user.Version,
            currentPassword ?? DefaultCurrentPassword,
            newPassword ?? DefaultNewPassword);
}