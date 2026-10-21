using CloudSharp.Core.Abstractions.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// Redis 세션 저장소 DI 등록 확장. <c>ConnectionStrings:Redis</c>를 사용해
/// <see cref="IConnectionMultiplexer"/>와 <see cref="ISessionStore"/>를 singleton으로 등록한다.
/// </summary>
public static class SessionServiceCollectionExtensions
{
    internal const string ConnectionStringName = "Redis";

    /// <summary>
    /// <c>ConnectionStrings:Redis</c> 연결 문자열로 <see cref="IConnectionMultiplexer"/>와
    /// <see cref="ISessionStore"/> singleton을 등록한다. 연결 문자열이 누락되었거나 해석할 수 없으면
    /// 연결 문자열 값을 노출하지 않는 구성 오류를 던진다. multiplexer는 첫 해결 시점에 연결하며
    /// 연결 실패로 시작을 중단하지 않고 백그라운드로 재접속한다(장애 중 요청은 fail closed).
    /// </summary>
    public static IServiceCollection AddRedisSessionStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is required.");
        }

        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is not a valid Redis connection configuration.",
                exception);
        }

        // 호스트 시작이 Redis 가용성에 묶이지 않게 한다. 단절 중 명령은 RedisConnectionException으로
        // 실패하고 RedisSessionStore가 DEPENDENCY_UNAVAILABLE로 fail closed 처리한다.
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options));
        services.AddSingleton<ISessionStore, RedisSessionStore>();

        return services;
    }
}
