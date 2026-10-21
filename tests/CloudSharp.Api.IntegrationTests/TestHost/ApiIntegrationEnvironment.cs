using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed class ApiIntegrationEnvironmentOptions
{
    internal bool BindRedisPort { get; init; }

    internal Action<IServiceCollection, ApiIntegrationEnvironment>? ConfigureTestServices { get; init; }
}

internal sealed class ApiIntegrationEnvironment : IAsyncDisposable
{
    internal const string SessionKeyPrefix = "auth:session:";
    internal const string UserSessionsKeyPrefix = "auth:user_sessions:";

    private readonly ApiIntegrationEnvironmentOptions _options;
    private readonly SemaphoreSlim _redisControl = new(1, 1);

    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private WebApplicationFactory<Program>? _factory;
    private IConnectionMultiplexer? _testRedis;
    private IConnectionMultiplexer? _appRedis;
    private ITokenHasher? _tokenHasher;
    private IPasswordHasher? _passwordHasher;
    private MutableClock? _clock;
    private string? _tokenHashingKey;
    private bool _disposed;

    private ApiIntegrationEnvironment(ApiIntegrationEnvironmentOptions options)
    {
        _options = options;
    }

    internal WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("The integration host has not been started.");

    internal MutableClock Clock =>
        _clock ?? throw new InvalidOperationException("The integration clock has not been initialized.");

    internal ITokenHasher TokenHasher =>
        _tokenHasher ?? throw new InvalidOperationException("The token hasher has not been initialized.");

    internal IPasswordHasher PasswordHasher =>
        _passwordHasher ?? throw new InvalidOperationException("The password hasher has not been initialized.");

    internal IConnectionMultiplexer TestRedis =>
        _testRedis ?? throw new InvalidOperationException("The test Redis connection has not been initialized.");

    private IConnectionMultiplexer AppRedis =>
        _appRedis ?? throw new InvalidOperationException("The application Redis connection has not been initialized.");

    internal static async Task<ApiIntegrationEnvironment> StartAsync(
        ApiIntegrationEnvironmentOptions? options = null)
    {
        var environment = new ApiIntegrationEnvironment(options ?? new ApiIntegrationEnvironmentOptions());
        try
        {
            await environment.StartCoreAsync();
            return environment;
        }
        catch
        {
            await environment.DisposeAsync();
            throw;
        }
    }

    internal TestApiClient CreateClient() => new(Factory);

