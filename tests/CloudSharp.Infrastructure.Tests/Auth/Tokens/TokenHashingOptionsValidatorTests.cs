using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Tokens;

[TestFixture]
public class TokenHashingOptionsValidatorTests
{
    private static readonly TokenHashingOptionsValidator Sut = new();

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Validate_WithMissingOrBlankActiveKey_ShouldFail(string? activeKey)
    {
        var options = new TokenHashingOptions { ActiveKey = activeKey ?? string.Empty };

        var result = Sut.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotBeNullOrEmpty();
        if (!string.IsNullOrEmpty(activeKey))
        {
            result.FailureMessage.Should().NotContain(activeKey);
        }
    }

    [TestCase("not-base64!@#")]
    [TestCase("$$$$$$")]
    [TestCase("with spaces and tabs")]
    public void Validate_WithNonBase64ActiveKey_ShouldFail(string activeKey)
    {
        var options = new TokenHashingOptions { ActiveKey = activeKey };

        var result = Sut.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotContain(activeKey);
    }

    [Test]
    public void Validate_WithKeyShorterThan32Bytes_ShouldFail()
    {
        var shortKey = Convert.ToBase64String(new byte[31]);
        var options = new TokenHashingOptions { ActiveKey = shortKey };

        var result = Sut.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotContain(shortKey);
    }

    [Test]
    public void Validate_WithExactly32ByteKey_ShouldSucceed()
    {
        var key = Convert.ToBase64String(new byte[32]);
        var options = new TokenHashingOptions { ActiveKey = key };

        var result = Sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public void Validate_WithLongerThan32ByteKey_ShouldSucceed()
    {
        var key = Convert.ToBase64String(new byte[64]);
        var options = new TokenHashingOptions { ActiveKey = key };

        var result = Sut.Validate(null, options);

        result.Succeeded.Should().BeTrue();
    }

    [Test]
    public void FailureMessages_ShouldNeverEchoKeyMaterial()
    {
        var sensitiveKey = Convert.ToBase64String(Enumerable.Range(0, 10).Select(i => (byte)i).ToArray());
        var options = new TokenHashingOptions { ActiveKey = sensitiveKey };

        var result = Sut.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().NotContain(sensitiveKey);
    }
}