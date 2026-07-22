using CloudSharp.Infrastructure.Auth.Passwords;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Passwords;

[TestFixture]
public class Pbkdf2PasswordHasherTests
{
    private const string LegacyCompatibleHash =
        "AQAAAAEAACcQAAAAEAABAgMEBQYHCAkKCwwNDg/Z+V9lwt+dKF0miCMAylvinj7VAFVmY4NcTGLicFFQIg==";

    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Test]
    public void Hash_ShouldCreateSaltedEncodedValue()
    {
        const string password = "correct horse battery staple";

        var first = _hasher.Hash(password);
        var second = _hasher.Hash(password);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.EqualTo(password));
            Assert.That(second, Is.Not.EqualTo(password));
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(Convert.FromBase64String(first)[0], Is.EqualTo(0x01));
        });
    }

    [Test]
    public void Verify_ShouldAcceptMatchingPassword()
    {
        const string password = "correct horse battery staple";
        var hash = _hasher.Hash(password);

        var verified = _hasher.Verify(hash, password);

        Assert.That(verified, Is.True);
    }

    [Test]
    public void Verify_ShouldRejectDifferentPassword()
    {
        var hash = _hasher.Hash("correct password");

        var verified = _hasher.Verify(hash, "wrong password");

        Assert.That(verified, Is.False);
    }

    [Test]
    public void Verify_ShouldAcceptHashFromPreviousFormat()
    {
        var verified = _hasher.Verify(LegacyCompatibleHash, "correct horse battery staple");

        Assert.That(verified, Is.True);
    }

    [TestCase("")]
    [TestCase("not-base64")]
    [TestCase("AQ==")]
    public void Verify_ShouldRejectMalformedHash(string malformedHash)
    {
        var verified = _hasher.Verify(malformedHash, "password");

        Assert.That(verified, Is.False);
    }
}