    internal async Task ResetAsync()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE users RESTART IDENTITY CASCADE");
        }

        await TestRedis.GetDatabase().ExecuteAsync("FLUSHDB");
        await ResetClockAsync();
    }

    internal async Task ResetClockAsync()
    {
        var serverTime = await GetRedisServerTimeAsync();
        Clock.Set(serverTime);
    }

    internal async Task<UserSnapshot?> FindUserAsync(Guid publicId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await context.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.PublicId == publicId);
        return entity is null ? null : UserSnapshot.From(entity);
    }

    internal async Task<long> CountUsersAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Users.LongCountAsync();
    }

    internal async Task UpdateUserAsync(Guid publicId, Action<UserEntity> update)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = await context.Users.SingleAsync(user => user.PublicId == publicId);
        update(entity);
        await context.SaveChangesAsync();
    }

    internal async Task<bool> SessionBodyExistsAsync(string token)
    {
        var hash = TokenHasher.Hash(token);
        return await TestRedis.GetDatabase()
            .KeyExistsAsync(SessionKeyPrefix + hash);
    }

    internal async Task<string?> GetSessionFieldAsync(string token, string field)
    {
        var hash = TokenHasher.Hash(token);
        var value = await TestRedis.GetDatabase()
            .HashGetAsync(SessionKeyPrefix + hash, field);
        return value.HasValue ? value.ToString() : null;
    }

    internal async Task SetSessionFieldsAsync(
        string token,
        params (string Field, string Value)[] fields)
    {
        var hash = TokenHasher.Hash(token);
        var entries = fields
            .Select(field => new HashEntry(field.Field, field.Value))
            .ToArray();
        await TestRedis.GetDatabase()
            .HashSetAsync(SessionKeyPrefix + hash, entries);
    }

    internal async Task DeleteSessionFieldAsync(string token, string field)
    {
        var hash = TokenHasher.Hash(token);
        await TestRedis.GetDatabase()
            .HashDeleteAsync(SessionKeyPrefix + hash, field);
    }

    internal async Task DeleteSessionAsync(string token)
    {
        var hash = TokenHasher.Hash(token);
        await TestRedis.GetDatabase().KeyDeleteAsync(SessionKeyPrefix + hash);
    }

    internal async Task<long> GetSessionIndexCountAsync(long userId) =>
        await TestRedis.GetDatabase()
            .SortedSetLengthAsync(UserSessionsKeyPrefix + userId);

    internal async Task<IReadOnlyList<string>> GetSessionIndexMembersAsync(long userId)
    {
        var members = await TestRedis.GetDatabase()
            .SortedSetRangeByRankAsync(UserSessionsKeyPrefix + userId);
        return members
            .Select(member => member.ToString())
            .ToArray();
    }

    internal Task SaveRedisAsync() => TestRedis.GetDatabase().ExecuteAsync("SAVE");

    internal async Task StopRedisAsync(bool save)
    {
        await _redisControl.WaitAsync();
        try
        {
            if (save)
            {
                await SaveRedisAsync();
            }

            await _redis!.StopAsync();
        }
        finally
        {
            _redisControl.Release();
        }
    }

    internal async Task StartRedisAsync()
    {
        await _redisControl.WaitAsync();
        try
        {
            await _redis!.StartAsync();
            await WaitUntilAsync(
                "Redis readiness",
                async () =>
                {
                    await TestRedis.GetDatabase().PingAsync();
                    return true;
                },
                TimeSpan.FromSeconds(30));
            await WaitForAppRedisConnectionAsync(true, TimeSpan.FromSeconds(30));
            await WaitUntilAsync(
                "Application Redis readiness",
                async () =>
                {
                    await AppRedis.GetDatabase().PingAsync();
                    return true;
                },
                TimeSpan.FromSeconds(30));
        }
        finally
        {
            _redisControl.Release();
        }
    }

    internal async Task StopPostgresAsync()
    {
        await _postgres!.StopAsync();
        NpgsqlConnection.ClearAllPools();
    }

    internal async Task WaitForAppRedisConnectionAsync(
        bool connected,
        TimeSpan timeout)
    {
        try
        {
            using var deadline = new CancellationTokenSource(timeout);
            while (AppRedis.IsConnected != connected)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"The application Redis connection did not become {(connected ? "available" : "unavailable")} within {timeout}.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        if (_testRedis is not null)
        {
            await _testRedis.DisposeAsync();
        }

        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }

        _redisControl.Dispose();
    }

    private async Task StartCoreAsync()
    {
        var redisBuilder = new RedisBuilder().WithImage("redis:7-alpine");
        if (_options.BindRedisPort)
        {
            redisBuilder = redisBuilder.WithPortBinding(FindAvailablePort(), 6379);
        }

        _redis = redisBuilder.Build();
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("cloudsharp_api_tests")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _postgres.StartAsync();
        await _redis.StartAsync();

        await WaitUntilAsync(
            "PostgreSQL readiness",
            async () =>
            {
                await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1;
            },
            TimeSpan.FromSeconds(30));

        _testRedis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
        await WaitUntilAsync(
            "Redis readiness",
            async () =>
            {
                await TestRedis.GetDatabase().PingAsync();
                return true;
            },
            TimeSpan.FromSeconds(30));

        var redisTime = await GetRedisServerTimeAsync();
        _clock = new MutableClock(redisTime);
        _tokenHashingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        _factory = new ApiIntegrationApplicationFactory(
            _postgres.GetConnectionString(),
            _redis.GetConnectionString(),
            _tokenHashingKey,
            Clock,
            _options.ConfigureTestServices,
            this);

        var configuration = Factory.Services.GetRequiredService<IConfiguration>();
        if (!string.Equals(
                configuration.GetConnectionString("PostgreSql"),
                _postgres.GetConnectionString(),
                StringComparison.Ordinal)
            || !string.Equals(
                configuration.GetConnectionString("Redis"),
                _redis.GetConnectionString(),
                StringComparison.Ordinal)
            || !string.Equals(
                configuration["TokenHashing:ActiveKey"],
                _tokenHashingKey,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The integration host did not receive the test container configuration.");
        }

        _appRedis = Factory.Services.GetRequiredService<IConnectionMultiplexer>();
        _tokenHasher = Factory.Services.GetRequiredService<ITokenHasher>();
        _passwordHasher = Factory.Services.GetRequiredService<IPasswordHasher>();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.Database.EnsureCreatedAsync();
        }

        await ResetClockAsync();
    }

    private async Task<DateTimeOffset> GetRedisServerTimeAsync()
    {
        var server = TestRedis.GetServer(TestRedis.GetEndPoints()[0]);
        var time = await server.TimeAsync();
        return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }

    private static async Task WaitUntilAsync(
        string description,
        Func<Task<bool>> probe,
        TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (true)
        {
            try
            {
                if (await probe())
                {
                    return;
                }
            }
            catch (Exception)
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), deadline.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"{description} did not become ready within {timeout}.");
            }
        }
    }

    private static int FindAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}

internal sealed record UserSnapshot(
    long Id,
    Guid PublicId,
    string Email,
    string NormalizedEmail,
    string? UserName,
    string? NormalizedUserName,
    string? DisplayName,
    string PasswordHash,
    SystemRole SystemRole,
    UserStatus Status,
    long SecurityVersion,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SuspendedAt,
    DateTimeOffset? DeletedAt)
{
    internal static UserSnapshot From(UserEntity entity) =>
        new(
            entity.Id,
            entity.PublicId,
            entity.Email,
            entity.NormalizedEmail,
            entity.UserName,
            entity.NormalizedUserName,
            entity.DisplayName,
            entity.PasswordHash,
            entity.SystemRole,
            entity.Status,
            entity.SecurityVersion,
            entity.Version,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.SuspendedAt,
            entity.DeletedAt);

    public override string ToString() =>
        $"UserSnapshot {{ Id = {Id}, PublicId = {PublicId}, Email = {Email}, "
        + $"UserName = {UserName}, DisplayName = {DisplayName}, PasswordHash = [redacted], "
        + $"SystemRole = {SystemRole}, Status = {Status}, SecurityVersion = {SecurityVersion}, "
        + $"Version = {Version}, CreatedAt = {CreatedAt}, UpdatedAt = {UpdatedAt}, "
        + $"SuspendedAt = {SuspendedAt}, DeletedAt = {DeletedAt} }}";
}
