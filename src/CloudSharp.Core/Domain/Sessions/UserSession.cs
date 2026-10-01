using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using FluentResults;

namespace CloudSharp.Core.Domain.Sessions;

/// <summary>
/// 로그인으로 발급된 opaque bearer token의 단명 인증 상태. Redis에 저장하는 보안 모델로 취급하며
/// 영속 Aggregate가 아니다. 원문 token은 저장하지 않고 <see cref="TokenHash"/>만 보관한다.
/// </summary>
public sealed class UserSession
{
    /// <summary>Idle timeout: 24시간.</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(24);

    /// <summary>Absolute timeout: 7일.</summary>
    private static readonly TimeSpan AbsoluteTimeout = TimeSpan.FromDays(7);

    /// <summary>활동 갱신 최소 간격: 5분.</summary>
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 세션 식별자. UUIDv7, 외부 응답에는 기본 비노출.
    /// </summary>
    public Guid SessionId { get; private set; }

    /// <summary>
    /// 내부 사용자 FK.
    /// </summary>
    public long UserId { get; private set; }

    /// <summary>
    /// 인증 principal·외부 이벤트용 사용자 식별자.
    /// </summary>
    public Guid UserPublicId { get; private set; }

    /// <summary>
    /// 저장된 token 해시. 원문 token은 보관하지 않는다.
    /// </summary>
    public TokenHash TokenHash { get; private set; } = null!;

    /// <summary>
    /// 발급 시점의 User SecurityVersion.
    /// </summary>
    public long SecurityVersion { get; private set; }

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset IdleExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    private UserSession() { }

    /// <summary>
    /// 새 세션을 발급한다. UUIDv7 <see cref="SessionId"/>를 생성하고 24시간 idle / 7일 absolute 만료를 계산한다.
    /// idle 만료는 absolute 만료를 넘지 않게 cap한다.
    /// </summary>
    public static UserSession Issue(
        long userId,
        Guid userPublicId,
        TokenHash tokenHash,
        long securityVersion,
        DateTimeOffset now)
    {
        if (userId < 0)
        {
            throw new ArgumentException("User id must not be negative.", nameof(userId));
        }

        if (userPublicId == Guid.Empty)
        {
            throw new ArgumentException("User public id must not be empty.", nameof(userPublicId));
        }

        ArgumentNullException.ThrowIfNull(tokenHash);

        if (securityVersion < 1)
        {
            throw new ArgumentException("SecurityVersion must be positive.", nameof(securityVersion));
        }

        if (now.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Issue requires UTC now.", nameof(now));
        }

        var absoluteExpiresAt = now.Add(AbsoluteTimeout);
        var idleExpiresAt = now.Add(IdleTimeout);
        if (idleExpiresAt > absoluteExpiresAt)
        {
            idleExpiresAt = absoluteExpiresAt;
        }

        return new UserSession
        {
            SessionId = Guid.CreateVersion7(),
            UserId = userId,
            UserPublicId = userPublicId,
            TokenHash = tokenHash,
            SecurityVersion = securityVersion,
            IssuedAt = now,
            LastSeenAt = now,
            IdleExpiresAt = idleExpiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
        };
    }

    /// <summary>
    /// Infrastructure 복원 전용. 신규 validation은 수행하지 않고
    /// <c>IssuedAt &lt;= LastSeenAt &lt;= IdleExpiresAt &lt;= AbsoluteExpiresAt</c>, UTC, 식별자, 보안 버전만 방어 검증한다.
    /// </summary>
    public static UserSession Reconstitute(
        Guid sessionId,
        long userId,
        Guid userPublicId,
        TokenHash tokenHash,
        long securityVersion,
        DateTimeOffset issuedAt,
        DateTimeOffset lastSeenAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("Session id must not be empty.", nameof(sessionId));
        }

        if (userId < 0)
        {
            throw new ArgumentException("User id must not be negative.", nameof(userId));
        }

        if (userPublicId == Guid.Empty)
        {
            throw new ArgumentException("User public id must not be empty.", nameof(userPublicId));
        }

        ArgumentNullException.ThrowIfNull(tokenHash);

