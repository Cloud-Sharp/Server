using System.Net;
using System.Net.Sockets;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Infrastructure.Auth.Sessions;
using CloudSharp.Infrastructure.Tests.TestSupport.Builders;
using FluentResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class RedisSessionStoreReconnectIntegrationTests
{
    private RedisContainer? container;
    private ServiceProvider? provider;
    private ISessionStore store = null!;

    private IConnectionMultiplexer Connection => provider!.GetRequiredService<IConnectionMultiplexer>();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        // Docker otherwise allocates a different host port after restarting.
        container = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .WithPortBinding(FindAvailablePort(), 6379)
            .Build();
        await container.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = container.GetConnectionString()
                    + ",asyncTimeout=10000,connectTimeout=1000,connectRetry=0",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddRedisSessionStore(configuration);
        provider = services.BuildServiceProvider();
        store = provider.GetRequiredService<ISessionStore>();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (provider is not null)
        {
            await provider.DisposeAsync();
        }

        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }

    [Test]
    public async Task Reconnect_ShouldNotApplyCommandsRejectedDuringOutage()
    {
        var serverTime = await Connection.GetServer(Connection.GetEndPoints()[0]).TimeAsync();
        var now = new DateTimeOffset(DateTime.SpecifyKind(serverTime, DateTimeKind.Utc));
        var unstored = TestUserSessions.Valid(userId: 1, tokenHash: "sh_unstored", now: now);
        var removable = TestUserSessions.Valid(userId: 2, tokenHash: "sh_removable", now: now);
        var current = TestUserSessions.Valid(
            userId: 3, tokenHash: "sh_current", securityVersion: 5, now: now);
        var other = TestUserSessions.Valid(
            userId: 3, tokenHash: "sh_other", securityVersion: 5, now: now);
        foreach (var session in new[] { removable, current, other })
        {
            Assert.That((await store.StoreAsync(session)).IsSuccess, Is.True);
        }

        // Persist the test data so restarting the same container only exercises
        // connection recovery, not Redis durability or data loss.
        await Connection.GetDatabase().ExecuteAsync("SAVE");
        await container!.StopAsync();
        await WaitForConnectionStateAsync(false);

        var storing = store.StoreAsync(unstored);
        var finding = store.FindByTokenHashAsync(current.TokenHash);
        var removing = store.RemoveAsync(removable);
        var finalizing = store.FinalizePasswordChangeAsync(current, 6, now);
        // Fail-fast must finish well before the configured 10-second async timeout.
        await Task.WhenAll(storing, finding, removing, finalizing).WaitAsync(TimeSpan.FromSeconds(2));
        AssertDependencyUnavailable(await storing);
        AssertDependencyUnavailable(await finding);
        AssertDependencyUnavailable(await removing);
        AssertDependencyUnavailable(await finalizing);

        await container.StartAsync();
        await WaitForConnectionStateAsync(true);
        await Connection.GetDatabase().PingAsync();

        var afterStore = await store.FindByTokenHashAsync(unstored.TokenHash);
        Assert.That(afterStore.IsSuccess, Is.True);
        Assert.That(afterStore.Value, Is.Null);
        foreach (var session in new[] { removable, current, other })
        {
            var found = await store.FindByTokenHashAsync(session.TokenHash);
            Assert.That(found.IsSuccess, Is.True);
            Assert.That(found.Value, Is.Not.Null);
            Assert.That(found.Value!.SessionId, Is.EqualTo(session.SessionId));
            Assert.That(found.Value.SecurityVersion, Is.EqualTo(session.SecurityVersion));
        }
    }

    private async Task WaitForConnectionStateAsync(bool connected)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (Connection.IsConnected != connected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), deadline.Token);
        }
    }

    private static void AssertDependencyUnavailable(IResultBase result)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && Equals(code, ErrorCodes.Common.DependencyUnavailable)), Is.True);
    }

    private static int FindAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
