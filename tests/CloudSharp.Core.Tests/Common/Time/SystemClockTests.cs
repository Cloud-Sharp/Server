using CloudSharp.Core.Common.Time;
using FluentAssertions;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Common.Time;

[TestFixture]
public class SystemClockTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public void SetNow(DateTimeOffset value) => _now = value;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Test]
    public void Constructor_WithNullTimeProvider_ShouldThrowArgumentNullException()
    {
        var act = () => new SystemClock(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("timeProvider");
    }

    [Test]
    public void UtcNow_ShouldReturnValueFromTimeProvider()
    {
        var fixedNow = new DateTimeOffset(2026, 7, 20, 3, 30, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(fixedNow);
        var clock = new SystemClock(provider);

        clock.UtcNow.Should().Be(fixedNow);
    }

    [Test]
    public void UtcNow_ShouldReflectSubsequentTimeProviderChanges()
    {
        var provider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var clock = new SystemClock(provider);

        var next = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        provider.SetNow(next);

        clock.UtcNow.Should().Be(next);
    }

    [Test]
    public void UtcNow_ShouldReturnZeroOffsetWhenProviderReturnsUtc()
    {
        var utcNow = new DateTimeOffset(2026, 7, 20, 3, 30, 0, TimeSpan.Zero);
        var provider = new FixedTimeProvider(utcNow);
        var clock = new SystemClock(provider);

        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }
}