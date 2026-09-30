using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Users;

[TestFixture]
public class EmailAddressTests
{
    private const string ValidEmail = "user@example.com";

    [Test]
    public void Create_WithValidEmail_ShouldTrimAndStore()
    {
        var result = EmailAddress.Create($"  {ValidEmail}  ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo(ValidEmail));
        Assert.That(result.Value.Normalized, Is.EqualTo(ValidEmail.ToUpperInvariant()));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithBlank_ShouldFailWithInvalidEmail(string? email)
    {
        var result = EmailAddress.Create(email!);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidEmail);
    }

    [Test]
    public void Create_WithEmailOver320_ShouldFailWithInvalidEmail()
    {
        var tooLong = new string('a', 321);

        var result = EmailAddress.Create(tooLong);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidEmail);
    }

    [Test]
    public void Create_WithoutAtSign_ShouldFailWithInvalidEmail()
    {
        var result = EmailAddress.Create("notanemail");

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.InvalidEmail);
    }

    [Test]
    public void Create_WithExactly320Chars_ShouldSucceed()
    {
        var local = new string('a', 64);
        var domain = new string('b', 320 - local.Length - 1);
        var email = $"{local}@{domain}";

        Assume.That(email.Length, Is.EqualTo(320));

        var result = EmailAddress.Create(email);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Reconstitute_WithMismatchedNormalized_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(
            () => EmailAddress.Reconstitute(ValidEmail, "DIFFERENT@EXAMPLE.COM"));
    }

    [Test]
    public void Reconstitute_WithEmptyValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => EmailAddress.Reconstitute(string.Empty, string.Empty));
    }

    [Test]
    public void Reconstitute_WithTooLongValue_ShouldThrow()
    {
        var tooLong = new string('a', 321);
        Assert.Throws<ArgumentException>(() => EmailAddress.Reconstitute(tooLong, tooLong.ToUpperInvariant()));
    }

    [Test]
    public void Reconstitute_WithMatchingNormalized_ShouldSucceed()
    {
        var email = EmailAddress.Reconstitute(ValidEmail, ValidEmail.ToUpperInvariant());

        Assert.That(email.Value, Is.EqualTo(ValidEmail));
        Assert.That(email.Normalized, Is.EqualTo(ValidEmail.ToUpperInvariant()));
    }

    [Test]
    public void SecurityRegression_EmailDoesNotLeakIntoErrorMetadata()
    {
        var sensitive = "leak-" + new string('x', 330) + "@example.com";

        Assume.That(sensitive.Length, Is.GreaterThan(320));

        var result = EmailAddress.Create(sensitive);

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