using Bogus;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;

namespace CloudSharp.Infrastructure.Tests.TestSupport.Builders;

/// <summary>
/// <see cref="UserSession"/> 테스트 builder. Bogus seed를 고정해 기본값을 항상
/// 유효한 세션으로 유지하고 테스트에서는 깨뜨릴 값만 override한다.
/// token hash는 평문 token prefix(<c>cs_</c>)로 시작하지 않는 16진수 문자열을 생성한다.
/// </summary>
public static class TestUserSessions
{
    public static UserSession Valid(
        long? userId = null,
        Guid? userPublicId = null,
        string? tokenHash = null,
        long? securityVersion = null,
        DateTimeOffset? now = null,
        int seed = 1)
    {
        var faker = new Faker("ko") { Random = new Randomizer(seed) };
        var hash = TokenHash.Create(
            tokenHash ?? "sh_" + faker.Random.String2(40, "0123456789abcdef"));
        if (hash.IsFailed)
        {
            throw new InvalidOperationException("Test token hash is invalid.");
        }

        return UserSession.Issue(
            userId ?? faker.Random.Long(1, 100_000),
            userPublicId ?? faker.Random.Guid(),
            hash.Value,
            securityVersion ?? faker.Random.Long(1, 10_000),
            now ?? new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero));
    }
}