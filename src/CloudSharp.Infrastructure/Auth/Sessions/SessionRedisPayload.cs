using System.Globalization;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// <see cref="UserSession"/>과 Redis 저장값 사이의 매핑. 세션 본문은
/// <c>auth:session:{tokenHash}</c> Hash, 사용자별 인덱스는 <c>auth:user_sessions:{userId}</c> Sorted Set이며,
/// <see langword="long"/>은 십진 문자열, 시각은 고정폭(19자리) UTC ticks 문자열로 저장해
/// Lua script가 값을 숫자로 변환하지 않아도 식별자·보안 버전·시간 정밀도가 훼손되지 않게 한다.
/// 평문 token과 token hash는 로그·예외에 노출하지 않는 <see cref="TokenHash"/> 규칙을 그대로 따른다.
/// </summary>
internal static class SessionRedisPayload
{
    internal const string SessionKeyPrefix = "auth:session:";
    internal const string UserSessionsKeyPrefix = "auth:user_sessions:";

    internal const string SessionIdField = "sessionId";
    internal const string UserIdField = "userId";
    internal const string UserPublicIdField = "userPublicId";
    internal const string SecurityVersionField = "securityVersion";
    internal const string IssuedAtField = "issuedAt";
    internal const string LastSeenAtField = "lastSeenAt";
    internal const string IdleExpiresAtField = "idleExpiresAt";
    internal const string AbsoluteExpiresAtField = "absoluteExpiresAt";

    /// <summary>.NET ticks 자릿수 상한. DateTimeOffset 최댓값도 19자리 안에 들어온다.</summary>
    private const int TicksWidth = 19;

    internal static RedisKey BodyKey(TokenHash tokenHash) => SessionKeyPrefix + tokenHash.Value;

    internal static RedisKey UserIndexKey(long userId) =>
        UserSessionsKeyPrefix + userId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <see cref="SessionStoreLuaScripts.Store"/>의 ARGV. 본문 필드값 8개(ARGV[1..8]) 뒤에
    /// token hash member(ARGV[9]), issuedAt Unix seconds score(ARGV[10]), 최대 세션 수(ARGV[11]),
    /// 본문 key 접두사(ARGV[12])가 따른다.
    /// </summary>
    internal static RedisValue[] ToStoreArguments(UserSession session) =>
    [
        FormatGuid(session.SessionId),
        FormatLong(session.UserId),
        FormatGuid(session.UserPublicId),
        FormatLong(session.SecurityVersion),
        FormatTicks(session.IssuedAt),
        FormatTicks(session.LastSeenAt),
        FormatTicks(session.IdleExpiresAt),
        FormatTicks(session.AbsoluteExpiresAt),
        session.TokenHash.Value,
        session.IssuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
        SessionStoreLuaScripts.MaxSessions.ToString(CultureInfo.InvariantCulture),
        SessionKeyPrefix,
    ];

    /// <summary>
    /// <see cref="SessionStoreLuaScripts.Remove"/>의 ARGV. token hash member(ARGV[1])와
    /// 본문 key 접두사(ARGV[2]).
    /// </summary>
    internal static RedisValue[] ToRemoveArguments(UserSession session) =>
    [
        session.TokenHash.Value,
        SessionKeyPrefix,
    ];

    /// <summary>
    /// <see cref="SessionStoreLuaScripts.FinalizePasswordChange"/>의 ARGV. 기대 sessionId(ARGV[1]),
    /// userId(ARGV[2]), 변경 전 보안 버전(ARGV[3]), 새 보안 버전(ARGV[4]), 현재 시각 ticks(ARGV[5]),
    /// token hash member(ARGV[6]), 본문 key 접두사(ARGV[7]).
    /// </summary>
    internal static RedisValue[] ToFinalizeArguments(
        UserSession currentSession,
        long newSecurityVersion,
        DateTimeOffset now) =>
    [
        FormatGuid(currentSession.SessionId),
        FormatLong(currentSession.UserId),
        FormatLong(currentSession.SecurityVersion),
        FormatLong(newSecurityVersion),
        FormatTicks(now),
        currentSession.TokenHash.Value,
        SessionKeyPrefix,
    ];

    /// <summary>
    /// 저장된 본문을 <see cref="UserSession.Reconstitute"/>로 독립 복원한다.
    /// 필드 누락·손상과 저장값의 복원 불변식 위반은 값을 노출하지 않는
    /// <see cref="SessionPayloadCorruptException"/>으로 전파한다.
    /// token hash는 payload에 저장하지 않으므로 조회에 사용한 <paramref name="tokenHash"/>를 그대로 쓴다.
    /// </summary>
    internal static UserSession ToDomain(HashEntry[] entries, TokenHash tokenHash)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(tokenHash);

        var fields = new Dictionary<RedisValue, RedisValue>(entries.Length);
        foreach (var entry in entries)
        {
            fields[entry.Name] = entry.Value;
        }

        var sessionId = ParseGuid(Required(fields, SessionIdField), SessionIdField);
        var userId = ParseLong(Required(fields, UserIdField), UserIdField);
        var userPublicId = ParseGuid(Required(fields, UserPublicIdField), UserPublicIdField);
        var securityVersion = ParseLong(Required(fields, SecurityVersionField), SecurityVersionField);
        var issuedAt = ParseTicks(Required(fields, IssuedAtField), IssuedAtField);
        var lastSeenAt = ParseTicks(Required(fields, LastSeenAtField), LastSeenAtField);
        var idleExpiresAt = ParseTicks(Required(fields, IdleExpiresAtField), IdleExpiresAtField);
        var absoluteExpiresAt = ParseTicks(Required(fields, AbsoluteExpiresAtField), AbsoluteExpiresAtField);

        try
        {
            return UserSession.Reconstitute(
                sessionId,
                userId,
                userPublicId,
                tokenHash,
                securityVersion,
                issuedAt,
                lastSeenAt,
                idleExpiresAt,
                absoluteExpiresAt);
        }
        catch (ArgumentException exception) when (exception.ParamName is
            SessionIdField or UserIdField or UserPublicIdField or SecurityVersionField
            or IssuedAtField or LastSeenAtField or IdleExpiresAtField or AbsoluteExpiresAtField)
        {
            throw Corrupt(exception.ParamName!, "session");
        }
    }

    private static RedisValue Required(Dictionary<RedisValue, RedisValue> fields, string fieldName)
    {
        if (fields.TryGetValue(fieldName, out var value) && value.HasValue)
        {
            return value;
        }

        throw new SessionPayloadCorruptException($"Stored session payload field '{fieldName}' is missing.");
    }

    private static Guid ParseGuid(RedisValue value, string fieldName) =>
        Guid.TryParseExact((string)value!, "N", out var parsed)
            ? parsed
            : throw Corrupt(fieldName, "GUID");

    private static long ParseLong(RedisValue value, string fieldName) =>
        long.TryParse((string)value!, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw Corrupt(fieldName, "integer");

    private static DateTimeOffset ParseTicks(RedisValue value, string fieldName)
    {
        try
        {
            return new DateTimeOffset(ParseLong(value, fieldName), TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Corrupt(fieldName, "timestamp");
        }
    }

    private static SessionPayloadCorruptException Corrupt(string fieldName, string kind) =>
        new($"Stored session payload field '{fieldName}' is not a valid {kind} value.");

    private static string FormatGuid(Guid value) => value.ToString("N", CultureInfo.InvariantCulture);

    private static string FormatLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string FormatTicks(DateTimeOffset value) =>
        value.UtcTicks.ToString($"D{TicksWidth}", CultureInfo.InvariantCulture);
}
