using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// Redis 작업 중 확인 가능한 연결·timeout 장애만 분류한다. 서버 오류(WRONGTYPE, Lua 오류),
/// 데이터 손상, 취소와 예상하지 못한 예외는 포함하지 않고 그대로 전파한다.
/// </summary>
internal static class RedisAvailabilityErrors
{
    internal static bool IsDependencyUnavailable(Exception exception) =>
        exception is RedisConnectionException or RedisTimeoutException;
}