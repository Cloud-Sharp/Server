using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Common.Tokens;

[TestFixture]
public class TokenHashTests
{
    private const string ValidHash = "abc123-_XYZ456def789";

    [Test]
    public void Create_WithValidHash_ShouldTrimAndStoreValue()
    {
        var result = TokenHash.Create($"  {ValidHash}  ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo(ValidHash));
    }

    [Test]
    public void Create_WithNull_ShouldFailWithTokenHashInvalid()
    {
        var result = TokenHash.Create(null!);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.TokenHashInvalid);
    }

    [Test]
    public void Create_WithEmpty_ShouldFailWithTokenHashInvalid()
    {
        var result = TokenHash.Create(string.Empty);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.TokenHashInvalid);
    }

    [Test]
    public void Create_WithWhitespace_ShouldFailWithTokenHashInvalid()
    {
        var result = TokenHash.Create("   ");

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.TokenHashInvalid);
    }

    [Test]
    public void Create_WithOver128Chars_ShouldFailWithTokenHashInvalid()
    {
        var tooLong = new string('a', 129);

        var result = TokenHash.Create(tooLong);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.TokenHashInvalid);
    }

    [Test]
    public void Create_WithRawTokenPrefix_ShouldFailWithTokenHashInvalid()
    {
        var rawToken = "cs_sess_someRandomBody";

        var result = TokenHash.Create(rawToken);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.Session.TokenHashInvalid);
    }

    [Test]
    public void Create_WithExactly128Chars_ShouldSucceed()
    {
        var validLength = new string('a', 128);

        var result = TokenHash.Create(validLength);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo(validLength));
    }

    [Test]
    public void ToString_AlwaysReturnsRedacted()
    {
        var result = TokenHash.Create(ValidHash);

        Assume.That(result.IsSuccess, Is.True);

        Assert.That(result.Value.ToString(), Is.EqualTo("[redacted]"));
    }

    [Test]
    public void Reconstitute_WithInvalidValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => TokenHash.Reconstitute(string.Empty));
        Assert.Throws<ArgumentException>(() => TokenHash.Reconstitute("   "));
        Assert.Throws<ArgumentException>(() => TokenHash.Reconstitute(new string('a', 129)));
        Assert.Throws<ArgumentException>(() => TokenHash.Reconstitute("cs_sess_raw"));
    }

    [Test]
    public void Reconstitute_WithValidValue_ShouldReturnTokenHash()
    {
        var hash = TokenHash.Reconstitute(ValidHash);

        Assert.That(hash.Value, Is.EqualTo(ValidHash));
    }

    [Test]
    public void SecurityRegression_RawTokenDoesNotLeakIntoErrorMetadata()
    {
        var sensitive = "cs_sess_SUPERSECRETOKENBODY1234567890";

        var result = TokenHash.Create(sensitive);

        Assert.That(result.IsFailed, Is.True);
        foreach (var error in result.Errors)
        {
            Assert.That(error.Message, Does.Not.Contain(sensitive));
            foreach (var metadata in error.Metadata.Values)
            {
                Assert.That(metadata?.ToString(), Does.Not.Contain(sensitive));
            }
        }
    }

    [Test]
    public void SecurityRegression_LongHashValueDoesNotLeakIntoErrorMetadata()
    {
        var sensitive = new string('x', 200);

        var result = TokenHash.Create(sensitive);

        Assert.That(result.IsFailed, Is.True);
        foreach (var error in result.Errors)
        {
            Assert.That(error.Message, Does.Not.Contain(sensitive));
            Assert.That(error.Metadata.ContainsKey("AttemptedValue"), Is.False);
            foreach (var metadata in error.Metadata.Values)
            {
                Assert.That(metadata?.ToString(), Does.Not.Contain(sensitive));
            }
        }
    }

    private static void AssertError<T>(Result<T> result, string expectedCode)
    {
        var hasCode = result.Errors.Any(e =>
            e.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string s && s == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }
}