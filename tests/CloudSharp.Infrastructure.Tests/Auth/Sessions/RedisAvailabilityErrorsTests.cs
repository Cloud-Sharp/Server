using CloudSharp.Infrastructure.Auth.Sessions;
using FluentAssertions;
using NUnit.Framework;
using StackExchange.Redis;

namespace CloudSharp.Infrastructure.Tests.Auth.Sessions;

[TestFixture]
public class RedisAvailabilityErrorsTests
{
    [Test]
    public void IsDependencyUnavailable_WithConnectionException_ShouldReturnTrue()
    {
        var exception = new RedisConnectionException(
            ConnectionFailureType.SocketFailure,
            "It was not possible to connect to the redis server(s).");

        RedisAvailabilityErrors.IsDependencyUnavailable(exception).Should().BeTrue();
    }

    [Test]
    public void IsDependencyUnavailable_WithTimeoutException_ShouldReturnTrue()
    {
        var exception = new RedisTimeoutException("Timeout performing HMGET", CommandStatus.Sent);

        RedisAvailabilityErrors.IsDependencyUnavailable(exception).Should().BeTrue();
    }

    [Test]
    public void IsDependencyUnavailable_WithServerException_ShouldReturnFalse()
    {
        var exception = new RedisServerException("WRONGTYPE Operation against a key");

        RedisAvailabilityErrors.IsDependencyUnavailable(exception).Should().BeFalse();
    }

    [Test]
    public void IsDependencyUnavailable_WithUnexpectedException_ShouldReturnFalse()
    {
        RedisAvailabilityErrors.IsDependencyUnavailable(new InvalidOperationException())
            .Should().BeFalse();
    }
}