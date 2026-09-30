using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Sessions;

[TestFixture]
public class UserSessionTests
{
    private static readonly DateTimeOffset Now =
        new DateTimeOffset(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private const long UserId = 42;
    private const long SecurityVersion = 5;
    private const string ValidHashValue = "abc123def456ghi789";

    private static readonly Guid UserPublicId = Guid.CreateVersion7();
    private static readonly TokenHash ValidHash = TokenHash.Reconstitute(ValidHashValue);

    [Test]
    public void Issue_ShouldGenerateUuidV7SessionId()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        Assert.That(session.SessionId, Is.Not.EqualTo(Guid.Empty));
        var sessionId = session.SessionId.ToString("D");
        Assert.That(sessionId[14], Is.EqualTo('7'), "SessionId version digit in canonical string should be 7 for UUIDv7.");
    }

    [Test]
    public void Issue_ShouldSet24HourIdleAnd7DayAbsoluteExpiry()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        Assert.Multiple(() =>
        {
            Assert.That(session.IssuedAt, Is.EqualTo(Now));
            Assert.That(session.LastSeenAt, Is.EqualTo(Now));
            Assert.That(session.IdleExpiresAt, Is.EqualTo(Now.AddHours(24)));
            Assert.That(session.AbsoluteExpiresAt, Is.EqualTo(Now.AddDays(7)));
            Assert.That(session.SecurityVersion, Is.EqualTo(SecurityVersion));
            Assert.That(session.UserId, Is.EqualTo(UserId));
            Assert.That(session.UserPublicId, Is.EqualTo(UserPublicId));
        });
    }

    [Test]
    public void Issue_WithNegativeUserId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => UserSession.Issue(-1, UserPublicId, ValidHash, SecurityVersion, Now));
    }

    [Test]
    public void Issue_WithEmptyUserPublicId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => UserSession.Issue(UserId, Guid.Empty, ValidHash, SecurityVersion, Now));
    }

    [Test]
    public void Issue_WithNullTokenHash_ShouldThrow()
    {
        Assert.Throws<ArgumentNullException>(() => UserSession.Issue(UserId, UserPublicId, null!, SecurityVersion, Now));
    }

    [Test]
    public void Issue_WithZeroSecurityVersion_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => UserSession.Issue(UserId, UserPublicId, ValidHash, 0, Now));
    }

    [Test]
    public void Issue_WithNonUtcNow_ShouldThrow()
    {
        var nonUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9));

        Assert.Throws<ArgumentException>(() => UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, nonUtc));
    }

    [Test]
    public void Reconstitute_WithValidState_ShouldRestoreFields()
    {
        var issuedAt = Now;
        var lastSeenAt = Now.AddHours(1);
        var idleExpiresAt = Now.AddHours(25);
        var absoluteExpiresAt = Now.AddDays(7);

        var session = UserSession.Reconstitute(
            Guid.CreateVersion7(), UserId, UserPublicId, ValidHash, SecurityVersion,
            issuedAt, lastSeenAt, idleExpiresAt, absoluteExpiresAt);

        Assert.Multiple(() =>
        {
            Assert.That(session.LastSeenAt, Is.EqualTo(lastSeenAt));
            Assert.That(session.IdleExpiresAt, Is.EqualTo(idleExpiresAt));
            Assert.That(session.AbsoluteExpiresAt, Is.EqualTo(absoluteExpiresAt));
        });
    }

    [Test]
    public void Reconstitute_WithLastSeenBeforeIssued_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(lastSeenAt: Now.AddSeconds(-1)));
    }

    [Test]
    public void Reconstitute_WithIdleBeforeLastSeen_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(idleExpiresAt: Now.AddSeconds(-1)));
    }

    [Test]
    public void Reconstitute_WithAbsoluteBeforeIdle_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(absoluteExpiresAt: Now.AddHours(24).AddSeconds(-1)));
    }

    [Test]
    public void Reconstitute_WithNonUtcTimestamp_ShouldThrow()
    {
        var nonUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9));
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(issuedAt: nonUtc));
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(lastSeenAt: nonUtc));
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(idleExpiresAt: nonUtc));
        Assert.Throws<ArgumentException>(() =>
            ReconstituteSession(absoluteExpiresAt: nonUtc));
    }

    [Test]
    public void Reconstitute_WithEmptySessionId_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => ReconstituteSession(sessionId: Guid.Empty));
    }

    [Test]
    public void Validate_WithMatchingHashAndVersionAndActiveUser_ShouldSucceed()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Validate_WithMismatchedHash_ShouldFailWithAuthSessionInvalid()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var other = TokenHash.Reconstitute("different-hash-value-123");

        var result = session.Validate(other, SecurityVersion, UserStatus.Active, Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionInvalid);
    }

    [Test]
    public void Validate_WithMismatchedSecurityVersion_ShouldFailWithAuthSessionInvalid()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion + 1, UserStatus.Active, Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionInvalid);
    }

    [Test]
    public void Validate_WithSuspendedUser_ShouldFailWithAuthUserInactive()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Suspended, Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthUserInactive);
    }

    [Test]
    public void Validate_WithDeletedUser_ShouldFailWithAuthUserInactive()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Deleted, Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthUserInactive);
    }

    [Test]
    public void Validate_JustBeforeIdleExpiry_ShouldSucceed()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var justBefore = session.IdleExpiresAt.AddSeconds(-1);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, justBefore);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Validate_AtIdleExpiry_ShouldFailWithAuthSessionExpired()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, session.IdleExpiresAt);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public void Validate_AfterIdleExpiry_ShouldFailWithAuthSessionExpired()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, session.IdleExpiresAt.AddSeconds(1));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public void Validate_JustBeforeAbsoluteExpiry_ShouldSucceed()
    {
        var absoluteExpiresAt = Now.AddDays(7);
        var session = UserSession.Reconstitute(
            Guid.CreateVersion7(),
            UserId,
            UserPublicId,
            ValidHash,
            SecurityVersion,
            Now,
            Now.AddMinutes(1),
            absoluteExpiresAt,
            absoluteExpiresAt);

        var justBefore = absoluteExpiresAt.AddSeconds(-1);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, justBefore);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Validate_AtAbsoluteExpiry_ShouldFailWithAuthSessionExpired()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, session.AbsoluteExpiresAt);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public void Validate_WithNonUtcNow_ShouldFailWithAuthSessionInvalidState()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var nonUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9));

        var result = session.Validate(ValidHash, SecurityVersion, UserStatus.Active, nonUtc);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionInvalidState);
    }

    [Test]
    public void TouchLastSeen_Within5Minutes_ShouldReturnFalseAndNotUpdate()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var originalLastSeen = session.LastSeenAt;
        var originalIdle = session.IdleExpiresAt;
        var within5 = Now.AddMinutes(4);

        var result = session.TouchLastSeen(within5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.False);
        Assert.Multiple(() =>
        {
            Assert.That(session.LastSeenAt, Is.EqualTo(originalLastSeen));
            Assert.That(session.IdleExpiresAt, Is.EqualTo(originalIdle));
        });
    }

    [Test]
    public void TouchLastSeen_ExactlyAt5Minutes_ShouldUpdateAndReturnTrue()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var after5 = Now.AddMinutes(5);
        var expectedIdle = after5.AddHours(24);

        var result = session.TouchLastSeen(after5);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(session.LastSeenAt, Is.EqualTo(after5));
            Assert.That(session.IdleExpiresAt, Is.EqualTo(expectedIdle));
        });
    }

    [Test]
    public void TouchLastSeen_After5Minutes_ShouldUpdateLastSeenAndIdleExpiry()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var after10 = Now.AddMinutes(10);

        var result = session.TouchLastSeen(after10);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
        Assert.That(session.IdleExpiresAt, Is.EqualTo(after10.AddHours(24)));
    }

    [Test]
    public void TouchLastSeen_ShouldCapIdleExpiryAtAbsoluteExpiry()
    {
        var issuedAt = Now.AddDays(-6);
        var lastSeenAt = Now.AddHours(-1);
        var idleExpiresAt = lastSeenAt.AddHours(24);
        var absoluteExpiresAt = issuedAt.AddDays(7);

        Assume.That(idleExpiresAt, Is.LessThan(absoluteExpiresAt));

        var session = UserSession.Reconstitute(
            Guid.CreateVersion7(),
            UserId,
            UserPublicId,
            ValidHash,
            SecurityVersion,
            issuedAt,
            lastSeenAt,
            idleExpiresAt,
            absoluteExpiresAt);

        var result = session.TouchLastSeen(Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.True);
        Assert.That(session.IdleExpiresAt, Is.EqualTo(session.AbsoluteExpiresAt));
        Assert.That(session.LastSeenAt, Is.EqualTo(Now));
    }

    [Test]
    public void TouchLastSeen_WithTimeBackward_ShouldFailWithAuthSessionInvalidState()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var firstTouch = Now.AddMinutes(10);
        session.TouchLastSeen(firstTouch);

        var result = session.TouchLastSeen(Now.AddMinutes(5));

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionInvalidState);
    }

    [Test]
    public void TouchLastSeen_WhenIdleExpired_ShouldFailWithAuthSessionExpired()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var afterIdle = session.IdleExpiresAt.AddMinutes(1);

        var result = session.TouchLastSeen(afterIdle);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public void TouchLastSeen_WhenAbsoluteExpired_ShouldFailWithAuthSessionExpired()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var afterAbsolute = session.AbsoluteExpiresAt.AddMinutes(1);

        var result = session.TouchLastSeen(afterAbsolute);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public void TouchLastSeen_WithNonUtcNow_ShouldFailWithAuthSessionInvalidState()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var nonUtc = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.FromHours(9));

        var result = session.TouchLastSeen(nonUtc);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.AuthSessionInvalidState);
    }

    [Test]
    public void SecurityRegression_TokenHashDoesNotLeakIntoErrorMetadata()
    {
        var session = UserSession.Issue(UserId, UserPublicId, ValidHash, SecurityVersion, Now);
        var sensitive = ValidHashValue;

        var other = TokenHash.Reconstitute("a-different-hash-value");
        var result = session.Validate(other, SecurityVersion, UserStatus.Active, Now.AddMinutes(1));

        Assert.That(result.IsFailed, Is.True);
        foreach (var error in result.Errors)
        {
            Assert.That(error.Message, Does.Not.Contain(sensitive));
            foreach (var metadata in error.Metadata.Values)
            {
                Assert.That(metadata?.ToString(), Does.Not.Contain(sensitive));
            }
        }
    }

    private static UserSession ReconstituteSession(
        Guid? sessionId = null,
        DateTimeOffset? issuedAt = null,
        DateTimeOffset? lastSeenAt = null,
        DateTimeOffset? idleExpiresAt = null,
        DateTimeOffset? absoluteExpiresAt = null)
    {
        var issued = issuedAt ?? Now;
        var lastSeen = lastSeenAt ?? Now.AddHours(1);
        var idle = idleExpiresAt ?? lastSeen.AddHours(24);
        var absolute = absoluteExpiresAt ?? Now.AddDays(7);

        return UserSession.Reconstitute(
            sessionId ?? Guid.CreateVersion7(),
            UserId,
            UserPublicId,
            ValidHash,
            SecurityVersion,
            issued,
            lastSeen,
            idle,
            absolute);
    }

    private static void AssertError<T>(Result<T> result, string expectedCode) =>
        AssertErrorCode(result, expectedCode);

    private static void AssertError(Result result, string expectedCode) =>
        AssertErrorCode(result, expectedCode);

    private static void AssertErrorCode(ResultBase result, string expectedCode)
    {
        var hasCode = result.Errors.Any(e =>
            e.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string s && s == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }
}