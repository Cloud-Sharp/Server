using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Tests.TestSupport.Builders;
using FluentAssertions;
using NUnit.Framework;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

/// <summary>
/// <see cref="SessionRedisPayload"/>의 저장값 매핑 검증. long 경계값과 UTC ticks 정밀도가
/// 문자열 직렬화로 무손실 보존되는지, 손상된 payload가 값을 노출하지 않는 예외로
/// 전파되는지를 검증한다.
/// </summary>
[TestFixture]
public class SessionRedisPayloadTests
{
    private static readonly string[] BodyFieldNames =
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

    [Test]
    public void ToDomain_WithStoredEntries_ShouldReconstituteAllFields()
    {
        var session = TestUserSessions.Valid();

        var reconstituted = SessionRedisPayload.ToDomain(ToStoredEntries(session), session.TokenHash);

        AssertSessionMatches(reconstituted, session);
        reconstituted.Should().NotBeSameAs(session);
    }

    [Test]
    public void ToDomain_WithBoundaryLongsAndTickPrecision_ShouldReconstituteExactly()
    {
        var now = new DateTimeOffset(2026, 10, 19, 9, 8, 7, TimeSpan.Zero).AddTicks(654_321);
        var session = TestUserSessions.Valid(
            userId: long.MaxValue,
            securityVersion: long.MaxValue,
            now: now);

        var reconstituted = SessionRedisPayload.ToDomain(ToStoredEntries(session), session.TokenHash);

        AssertSessionMatches(reconstituted, session);
        reconstituted.IssuedAt.Ticks.Should().Be(session.IssuedAt.Ticks);
    }

    [Test]
    public void ToStoreArguments_WithUtcTimes_ShouldFormatFixedWidthTickStrings()
    {
        var session = TestUserSessions.Valid();

        var arguments = SessionRedisPayload.ToStoreArguments(session);
        var timeValues = arguments.Skip(4).Take(4).Select(value => (string)value!).ToArray();

        timeValues.Should().OnlyContain(value => value.Length == 19);
        timeValues.Should().OnlyContain(value => value.All(c => c >= '0' && c <= '9'));
    }

    [Test]
    public void ToStoreArguments_WithAnySession_ShouldNotContainRawTokenValues()
    {
        var session = TestUserSessions.Valid();

        var arguments = SessionRedisPayload.ToStoreArguments(session)
            .Concat(SessionRedisPayload.ToRemoveArguments(session))
            .Concat(SessionRedisPayload.ToFinalizeArguments(session, session.SecurityVersion + 1, session.IssuedAt))
            .Select(value => (string)value!)
            .ToArray();

        arguments.Should().NotContain(value => value.StartsWith("cs_", StringComparison.Ordinal));
    }

    [Test]
    public void BodyKey_WithTokenHash_ShouldUseSessionKeyPrefix()
    {
        var session = TestUserSessions.Valid();

        var bodyKey = SessionRedisPayload.BodyKey(session.TokenHash);

        bodyKey.ToString().Should().Be("auth:session:" + session.TokenHash.Value);
    }

    [Test]
    public void UserIndexKey_WithBoundaryUserId_ShouldUseUserSessionsPrefixAndInvariantDigits()
    {
        var indexKey = SessionRedisPayload.UserIndexKey(long.MaxValue);

        indexKey.ToString().Should().Be("auth:user_sessions:9223372036854775807");
    }

    [TestCaseSource(nameof(MissingBodyFields))]
    public void ToDomain_WithMissingField_ShouldThrow(string missingField)
    {
        var session = TestUserSessions.Valid();
        var entries = ToStoredEntries(session)
            .Where(entry => entry.Name != missingField)
            .ToArray();

        var act = () => SessionRedisPayload.ToDomain(entries, session.TokenHash);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{missingField}*");
    }

    [TestCaseSource(nameof(BodyFieldsAsCases))]
    public void ToDomain_WithCorruptFieldValue_ShouldThrow(string corruptField)
    {
        var session = TestUserSessions.Valid();
        var entries = ToStoredEntries(session)
            .Select(entry => entry.Name == corruptField
                ? new HashEntry(entry.Name, "corrupt-value")
                : entry)
            .ToArray();

        var act = () => SessionRedisPayload.ToDomain(entries, session.TokenHash);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{corruptField}*");
    }

    private static HashEntry[] ToStoredEntries(UserSession session) =>
        SessionRedisPayload.ToStoreArguments(session)
            .Take(BodyFieldNames.Length)
            .Select((value, index) => new HashEntry(BodyFieldNames[index], value))
            .ToArray();

    private static IEnumerable<TestCaseData> MissingBodyFields() =>
        BodyFieldNames.Select(field => new TestCaseData(field).SetName($"Missing {field}"));

    private static IEnumerable<TestCaseData> BodyFieldsAsCases() =>
        BodyFieldNames.Select(field => new TestCaseData(field).SetName($"Corrupt {field}"));

    private static void AssertSessionMatches(UserSession actual, UserSession expected)
    {
        actual.SessionId.Should().Be(expected.SessionId);
        actual.UserId.Should().Be(expected.UserId);
        actual.UserPublicId.Should().Be(expected.UserPublicId);
        actual.TokenHash.Should().Be(expected.TokenHash);
        actual.SecurityVersion.Should().Be(expected.SecurityVersion);
        actual.IssuedAt.Should().Be(expected.IssuedAt);
        actual.LastSeenAt.Should().Be(expected.LastSeenAt);
        actual.IdleExpiresAt.Should().Be(expected.IdleExpiresAt);
        actual.AbsoluteExpiresAt.Should().Be(expected.AbsoluteExpiresAt);
    }
}