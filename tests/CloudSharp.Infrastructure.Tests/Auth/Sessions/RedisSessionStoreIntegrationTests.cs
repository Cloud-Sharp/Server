using System.Globalization;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Tests.TestSupport.Builders;
using FluentAssertions;
using FluentResults;
using NUnit.Framework;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

/// <summary>
/// 실제 Redis 컨테이너에서 <see cref="RedisSessionStore"/>의 저장·조회·삭제·비밀번호 변경
/// 후처리 계약을 검증한다. 본문 TTL·인덱스 유지·최대 10개 세션 제한·장애 분류를
/// 실제 payload와 Redis 서버 시각 기준으로 확인한다. 토큰 해시는 key·member로만 존재하고
/// 평문 토큰은 저장되지 않는다.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class RedisSessionStoreIntegrationTests
{
    private static long userIdSequence;

    private RedisContainer? container;
    private IConnectionMultiplexer connection = null!;
    private RedisSessionStore store = null!;
    private IDatabase redis = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        container = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await container.StartAsync();

        connection = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        store = new RedisSessionStore(connection);
        redis = connection.GetDatabase();
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

    [Test]
    public async Task StoreAsync_FindByTokenHashAsync_ShouldRoundTripBoundaryValues()
    {
        var now = DateTimeOffset.UtcNow;
        var session = UserSession.Reconstitute(
            Guid.CreateVersion7(), long.MaxValue, Guid.CreateVersion7(),
            NewHash(), long.MaxValue,
            now, now, now.AddHours(24), now.AddDays(7));

        var stored = await store.StoreAsync(session);

        stored.IsSuccess.Should().BeTrue(stored.ToString());
        var found = await store.FindByTokenHashAsync(session.TokenHash);
        found.IsSuccess.Should().BeTrue(found.ToString());
        found.Value.Should().NotBeNull();
        found.Value!.UserId.Should().Be(long.MaxValue);
        found.Value.SecurityVersion.Should().Be(long.MaxValue);
        found.Value.SessionId.Should().Be(session.SessionId);
        found.Value.UserPublicId.Should().Be(session.UserPublicId);
        found.Value.IssuedAt.Should().Be(session.IssuedAt);
        found.Value.LastSeenAt.Should().Be(session.LastSeenAt);
        found.Value.IdleExpiresAt.Should().Be(session.IdleExpiresAt);
        found.Value.AbsoluteExpiresAt.Should().Be(session.AbsoluteExpiresAt);
        found.Value.TokenHash.Should().Be(session.TokenHash);
    }

    [Test]
    public async Task FindByTokenHashAsync_WithStoredSession_ShouldReturnIndependentSnapshot()
    {
        var userId = NewUserId();
        var session = await StoreSessionAsync(userId);

        var first = await store.FindByTokenHashAsync(session.TokenHash);
        var second = await store.FindByTokenHashAsync(session.TokenHash);

        first.Value.Should().NotBeSameAs(second.Value);
        first.Value.Should().NotBeNull();
        second.Value.Should().NotBeNull();
        first.Value!.SessionId.Should().Be(second.Value!.SessionId);
    }

    [Test]
    public async Task FindByTokenHashAsync_WithMissingSession_ShouldReturnNull()
    {
        var found = await store.FindByTokenHashAsync(NewHash());

        found.IsSuccess.Should().BeTrue(found.ToString());
        found.Value.Should().BeNull();
    }

    [Test]
    public async Task StoreAsync_WithExistingBody_ShouldRejectWithoutOverwriting()
    {
        var userId = NewUserId();
        var session = await StoreSessionAsync(userId);
        var duplicate = UserSession.Issue(
            userId, session.UserPublicId, session.TokenHash,
            session.SecurityVersion, session.IssuedAt.AddMinutes(1));

        var act = () => store.StoreAsync(duplicate);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var found = await store.FindByTokenHashAsync(session.TokenHash);
        found.Value!.SessionId.Should().Be(session.SessionId);
    }

    [Test]
    public async Task FindByTokenHashAsync_WithWrongRedisType_ShouldThrow()
    {
        var hash = NewHash();
        await redis.StringSetAsync(BodyKeyOf(hash.Value), "not-a-hash");

        var act = () => store.FindByTokenHashAsync(hash);

        await act.Should().ThrowAsync<RedisServerException>();
    }

    [Test]
    public async Task FindByTokenHashAsync_WithCorruptPayload_ShouldThrow()
    {
        var userId = NewUserId();
        var hash = NewHash();
        var session = TestUserSessions.Valid(userId: userId, tokenHash: hash.Value);
        await redis.HashSetAsync(BodyKeyOf(hash.Value), BodyEntries(session).Take(3).ToArray());
        await redis.KeyExpireAsync(BodyKeyOf(hash.Value), TimeSpan.FromHours(1));

        var act = () => store.FindByTokenHashAsync(hash);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task RemoveAsync_ShouldDeleteBodyAndIndexMember()
    {
        var userId = NewUserId();
        var first = await StoreSessionAsync(userId);
        var second = await StoreSessionAsync(userId);

        var removed = await store.RemoveAsync(first);

        removed.IsSuccess.Should().BeTrue(removed.ToString());
        (await redis.KeyExistsAsync(BodyKeyOf(first.TokenHash.Value))).Should().BeFalse();
        (await IndexMembersAsync(userId)).Should().ContainSingle()
            .Which.Should().Be(second.TokenHash.Value);
    }

    [Test]
    public async Task RemoveAsync_WithAlreadyRemovedSession_ShouldSucceed()
    {
        var userId = NewUserId();
        var session = await StoreSessionAsync(userId);

        var firstRemove = await store.RemoveAsync(session);
        var secondRemove = await store.RemoveAsync(session);

        firstRemove.IsSuccess.Should().BeTrue(firstRemove.ToString());
        secondRemove.IsSuccess.Should().BeTrue(secondRemove.ToString());
    }

    [Test]
    public async Task RemoveAsync_WithLastSessionOfUser_ShouldDeleteIndexKey()
    {
        var userId = NewUserId();
        var session = await StoreSessionAsync(userId);

        await store.RemoveAsync(session);

        (await redis.KeyExistsAsync(IndexKey(userId))).Should().BeFalse();
    }

    [Test]
    public async Task StoreAsync_WithMoreThanTenSessions_ShouldEvictOldestFirst()
    {
        var userId = NewUserId();
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<UserSession>();
        for (var i = 0; i < 10; i++)
        {
            sessions.Add(await StoreSessionAsync(userId, now.AddMinutes(i)));
        }

        var newest = await StoreSessionAsync(userId, now.AddMinutes(10));

        (await IndexMembersAsync(userId)).Should().HaveCount(10);
        (await redis.KeyExistsAsync(BodyKeyOf(sessions[0].TokenHash.Value))).Should().BeFalse();
        for (var i = 1; i < 10; i++)
        {
            (await redis.KeyExistsAsync(BodyKeyOf(sessions[i].TokenHash.Value))).Should().BeTrue();
        }
        (await redis.KeyExistsAsync(BodyKeyOf(newest.TokenHash.Value))).Should().BeTrue();
    }

    [Test]
    public async Task StoreAsync_WithIssuedAtTies_ShouldEvictByTokenHashOrder()
    {
        var userId = NewUserId();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 10; i++)
        {
            await StoreSessionAsync(userId, now, $"sh_{i:D18}");
        }

        var newestHash = "sh_1" + new string('0', 17);
        var newest = await StoreSessionAsync(userId, now, newestHash);

        (await IndexMembersAsync(userId)).Should().HaveCount(10);
        (await redis.KeyExistsAsync(BodyKeyOf("sh_" + 0.ToString("D18")))).Should().BeFalse();
        (await redis.KeyExistsAsync(BodyKeyOf(newest.TokenHash.Value))).Should().BeTrue();
    }

    [Test]
    public async Task StoreAsync_WithConcurrentIssuance_ShouldKeepAtMostTenSessions()
    {
        var userId = NewUserId();
        var start = DateTimeOffset.UtcNow;
        var sessions = Enumerable.Range(0, 12)
            .Select(i => TestUserSessions.Valid(
                userId: userId, tokenHash: NewTokenHash(), now: start.AddMinutes(i)))
            .ToArray();

        var results = await Task.WhenAll(sessions.Select(session => store.StoreAsync(session)));

        results.Should().OnlyContain(result => result.IsSuccess);
        (await IndexMembersAsync(userId)).Should().HaveCount(10);
        foreach (var evicted in sessions.Take(2))
        {
            (await redis.KeyExistsAsync(BodyKeyOf(evicted.TokenHash.Value))).Should().BeFalse();
        }
        foreach (var kept in sessions.Skip(2))
        {
            (await redis.KeyExistsAsync(BodyKeyOf(kept.TokenHash.Value))).Should().BeTrue();
        }
    }

    [TestCase(1L, 2L)]
    [TestCase(1_000_000L, 2_000_000L)]
    public async Task StoreAsync_ShouldEvictOldestIssuedAt(long oldestTicks, long newestTicks)
    {
        var userId = NewUserId();
        var serverTime = await connection.GetServer(connection.GetEndPoints()[0]).TimeAsync();
        var start = new DateTimeOffset(
            serverTime.Ticks - serverTime.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        var oldest = await StoreSessionAsync(userId, start.AddTicks(oldestTicks), $"sh_z_{userId}");
        for (var i = 0; i < 9; i++)
        {
            await StoreSessionAsync(userId, start.AddTicks(newestTicks), $"sh_m_{userId}_{i}");
        }

        var newest = await StoreSessionAsync(userId, start.AddTicks(newestTicks), $"sh_a_{userId}");

        Assert.That(await IndexMembersAsync(userId), Has.Length.EqualTo(10));
        Assert.That(await redis.KeyExistsAsync(BodyKeyOf(oldest.TokenHash.Value)), Is.False);
        Assert.That(await redis.KeyExistsAsync(BodyKeyOf(newest.TokenHash.Value)), Is.True);
        Assert.That(await redis.SortedSetScoreAsync(IndexKey(userId), newest.TokenHash.Value),
            Is.EqualTo((double)newest.IssuedAt.ToUnixTimeSeconds()));
    }

    [Test]
    public async Task StoreAsync_ShouldKeepIndexUntilLatestRemainingExpiry()
    {
        var userId = NewUserId();
        var serverTime = await connection.GetServer(connection.GetEndPoints()[0]).TimeAsync();
        var now = new DateTimeOffset(DateTime.SpecifyKind(serverTime, DateTimeKind.Utc));
        var later = UserSession.Reconstitute(
            Guid.CreateVersion7(), userId, Guid.CreateVersion7(), NewHash(), 1,
            now, now, now.AddHours(1), now.AddHours(2));
        var earlier = UserSession.Reconstitute(
            Guid.CreateVersion7(), userId, later.UserPublicId, NewHash(), 1,
            now.AddMinutes(-1), now.AddMinutes(-1), now.AddMinutes(10), now.AddMinutes(20));
        Assert.That((await store.StoreAsync(later)).IsSuccess, Is.True);

        var stored = await store.StoreAsync(earlier);

        Assert.That(stored.IsSuccess, Is.True);
        var bodyTtl = await redis.KeyTimeToLiveAsync(BodyKeyOf(later.TokenHash.Value));
        var indexTtl = await redis.KeyTimeToLiveAsync(IndexKey(userId));
        Assert.That(bodyTtl, Is.Not.Null);
        Assert.That(indexTtl, Is.Not.Null);
        Assert.That(indexTtl!.Value, Is.GreaterThan(TimeSpan.FromHours(1)));
        Assert.That(indexTtl.Value, Is.EqualTo(bodyTtl!.Value).Within(TimeSpan.FromSeconds(1)));
        Assert.That(await IndexMembersAsync(userId), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task StoreAsync_WithGhostIndexMember_ShouldCleanItUp()
    {
        var userId = NewUserId();
        var ghostHash = NewTokenHash();
        await redis.SortedSetAddAsync(
            IndexKey(userId), ghostHash, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        await StoreSessionAsync(userId);

        var members = await IndexMembersAsync(userId);
        members.Should().HaveCount(1);
        members.Should().NotContain(ghostHash);
    }

    [Test]
    public async Task StoreAsync_WithIdleExpiredSession_ShouldCleanItUp()
    {
        var userId = NewUserId();
        var expired = IdleExpiredSession(userId);
        await SeedBodyAsync(userId, expired);
        var live = await StoreSessionAsync(userId);

        var members = await IndexMembersAsync(userId);

        members.Should().Contain(live.TokenHash.Value);
        members.Should().NotContain(expired.TokenHash.Value);
        (await redis.KeyExistsAsync(BodyKeyOf(expired.TokenHash.Value))).Should().BeFalse();
    }

    [Test]
    public async Task StoreAsync_ShouldKeepBodyTtlWithinAbsoluteExpiry()
    {
        var userId = NewUserId();
        var issuedAt = DateTimeOffset.UtcNow;
        var session = await StoreSessionAsync(userId, issuedAt);

        var ttl = await redis.KeyTimeToLiveAsync(BodyKeyOf(session.TokenHash.Value));

        ttl.Should().NotBeNull();
        var expected = session.AbsoluteExpiresAt - issuedAt;
        ttl.Should().BeLessOrEqualTo(expected + TimeSpan.FromSeconds(2));
        ttl.Should().BeGreaterThan(expected - TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task RemoveAsync_ShouldShortenIndexTtlToRemainingSessions()
    {
        var userId = NewUserId();
        var start = DateTimeOffset.UtcNow;
        var older = await StoreSessionAsync(userId, start);
        var newer = await StoreSessionAsync(userId, start.AddHours(1));

        var ttlAfterStore = await redis.KeyTimeToLiveAsync(IndexKey(userId));
        await store.RemoveAsync(newer);
        var ttlAfterRemove = await redis.KeyTimeToLiveAsync(IndexKey(userId));

        ttlAfterStore.Should().NotBeNull();
        ttlAfterRemove.Should().NotBeNull();
        var boundForOlder = older.AbsoluteExpiresAt - start;
        ttlAfterRemove!.Value.Should().BeLessOrEqualTo(boundForOlder + TimeSpan.FromSeconds(2));
        ttlAfterStore!.Value.Should().BeGreaterThan(ttlAfterRemove.Value);
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithMatchingCurrentSession_ShouldSwapVersionAndRemoveOthers()
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId);
        var other1 = await StoreSessionAsync(userId);
        var other2 = await StoreSessionAsync(userId);
        var ttlBefore = await redis.KeyTimeToLiveAsync(BodyKeyOf(current.TokenHash.Value));
        var newVersion = current.SecurityVersion + 1;

        var finalized = await store.FinalizePasswordChangeAsync(current, newVersion, DateTimeOffset.UtcNow);

        finalized.IsSuccess.Should().BeTrue(finalized.ToString());
        finalized.Value.Should().BeTrue();
        var found = await store.FindByTokenHashAsync(current.TokenHash);
        found.Value!.SecurityVersion.Should().Be(newVersion);
        found.Value.SessionId.Should().Be(current.SessionId);
        found.Value.IssuedAt.Should().Be(current.IssuedAt);
        found.Value.LastSeenAt.Should().Be(current.LastSeenAt);
        found.Value.IdleExpiresAt.Should().Be(current.IdleExpiresAt);
        found.Value.AbsoluteExpiresAt.Should().Be(current.AbsoluteExpiresAt);
        var ttlAfter = await redis.KeyTimeToLiveAsync(BodyKeyOf(current.TokenHash.Value));
        ttlAfter.Should().NotBeNull();
        ttlAfter.Should().BeCloseTo(ttlBefore!.Value, TimeSpan.FromSeconds(5));

        (await redis.KeyExistsAsync(BodyKeyOf(other1.TokenHash.Value))).Should().BeFalse();
        (await redis.KeyExistsAsync(BodyKeyOf(other2.TokenHash.Value))).Should().BeFalse();
        (await IndexMembersAsync(userId)).Should().ContainSingle()
            .Which.Should().Be(current.TokenHash.Value);
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithDeletedCurrentSession_ShouldRemoveOthersWithoutResurrecting()
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId);
        var other = await StoreSessionAsync(userId);
        await store.RemoveAsync(current);

        var finalized = await store.FinalizePasswordChangeAsync(
            current, current.SecurityVersion + 1, DateTimeOffset.UtcNow);

        finalized.IsSuccess.Should().BeTrue(finalized.ToString());
        finalized.Value.Should().BeFalse();
        (await store.FindByTokenHashAsync(current.TokenHash)).Value.Should().BeNull();
        (await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value))).Should().BeFalse();
        (await IndexMembersAsync(userId)).Should().BeEmpty();
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithExpiredCurrentSession_ShouldKeepCurrentBodyUnchanged()
    {
        var userId = NewUserId();
        var other = await StoreSessionAsync(userId);
        var expired = IdleExpiredSession(userId);
        await SeedBodyAsync(userId, expired);

        var finalized = await store.FinalizePasswordChangeAsync(
            expired, expired.SecurityVersion + 1, DateTimeOffset.UtcNow);

        finalized.IsSuccess.Should().BeTrue(finalized.ToString());
        finalized.Value.Should().BeFalse();
        (await redis.HashGetAsync(
            BodyKeyOf(expired.TokenHash.Value),
            SessionRedisPayload.SecurityVersionField))
            .Should().Be(expired.SecurityVersion.ToString(CultureInfo.InvariantCulture));
        (await redis.KeyExistsAsync(BodyKeyOf(expired.TokenHash.Value))).Should().BeTrue();
        (await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value))).Should().BeFalse();
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithChangedSecurityVersion_ShouldNotTouchCurrentSession()
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId, securityVersion: 5);
        var other = await StoreSessionAsync(userId, securityVersion: 5);
        var bumped = 14L;
        await redis.HashSetAsync(
            BodyKeyOf(current.TokenHash.Value), SessionRedisPayload.SecurityVersionField,
            bumped.ToString(CultureInfo.InvariantCulture));

        var finalized = await store.FinalizePasswordChangeAsync(
            current, bumped + 1, DateTimeOffset.UtcNow);

        finalized.Value.Should().BeFalse();
        (await redis.HashGetAsync(
            BodyKeyOf(current.TokenHash.Value),
            SessionRedisPayload.SecurityVersionField))
            .Should().Be(bumped.ToString(CultureInfo.InvariantCulture));
        (await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value))).Should().BeFalse();
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithSecurityVersionRegression_ShouldNotDowngradeCurrentSession()
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId, securityVersion: 5);
        var other = await StoreSessionAsync(userId, securityVersion: 5);

        var finalized = await store.FinalizePasswordChangeAsync(
            current, current.SecurityVersion - 1, DateTimeOffset.UtcNow);

        finalized.Value.Should().BeFalse();
        (await redis.HashGetAsync(
            BodyKeyOf(current.TokenHash.Value),
            SessionRedisPayload.SecurityVersionField))
            .Should().Be("5");
        (await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value))).Should().BeFalse();
    }

    [TestCase(9L, 10L)]
    [TestCase(9_007_199_254_740_992L, 9_007_199_254_740_993L)]
    [TestCase(9_007_199_254_740_993L, 9_007_199_254_740_993L)]
    [TestCase(long.MaxValue - 1, long.MaxValue)]
    [TestCase(long.MaxValue, long.MaxValue)]
    public async Task FinalizePasswordChangeAsync_ShouldPreserveExactSecurityVersion(
        long currentVersion, long newVersion)
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId, securityVersion: currentVersion);
        var other = await StoreSessionAsync(userId, securityVersion: currentVersion);
        var bodyKey = BodyKeyOf(current.TokenHash.Value);
        var before = await redis.HashGetAllAsync(bodyKey);
        var ttlBefore = await redis.KeyTimeToLiveAsync(bodyKey);

        var finalized = await store.FinalizePasswordChangeAsync(current, newVersion, current.IssuedAt);

        Assert.That(finalized.IsSuccess, Is.True);
        Assert.That(finalized.Value, Is.True);
        var expected = before.Select(entry => entry.Name == SessionRedisPayload.SecurityVersionField
            ? new HashEntry(entry.Name, newVersion.ToString(CultureInfo.InvariantCulture))
            : entry);
        Assert.That(await redis.HashGetAllAsync(bodyKey), Is.EquivalentTo(expected));
        var ttlAfter = await redis.KeyTimeToLiveAsync(bodyKey);
        Assert.That(ttlBefore, Is.Not.Null);
        Assert.That(ttlAfter, Is.Not.Null);
        Assert.That(ttlAfter!.Value, Is.InRange(ttlBefore!.Value - TimeSpan.FromSeconds(5), ttlBefore.Value));
        Assert.That(await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value)), Is.False);
    }

    [TestCase(100L, 99L)]
    [TestCase(9_007_199_254_740_993L, 9_007_199_254_740_992L)]
    [TestCase(long.MaxValue, long.MaxValue - 1)]
    public async Task FinalizePasswordChangeAsync_ShouldRejectSecurityVersionRegression(
        long currentVersion, long newVersion)
    {
        var userId = NewUserId();
        var current = await StoreSessionAsync(userId, securityVersion: currentVersion);
        var other = await StoreSessionAsync(userId, securityVersion: currentVersion);
        var bodyKey = BodyKeyOf(current.TokenHash.Value);
        var before = await redis.HashGetAllAsync(bodyKey);
        var ttlBefore = await redis.KeyTimeToLiveAsync(bodyKey);

        var finalized = await store.FinalizePasswordChangeAsync(current, newVersion, current.IssuedAt);

        Assert.That(finalized.IsSuccess, Is.True);
        Assert.That(finalized.Value, Is.False);
        Assert.That(await redis.HashGetAllAsync(bodyKey), Is.EquivalentTo(before));
        var ttlAfter = await redis.KeyTimeToLiveAsync(bodyKey);
        Assert.That(ttlBefore, Is.Not.Null);
        Assert.That(ttlAfter, Is.Not.Null);
        Assert.That(ttlAfter!.Value, Is.InRange(ttlBefore!.Value - TimeSpan.FromSeconds(5), ttlBefore.Value));
        Assert.That(await redis.KeyExistsAsync(BodyKeyOf(other.TokenHash.Value)), Is.False);
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithOtherUsersSessions_ShouldNotTouchOtherUsers()
    {
        var userId = NewUserId();
        var otherUserId = NewUserId();
        var current = await StoreSessionAsync(userId);
        var otherUserSession = await StoreSessionAsync(otherUserId);

        var finalized = await store.FinalizePasswordChangeAsync(
            current, current.SecurityVersion + 1, DateTimeOffset.UtcNow);

        finalized.Value.Should().BeTrue();
        var otherFound = await store.FindByTokenHashAsync(otherUserSession.TokenHash);
        otherFound.Value!.SecurityVersion.Should().Be(otherUserSession.SecurityVersion);
        (await IndexMembersAsync(otherUserId)).Should().ContainSingle()
            .Which.Should().Be(otherUserSession.TokenHash.Value);
    }

    [Test]
    public async Task StoreAsync_WithUnreachableRedis_ShouldReturnDependencyUnavailable()
    {
        var unreachable = await CreateUnreachableStoreAsync();

        var result = await unreachable.StoreAsync(TestUserSessions.Valid());

        AssertIsDependencyUnavailable(result);
    }

    [Test]
    public async Task FindByTokenHashAsync_WithUnreachableRedis_ShouldReturnDependencyUnavailable()
    {
        var unreachable = await CreateUnreachableStoreAsync();

        var result = await unreachable.FindByTokenHashAsync(NewHash());

        AssertIsDependencyUnavailable(result);
    }

    [Test]
    public async Task RemoveAsync_WithUnreachableRedis_ShouldReturnDependencyUnavailable()
    {
        var unreachable = await CreateUnreachableStoreAsync();

        var result = await unreachable.RemoveAsync(TestUserSessions.Valid());

        AssertIsDependencyUnavailable(result);
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithUnreachableRedis_ShouldReturnDependencyUnavailable()
    {
        var unreachable = await CreateUnreachableStoreAsync();
        var session = TestUserSessions.Valid();

        var result = await unreachable.FinalizePasswordChangeAsync(
            session, session.SecurityVersion + 1, DateTimeOffset.UtcNow);

        AssertIsDependencyUnavailable(result);
    }

    [Test]
    public async Task StoreAsync_WithCancelledToken_ShouldThrowBeforeSending()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => store.StoreAsync(TestUserSessions.Valid(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task FindByTokenHashAsync_WithCancelledToken_ShouldThrowBeforeSending()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => store.FindByTokenHashAsync(NewHash(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task RemoveAsync_WithCancelledToken_ShouldThrowBeforeSending()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var act = () => store.RemoveAsync(TestUserSessions.Valid(), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task FinalizePasswordChangeAsync_WithCancelledToken_ShouldThrowBeforeSending()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var session = TestUserSessions.Valid();

        var act = () => store.FinalizePasswordChangeAsync(
            session, session.SecurityVersion + 1, DateTimeOffset.UtcNow, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static long NewUserId() => Interlocked.Increment(ref userIdSequence);

    private static string NewTokenHash() => "sh_" + Guid.CreateVersion7().ToString("N");

    private static TokenHash NewHash()
    {
        var hash = TokenHash.Create(NewTokenHash());
        hash.IsSuccess.Should().BeTrue("test token hash must be valid.");
        return hash.Value;
    }

    private static string BodyKeyOf(string tokenHash) => SessionRedisPayload.SessionKeyPrefix + tokenHash;

    private string IndexKey(long userId) => SessionRedisPayload.UserIndexKey(userId).ToString();

    private static UserSession IdleExpiredSession(long userId)
    {
        var now = DateTimeOffset.UtcNow;
        return UserSession.Reconstitute(
            Guid.CreateVersion7(), userId, Guid.CreateVersion7(), NewHash(), 1,
            now.AddDays(-2), now.AddDays(-2), now.AddHours(-25), now.AddDays(5));
    }

    private async Task<UserSession> StoreSessionAsync(
        long userId,
        DateTimeOffset? now = null,
        string? tokenHash = null,
        long? securityVersion = null)
    {
        var session = TestUserSessions.Valid(
            userId: userId,
            tokenHash: tokenHash ?? NewTokenHash(),
            securityVersion: securityVersion,
            now: now ?? DateTimeOffset.UtcNow);
        var stored = await store.StoreAsync(session);
        stored.IsSuccess.Should().BeTrue(stored.ToString());
        return session;
    }

    private async Task<string[]> IndexMembersAsync(long userId) =>
        (await redis.SortedSetRangeByRankAsync(IndexKey(userId)))
            .Select(member => (string)member!)
            .ToArray();

    private async Task SeedBodyAsync(long userId, UserSession session)
    {
        var bodyKey = BodyKeyOf(session.TokenHash.Value);
        await redis.HashSetAsync(bodyKey, BodyEntries(session));
        await redis.KeyExpireAsync(bodyKey, TimeSpan.FromHours(1));
        await redis.SortedSetAddAsync(
            IndexKey(userId), session.TokenHash.Value, session.IssuedAt.ToUnixTimeSeconds());
    }

    private static HashEntry[] BodyEntries(UserSession session)
    {
        var arguments = SessionRedisPayload.ToStoreArguments(session);
        string[] fieldNames =
        [
            SessionRedisPayload.SessionIdField,
            SessionRedisPayload.UserIdField,
            SessionRedisPayload.UserPublicIdField,
            SessionRedisPayload.SecurityVersionField,
            SessionRedisPayload.IssuedAtField,
            SessionRedisPayload.LastSeenAtField,
            SessionRedisPayload.IdleExpiresAtField,
            SessionRedisPayload.AbsoluteExpiresAtField,
        ];
        return fieldNames
            .Select((field, index) => new HashEntry(field, arguments[index]))
            .ToArray();
    }

    private static async Task<RedisSessionStore> CreateUnreachableStoreAsync()
    {
        var options = ConfigurationOptions.Parse("localhost:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 100;
        var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        return new RedisSessionStore(multiplexer);
    }

    private static void AssertIsDependencyUnavailable(IResultBase result)
    {
        result.IsFailed.Should().BeTrue("an unreachable Redis must fail closed.");
        var hasDependencyUnavailable = result.Errors.Any(error =>
            error.Metadata is not null
            && error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && string.Equals(
                code as string,
                ErrorCodes.Common.DependencyUnavailable,
                StringComparison.Ordinal));
        hasDependencyUnavailable.Should().BeTrue(
            "an unreachable Redis must fail closed with DEPENDENCY_UNAVAILABLE.");
    }
}
