using Bogus;
using CloudSharp.Core.UseCases.Auth.Logins;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

/// <summary>
/// 로그인 use case 테스트용 <see cref="LoginCommand"/> builder.
/// Bogus seed를 고정해 기본값을 항상 유효한 command로 유지하고,
/// 테스트에서는 깨뜨릴 값만 override한다.
/// </summary>
public static class TestLoginCommands
{
    public const string DefaultEmail = "user@example.com";

    public const string DefaultPassword = "example-password";

    public static LoginCommand Valid(
        string? loginId = null,
        string? password = null,
        int seed = 1)
    {
        var faker = new Faker("ko") { Random = new Randomizer(seed) };
        return new LoginCommand(
            LoginId: loginId ?? faker.Internet.Email(),
            Password: password ?? DefaultPassword);
    }
}