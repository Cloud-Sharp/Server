using System.Buffers.Text;
using System.Text;
using CloudSharp.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Repositories;

[TestFixture]
public sealed class KeysetCursorTests
{
    private static readonly Guid PublicId = new("019b26d0-0000-7000-8000-000000000101");
    private static readonly DateTimeOffset SortValue = new(2026, 11, 27, 3, 4, 5, TimeSpan.Zero);

    [Test]
    public void TryRead_ShouldReturnIssuedPublicIdAndSortValue()
    {
        var cursor = KeysetCursor.Issue(PublicId, SortValue.ToOffset(TimeSpan.FromHours(9)));

        var read = KeysetCursor.TryRead(cursor, out var position);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(cursor, Does.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(position.PublicId, Is.EqualTo(PublicId));
            Assert.That(position.SortValue, Is.EqualTo(SortValue));
            Assert.That(position.SortValue!.Value.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void TryRead_ShouldReturnPositionWithoutSortValue()
    {
        var read = KeysetCursor.TryRead(KeysetCursor.Issue(PublicId), out var position);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(position.PublicId, Is.EqualTo(PublicId));
            Assert.That(position.SortValue, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("not-a-cursor")]
    [TestCase("e30")]
    public void TryRead_ShouldRejectMalformedCursor(string cursor)
    {
        Assert.That(KeysetCursor.TryRead(cursor, out _), Is.False);
    }

    [TestCase("""{"v":2,"id":"019b26d0-0000-7000-8000-000000000101"}""")]
    [TestCase("""{"v":1,"id":"00000000-0000-0000-0000-000000000000"}""")]
    [TestCase("""{"v":1,"id":"019b26d0-0000-7000-8000-000000000101","t":-1}""")]
    [TestCase("""{"v":1,"id":"019b26d0-0000-7000-8000-000000000101","t":3155378976000000000}""")]
    [TestCase("""{"id":"019b26d0-0000-7000-8000-000000000101","v":1}""")]
    [TestCase("""{"v":1,"id":"019b26d0-0000-7000-8000-000000000101","t":null}""")]
    public void TryRead_ShouldRejectInvalidOrNonCanonicalPayload(string json)
    {
        var cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

        Assert.That(KeysetCursor.TryRead(cursor, out _), Is.False);
    }

    [Test]
    public void TryRead_ShouldRejectPaddedBase64()
    {
        var cursor = KeysetCursor.Issue(PublicId) + "=";

        Assert.That(KeysetCursor.TryRead(cursor, out _), Is.False);
    }
}
