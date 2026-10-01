using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Users.Profiles;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

/// <summary>
/// 내 프로필 조회 use case 테스트용 <see cref="GetProfileQuery"/> builder.
/// UserId는 seed한 사용자와 반드시 일치해야 하므로 무작위 값을 만들지 않고
/// 테스트에서 깨뜨릴 값만 override한다.
/// </summary>
public static class TestGetProfileQueries
{
    public static GetProfileQuery For(User user, long? userId = null) =>
        new(userId ?? user.Id);
}