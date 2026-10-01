using Bogus;
using CloudSharp.Core.UseCases.Auth.Registrations;

namespace CloudSharp.Core.Tests.TestSupport.Builders;

/// <summary>
/// 등록 use case 테스트용 <see cref="RegistrationCommand"/> builder.
/// Bogus seed를 고정해 기본값을 항상 유효한 command로 유지하고,
/// 테스트에서는 깨뜨릴 값만 override한다.
/// </summary>
public static class TestRegistrationCommands
{
    public static RegistrationCommand Valid(
        string? email = null,
        string? userName = null,
        string? displayName = null,
        string? password = null,
        int seed = 1)
    {
        var faker = new Faker("ko") { Random = new Randomizer(seed) };
        return new RegistrationCommand(
            Email: email ?? faker.Internet.Email(),
            UserName: userName ?? faker.Random.String2(10),
            DisplayName: displayName ?? faker.Name.FullName(),
            Password: password ?? "example-password");
    }
}