using System.Security.Cryptography;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.TestSupport.Fakes;
using FluentAssertions;
using FluentResults;
using NUnit.Framework;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

/// <summary>
/// 실제 Redis 컨테이너에서 <see cref="SessionIssuanceService"/>의 발급·조회·폐기 전체 흐름을
/// 검증한다. FakeClock으로 발급 시각을 고정해 <see cref="UserSession"/> 만료 시각이
/// 도메인 규칙(24시간 idle / 7일 absolute)대로 계산·저장되는지 확인하고, 평문 토큰이나
/// 토큰 본문이 Redis에 어떤 값으로도 저장되지 않는지 확인한다.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class SessionIssuanceServiceRedisIntegrationTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);

    private RedisContainer? container;
    private IConnectionMultiplexer connection = null!;
    private HmacSha256TokenHasher hasher = null!;
    private RedisSessionStore store = null!;
    private SessionIssuanceService service = null!;
    private FakeClock clock = null!;
    private IDatabase redis = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        container = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await container.StartAsync();

        connection = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        redis = connection.GetDatabase();
        hasher = new HmacSha256TokenHasher(new TokenHashingOptions
        {
            ActiveKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        });
        clock = new FakeClock(InitialNow);
        store = new RedisSessionStore(connection);
        service = new SessionIssuanceService(
            new TokenIssuer(new RandomTokenGenerator(), hasher),
            store,
            clock);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (connection is not null)
        {
            await connection.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [SetUp]
    public void SetUp()
    {
        clock.SetUtcNow(InitialNow);
    }

    [Test]
    public async Task IssueAsync_WithActiveUser_ShouldStoreSessionFindableByTokenHash()
    {
        var user = NewUser(securityVersion: 7);

        var issued = await service.IssueAsync(user);

        issued.IsSuccess.Should().BeTrue(issued.ToString());
        issued.Value.AccessToken.Should().StartWith("cs_sess_");
        var found = await FindByPlainTokenAsync(issued.Value.AccessToken);
        found.Value.Should().NotBeNull();
        found.Value!.UserId.Should().Be(user.Id);
        found.Value.UserPublicId.Should().Be(user.PublicId);
        found.Value.SecurityVersion.Should().Be(7);
        found.Value.IssuedAt.Should().Be(InitialNow);
        found.Value.LastSeenAt.Should().Be(InitialNow);
        found.Value.IdleExpiresAt.Should().Be(InitialNow.AddHours(24));
        found.Value.AbsoluteExpiresAt.Should().Be(InitialNow.AddDays(7));
        issued.Value.IdleExpiresAt.Should().Be(found.Value.IdleExpiresAt);
        issued.Value.AbsoluteExpiresAt.Should().Be(found.Value.AbsoluteExpiresAt);
    }

    [Test]
    public async Task IssueAsync_WithRealRedis_ShouldNotStorePlainTokenOrTokenBody()
    {
        var user = NewUser();

        var issued = await service.IssueAsync(user);
        var tokenHash = HashOf(issued.Value.AccessToken);
        var tokenBody = issued.Value.AccessToken["cs_sess_".Length..];

        var bodyEntries = await redis.HashGetAllAsync(SessionRedisPayload.BodyKey(tokenHash));
        var indexMembers = await redis.SortedSetRangeByRankAsync(SessionRedisPayload.UserIndexKey(user.Id));
        var storedValues = bodyEntries
            .Select(entry => (string)entry.Value!)
            .Concat(indexMembers.Select(member => (string)member!))
            .ToArray();

        storedValues.Should().NotBeEmpty();
        foreach (var storedValue in storedValues)
        {
            storedValue.Should().NotContain(issued.Value.AccessToken);
            storedValue.Should().NotContain(tokenBody);
        }
    }

    [Test]
    public async Task IssueThenRemoveThenFind_ShouldReturnNullAndDropIndex()
    {
        var user = NewUser();
        var issued = await service.IssueAsync(user);
        var tokenHash = HashOf(issued.Value.AccessToken);

        var found = await store.FindByTokenHashAsync(tokenHash);
        found.Value.Should().NotBeNull();

        var removed = await store.RemoveAsync(found.Value!);

        removed.IsSuccess.Should().BeTrue(removed.ToString());
        var afterRemove = await store.FindByTokenHashAsync(tokenHash);
        afterRemove.IsSuccess.Should().BeTrue(afterRemove.ToString());
        afterRemove.Value.Should().BeNull();
        (await redis.KeyExistsAsync(SessionRedisPayload.UserIndexKey(user.Id))).Should().BeFalse();
    }

    [Test]
    public async Task IssueAsync_TwelveTimes_ShouldEvictOldestAndKeepTen()
    {
        var user = NewUser();
        var indexKey = SessionRedisPayload.UserIndexKey(user.Id);

        for (var i = 0; i < 12; i++)
        {
            var issued = await service.IssueAsync(user);
            issued.IsSuccess.Should().BeTrue(issued.ToString());
            clock.Advance(TimeSpan.FromMinutes(1));
        }

        var members = await redis.SortedSetRangeByRankAsync(indexKey);
        members.Length.Should().Be(10);
        var oldest = await store.FindByTokenHashAsync(TokenHash.Reconstitute((string)members[0]!));
        oldest.Value.Should().NotBeNull();
        oldest.Value!.IssuedAt.Should().Be(InitialNow.AddMinutes(2));
    }

    private User NewUser(long securityVersion = 1)
    {
        var now = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        return User.Reconstitute(
            id: RandomNumberGenerator.GetInt32(1, int.MaxValue),
            publicId: Guid.CreateVersion7(),
            email: EmailAddress.Create($"user-{Guid.CreateVersion7():N}@example.com").Value,
            userName: null,
            displayName: "Session User",
            passwordHash: "hashed-password-value",
            systemRole: SystemRole.User,
            status: UserStatus.Active,
            securityVersion: securityVersion,
            version: 1,
            createdAt: now,
            updatedAt: now,
            suspendedAt: null,
            deletedAt: null);
    }

    private async Task<Result<UserSession?>> FindByPlainTokenAsync(string accessToken) =>
        await store.FindByTokenHashAsync(HashOf(accessToken));

    private TokenHash HashOf(string accessToken)
    {
        var hash = TokenHash.Create(hasher.Hash(accessToken));
        if (hash.IsFailed)
        {
            throw new InvalidOperationException(
                "HMAC-SHA-256 token hash rejected by TokenHash: " + hash.Errors[0].Message);
        }

        return hash.Value;
    }
}