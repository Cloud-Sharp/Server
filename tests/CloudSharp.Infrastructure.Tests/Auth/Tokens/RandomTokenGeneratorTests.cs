using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Tokens;

[TestFixture]
public class RandomTokenGeneratorTests
{
    private static readonly IReadOnlyDictionary<TokenKind, string> ExpectedPrefixes =
        new Dictionary<TokenKind, string>
        {
            { TokenKind.Session, "cs_sess_" },
            { TokenKind.McpCredential, "cs_mcp_" },
            { TokenKind.ShareLink, "cs_share_" },
            { TokenKind.SpaceInvite, "cs_inv_" },
            { TokenKind.DownloadGrant, "cs_dl_" },
        };

    private static IEnumerable<object[]> PrefixCases() =>
        ExpectedPrefixes.Select(kv => new object[] { kv.Key, kv.Value });

    [TestCaseSource(nameof(PrefixCases))]
    public void Generate_WithDefinedKind_ShouldStartWithExpectedPrefix(
        TokenKind kind, string expectedPrefix)
    {
        var generator = new RandomTokenGenerator();

        var token = generator.Generate(kind);

        token.Should().StartWith(expectedPrefix);
    }

    [Test]
    public void Generate_BodyAfterPrefix_ShouldBeBase64UrlWithoutPadding(
        [Values(
            TokenKind.Session,
            TokenKind.McpCredential,
            TokenKind.ShareLink,
            TokenKind.SpaceInvite,
            TokenKind.DownloadGrant)]
        TokenKind kind)
    {
        var generator = new RandomTokenGenerator();

        var token = generator.Generate(kind);
        var prefix = ExpectedPrefixes[kind];
        var body = token[prefix.Length..];

        body.Should().NotContain("=");
        body.Should().NotContain("+");
        body.Should().NotContain("/");
        body.Length.Should().Be(43);
    }

    [Test]
    public void Generate_BodyAfterPrefix_ShouldDecodeToExactly32Bytes(
        [Values(
            TokenKind.Session,
            TokenKind.McpCredential,
            TokenKind.ShareLink,
            TokenKind.SpaceInvite,
            TokenKind.DownloadGrant)]
        TokenKind kind)
    {
        var generator = new RandomTokenGenerator();

        var token = generator.Generate(kind);
        var prefix = ExpectedPrefixes[kind];
        var body = token[prefix.Length..];

        var bytes = Base64Url.Decode(body);
        bytes.Length.Should().Be(32);
    }

    [Test]
    public void Generate_CalledTwice_ShouldProduceDifferentTokens(
        [Values(
            TokenKind.Session,
            TokenKind.McpCredential,
            TokenKind.ShareLink,
            TokenKind.SpaceInvite,
            TokenKind.DownloadGrant)]
        TokenKind kind)
    {
        var generator = new RandomTokenGenerator();

        var first = generator.Generate(kind);
        var second = generator.Generate(kind);

        first.Should().NotBe(second);
    }

    [Test]
    public void Generate_WithUndefinedEnumValue_ShouldThrowArgumentOutOfRangeException()
    {
        var generator = new RandomTokenGenerator();
        var invalidKind = (TokenKind)999;

        var act = () => generator.Generate(invalidKind);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("kind");
    }
}