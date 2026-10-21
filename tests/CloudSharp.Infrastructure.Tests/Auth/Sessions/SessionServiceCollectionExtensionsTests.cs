using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Infrastructure.Auth.Sessions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

[TestFixture]
public class SessionServiceCollectionExtensionsTests
{
    private static IConfiguration ConfigWithRedis(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(connectionString is null
                ? []
                : new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = connectionString })
            .Build();

    [Test]
    public void AddRedisSessionStore_WithMissingConnectionString_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddRedisSessionStore(ConfigWithRedis(null));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:Redis*");
    }

    [Test]
    public void AddRedisSessionStore_WithInvalidConnectionString_ShouldThrowWithoutExposingValue()
    {
        var services = new ServiceCollection();
        var invalidValue = "syncTimeout=not-a-number";

        var act = () => services.AddRedisSessionStore(ConfigWithRedis(invalidValue));

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().NotContain(invalidValue);
    }

    [Test]
    public void AddRedisSessionStore_WithValidConnectionString_ShouldResolveSingletons()
    {
        var services = new ServiceCollection();
        services.AddRedisSessionStore(ConfigWithRedis("localhost:1,connectTimeout=100"));
        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<ISessionStore>();
        var multiplexer = provider.GetRequiredService<IConnectionMultiplexer>();

        store.Should().BeOfType<RedisSessionStore>();
        store.Should().BeSameAs(provider.GetRequiredService<ISessionStore>());
        multiplexer.Should().BeSameAs(provider.GetRequiredService<IConnectionMultiplexer>());
    }
}