        if (securityVersion < 1)
        {
            throw new ArgumentException("SecurityVersion must be positive.", nameof(securityVersion));
        }

        EnsureUtc(issuedAt, nameof(issuedAt));
        EnsureUtc(lastSeenAt, nameof(lastSeenAt));
        EnsureUtc(idleExpiresAt, nameof(idleExpiresAt));
        EnsureUtc(absoluteExpiresAt, nameof(absoluteExpiresAt));

        if (lastSeenAt < issuedAt)
        {
            throw new ArgumentException("LastSeenAt must not be earlier than IssuedAt.", nameof(lastSeenAt));
        }

        if (idleExpiresAt < lastSeenAt)
        {
            throw new ArgumentException("IdleExpiresAt must not be earlier than LastSeenAt.", nameof(idleExpiresAt));
        }

        if (absoluteExpiresAt < idleExpiresAt)
        {
            throw new ArgumentException("AbsoluteExpiresAt must not be earlier than IdleExpiresAt.", nameof(absoluteExpiresAt));
        }

        return new UserSession
        {
            SessionId = sessionId,
            UserId = userId,
            UserPublicId = userPublicId,
            TokenHash = tokenHash,
            SecurityVersion = securityVersion,
            IssuedAt = issuedAt,
            LastSeenAt = lastSeenAt,
            IdleExpiresAt = idleExpiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
        };
    }

    /// <summary>
    /// 현재 요청의 token hash, User 상태, 보안 버전, idle/absolute 만료를 검증한다.
    /// </summary>
    public Result Validate(
        TokenHash presentedHash,
        long currentSecurityVersion,
        UserStatus currentUserStatus,
        DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail(SessionError.AuthSessionInvalidState("Validate requires UTC now."));
        }

        if (currentUserStatus != UserStatus.Active)
        {
            return Result.Fail(SessionError.AuthUserInactive());
        }

        if (currentSecurityVersion != SecurityVersion)
        {
            return Result.Fail(SessionError.AuthSessionInvalid());
        }

        if (!TokenHash.Equals(presentedHash))
        {
            return Result.Fail(SessionError.AuthSessionInvalid());
        }

        if (now >= AbsoluteExpiresAt)
        {
            return Result.Fail(SessionError.AuthSessionExpired());
        }

        if (now >= IdleExpiresAt)
        {
            return Result.Fail(SessionError.AuthSessionExpired());
        }

        return Result.Ok();
    }

    /// <summary>
    /// 활동 갱신. <paramref name="now"/>가 <see cref="LastSeenAt"/>에서 5분 미만이면 no-op으로 성공(false)을 반환하고,
    /// 5분 이상이면 <see cref="LastSeenAt"/>과 <see cref="IdleExpiresAt"/>을 단조 증가시키고 true를 반환한다.
    /// <see cref="IdleExpiresAt"/>은 <see cref="AbsoluteExpiresAt"/>을 넘지 않게 cap한다.
    /// </summary>
    public Result<bool> TouchLastSeen(DateTimeOffset now)
    {
        if (now.Offset != TimeSpan.Zero)
        {
            return Result.Fail<bool>(SessionError.AuthSessionInvalidState("TouchLastSeen requires UTC now."));
        }

        if (now < LastSeenAt)
        {
            return Result.Fail<bool>(SessionError.AuthSessionInvalidState("TouchLastSeen cannot move time backward."));
        }

        if (now >= AbsoluteExpiresAt)
        {
            return Result.Fail<bool>(SessionError.AuthSessionExpired());
        }

        if (now >= IdleExpiresAt)
        {
            return Result.Fail<bool>(SessionError.AuthSessionExpired());
        }

        if (now - LastSeenAt < TouchInterval)
        {
            return Result.Ok(false);
        }

        var newIdleExpiresAt = now.Add(IdleTimeout);
        if (newIdleExpiresAt > AbsoluteExpiresAt)
        {
            newIdleExpiresAt = AbsoluteExpiresAt;
        }

        LastSeenAt = now;
        IdleExpiresAt = newIdleExpiresAt;
        return Result.Ok(true);
    }

    private static void EnsureUtc(DateTimeOffset value, string paramName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"{paramName} must be UTC.", paramName);
        }
    }
}