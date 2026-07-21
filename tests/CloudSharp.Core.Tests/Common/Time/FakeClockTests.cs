using CloudSharp.TestSupport.Fakes;
using FluentAssertions;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Common.Time;

[TestFixture]
public class FakeClockTests
{
    private static readonly DateTimeOffset BaseUtc =
        new DateTimeOffset(2026, 7, 20, 3, 30, 0, TimeSpan.Zero);

    [Test]
    public void Constructor_WithUtcValue_ShouldKeepValueAsUtc()
    {
        var clock = new FakeClock(BaseUtc);

        clock.UtcNow.Should().Be(BaseUtc);
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [TestCase(9, 0, 0, "KST (+09:00) should normalize to UTC")]
    [TestCase(-5, 0, 0, "EST (-05:00) should normalize to UTC")]
    [TestCase(2, 30, 0, "non-zero offset should normalize to UTC")]
    public void Constructor_WithNonUtcOffset_ShouldNormalizeToUtc(int hours, int minutes, int seconds, string reason)
    {
        var offset = new TimeSpan(hours, minutes, seconds);
        var local = new DateTimeOffset(2026, 7, 20, 12, 0, 0, offset);
        var expectedUtc = local.ToUniversalTime();

        var clock = new FakeClock(local);

        clock.UtcNow.Should().Be(expectedUtc, reason);
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero, reason);
    }

    [Test]
    public void UtcNow_ShouldReturnInitialValue()
    {
        var clock = new FakeClock(BaseUtc);

        clock.UtcNow.Should().Be(BaseUtc);
    }

    [Test]
    public void SetUtcNow_ShouldUpdateValueAndNormalizeToUtc()
    {
        var clock = new FakeClock(BaseUtc);
        var kst = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.FromHours(9));
        var expected = kst.ToUniversalTime();

        clock.SetUtcNow(kst);

        clock.UtcNow.Should().Be(expected);
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void Advance_WithPositiveDuration_ShouldMoveClockForward()
    {
        var clock = new FakeClock(BaseUtc);

        clock.Advance(TimeSpan.FromHours(3));

        clock.UtcNow.Should().Be(BaseUtc.Add(TimeSpan.FromHours(3)));
    }

    [Test]
    public void Advance_WithZeroDuration_ShouldKeepClockAtSameValue()
    {
        var clock = new FakeClock(BaseUtc);

        clock.Advance(TimeSpan.Zero);

        clock.UtcNow.Should().Be(BaseUtc);
    }

    [Test]
    public void Advance_WithNegativeDuration_ShouldThrowArgumentOutOfRangeException()
    {
        var clock = new FakeClock(BaseUtc);
        var negative = TimeSpan.FromMinutes(-5);

        var act = () => clock.Advance(negative);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("duration");
    }

    [Test]
    public void Advance_AfterSetUtcNow_ShouldContinueFromNewValue()
    {
        var clock = new FakeClock(BaseUtc);
        var next = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        clock.SetUtcNow(next);

        clock.Advance(TimeSpan.FromMinutes(10));

        clock.UtcNow.Should().Be(next.Add(TimeSpan.FromMinutes(10)));
    }
}