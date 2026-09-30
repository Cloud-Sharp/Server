using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Users;

[TestFixture]
public class NormalizedNameTests
{
    private const string ValidName = "alice";

    [Test]
    public void Create_WithValidName_ShouldTrimAndStore()
    {
        var result = NormalizedName.Create($"  {ValidName}  ");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo(ValidName));
        Assert.That(result.Value.Normalized, Is.EqualTo(ValidName.ToUpperInvariant()));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WithBlank_ShouldFailWithNameInvalid(string? name)
    {
        var result = NormalizedName.Create(name!);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.NameInvalid);
    }

    [Test]
    public void Create_WithOver30Chars_ShouldFailWithNameInvalid()
    {
        var tooLong = new string('a', 31);

        var result = NormalizedName.Create(tooLong);

        Assert.That(result.IsFailed, Is.True);
        AssertError(result, ErrorCodes.User.NameInvalid);
    }

    [Test]
    public void Create_WithExactly30Chars_ShouldSucceed()
    {
        var maxName = new string('a', 30);

        var result = NormalizedName.Create(maxName);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Value, Is.EqualTo(maxName));
    }

    [Test]
    public void Reconstitute_WithMismatchedNormalized_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => NormalizedName.Reconstitute(ValidName, "DIFFERENT"));
    }

    [Test]
    public void Reconstitute_WithEmptyValue_ShouldThrow()
    {
        Assert.Throws<ArgumentException>(() => NormalizedName.Reconstitute(string.Empty, string.Empty));
    }

    [Test]
    public void Reconstitute_WithMatchingNormalized_ShouldSucceed()
    {
        var name = NormalizedName.Reconstitute(ValidName, ValidName.ToUpperInvariant());

        Assert.That(name.Value, Is.EqualTo(ValidName));
        Assert.That(name.Normalized, Is.EqualTo(ValidName.ToUpperInvariant()));
    }

    private static void AssertError<T>(Result<T> result, string expectedCode)
    {
        var hasCode = result.Errors.Any(e =>
            e.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string s && s == expectedCode);
        Assert.That(hasCode, Is.True, $"Expected error code {expectedCode} in metadata.");
    }
}