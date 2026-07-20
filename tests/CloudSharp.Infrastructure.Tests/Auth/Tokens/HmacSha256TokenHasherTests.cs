using System.Security.Cryptography;
using System.Text;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Tokens;

[TestFixture]
public class HmacSha256TokenHasherTests
{
    private static string ValidKeyBase64(int bytes = 32) =>
        Convert.ToBase64String(Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());

    private static TokenHashingOptions OptionsWithKey(string activeKey) =>
        new() { ActiveKey = activeKey };

    [Test]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        var act = () => new HmacSha256TokenHasher(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Constructor_WithEmptyOrWhitespaceActiveKey_ShouldThrowArgumentException(string activeKey)
    {
        var act = () => new HmacSha256TokenHasher(OptionsWithKey(activeKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName("options");
    }

    [TestCase("not-base64!@#")]
    [TestCase("$$$$$$")]
    public void Constructor_WithNonBase64ActiveKey_ShouldThrowArgumentException(string activeKey)
    {
        var act = () => new HmacSha256TokenHasher(OptionsWithKey(activeKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName("options");
    }

    [Test]
    public void Constructor_WithKeyShorterThan32Bytes_ShouldThrowArgumentException()
    {
        var shortKey = Convert.ToBase64String(new byte[31]);
        var act = () => new HmacSha256TokenHasher(OptionsWithKey(shortKey));
        act.Should().Throw<ArgumentException>()
            .WithParameterName("options");
    }

    [Test]
    public void Constructor_WithExactly32ByteKey_ShouldSucceed()
    {
        var act = () => new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64(32)));
        act.Should().NotThrow();
    }

    [Test]
    public void Hash_WithNullPlainToken_ShouldThrowArgumentNullException()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var act = () => hasher.Hash(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("plainToken");
    }

    [TestCase("")]
    public void Hash_WithEmptyPlainToken_ShouldThrowArgumentException(string plain)
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var act = () => hasher.Hash(plain);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("plainToken");
    }

    [Test]
    public void Hash_ShouldReturn43CharBase64UrlWithoutPadding()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));

        var hash = hasher.Hash("cs_sess_someplaintexttoken");

        hash.Length.Should().Be(43);
        hash.Should().NotContain("=");
        hash.Should().NotContain("+");
        hash.Should().NotContain("/");
    }

    [Test]
    public void Hash_ShouldDecodeToExactly32Bytes()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));

        var hash = hasher.Hash("cs_sess_someplaintexttoken");

        var bytes = Base64Url.Decode(hash);
        bytes.Length.Should().Be(32);
    }

    [Test]
    public void Hash_WithSameKeyAndPlaintext_ShouldBeDeterministic()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));

        var first = hasher.Hash("cs_sess_plaintext");
        var second = hasher.Hash("cs_sess_plaintext");

        first.Should().Be(second);
    }

    [Test]
    public void Hash_WithDifferentKeys_ShouldProduceDifferentHashes()
    {
        var keyA = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var keyB = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)(i + 1)).ToArray());
        var hasherA = new HmacSha256TokenHasher(OptionsWithKey(keyA));
        var hasherB = new HmacSha256TokenHasher(OptionsWithKey(keyB));

        var hashA = hasherA.Hash("cs_sess_plaintext");
        var hashB = hasherB.Hash("cs_sess_plaintext");

        hashA.Should().NotBe(hashB);
    }

    [Test]
    public void Hash_ShouldMatchRawHmacSha256OverFullPlaintextUtf8Bytes()
    {
        var keyBytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var options = OptionsWithKey(Convert.ToBase64String(keyBytes));
        var hasher = new HmacSha256TokenHasher(options);
        var plainToken = "cs_sess_someplaintexttoken";

        var actual = hasher.Hash(plainToken);
        var raw = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(plainToken));
        var expected = Base64Url.Encode(raw);

        actual.Should().Be(expected);
    }

    [Test]
    public void Hash_ShouldHashFullPrefixedTokenNotStripPrefix()
    {
        var keyBytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        var options = OptionsWithKey(Convert.ToBase64String(keyBytes));
        var hasher = new HmacSha256TokenHasher(options);
        var plain = "cs_sess_someplaintexttoken";
        var plainWithoutPrefix = "someplaintexttoken";

        var withPrefix = hasher.Hash(plain);
        var withoutPrefix = hasher.Hash(plainWithoutPrefix);

        withPrefix.Should().NotBe(withoutPrefix);
    }

    [Test]
    public void Verify_WithCorrectHash_ShouldReturnTrue()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var plain = "cs_sess_plaintext";
        var expected = hasher.Hash(plain);

        hasher.Verify(plain, expected).Should().BeTrue();
    }

    [Test]
    public void Verify_WithWrongPlaintext_ShouldReturnFalse()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var expected = hasher.Hash("cs_sess_plaintext");

        hasher.Verify("cs_sess_different", expected).Should().BeFalse();
    }

    [Test]
    public void Verify_WithWrongHash_ShouldReturnFalse()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var plain = "cs_sess_plaintext";
        var correctHash = hasher.Hash(plain);
        var tamperedHash = correctHash.Length > 0
            ? correctHash[..^1] + (correctHash[^1] == 'A' ? 'B' : 'A')
            : "X";

        hasher.Verify(plain, tamperedHash).Should().BeFalse();
    }

    [Test]
    public void Verify_WithNonBase64UrlExpectedHash_ShouldReturnFalse()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));

        hasher.Verify("cs_sess_plaintext", "not!base64!url!").Should().BeFalse();
    }

    [Test]
    public void Verify_WithNullPlaintext_ShouldThrowArgumentNullException()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var act = () => hasher.Verify(null!, "anything");
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("plainToken");
    }

    [TestCase("")]
    public void Verify_WithEmptyPlaintext_ShouldThrowArgumentException(string plain)
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        var act = () => hasher.Verify(plain, "anything");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("plainToken");
    }

    [Test]
    public void Verify_WithEmptyExpectedHash_ShouldReturnFalse()
    {
        var hasher = new HmacSha256TokenHasher(OptionsWithKey(ValidKeyBase64()));
        hasher.Verify("cs_sess_plaintext", "").Should().BeFalse();
    }
}