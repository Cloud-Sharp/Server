using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Tokens;

[TestFixture]
public class TokenIssuerTests
{
    private sealed class FakeGenerator : ITokenGenerator
    {
        private readonly Func<TokenKind, string> _generate;
        public List<TokenKind> CalledWith { get; } = new();

        public FakeGenerator(Func<TokenKind, string> generate) => _generate = generate;

        public string Generate(TokenKind kind)
        {
            CalledWith.Add(kind);
            return _generate(kind);
        }
    }

    private sealed class FakeHasher : ITokenHasher
    {
        private readonly Func<string, string> _hash;
        public List<string> HashedInputs { get; } = new();

        public FakeHasher(Func<string, string> hash) => _hash = hash;

        public string Hash(string plainToken)
        {
            HashedInputs.Add(plainToken);
            return _hash(plainToken);
        }

        public bool Verify(string plainToken, string expectedHashedToken) => false;
    }

    [Test]
    public void Constructor_WithNullGenerator_ShouldThrowArgumentNullException()
    {
        var hasher = new FakeHasher(_ => "hash");
        var act = () => new TokenIssuer(null!, hasher);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("generator");
    }

    [Test]
    public void Constructor_WithNullHasher_ShouldThrowArgumentNullException()
    {
        var generator = new FakeGenerator(_ => "plain");
        var act = () => new TokenIssuer(generator, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("hasher");
    }

    [Test]
    public void Issue_ShouldReturnPlainTokenFromGeneratorAndHashFromHasher()
    {
        const string plain = "cs_sess_plaintexttoken";
        const string hash = "hashedvalue";
        var generator = new FakeGenerator(_ => plain);
        var hasher = new FakeHasher(_ => hash);
        var issuer = new TokenIssuer(generator, hasher);

        var issued = issuer.Issue(TokenKind.Session);

        issued.PlainToken.Should().Be(plain);
        issued.HashedToken.Should().Be(hash);
        generator.CalledWith.Should().Contain(TokenKind.Session);
        hasher.HashedInputs.Should().Contain(plain);
    }

    [Test]
    public void Issue_ShouldCallHasherWithExactPlainTokenFromGenerator()
    {
        const string plain = "cs_mcp_tokenvalue";
        var generator = new FakeGenerator(_ => plain);
        var hasher = new FakeHasher(_ => "anyhash");
        var issuer = new TokenIssuer(generator, hasher);

        issuer.Issue(TokenKind.McpCredential);

        hasher.HashedInputs.Should().ContainInOrder(new[] { plain });
    }

    [Test]
    public void Issue_WithUndefinedEnumValue_ShouldPropagateArgumentOutOfRangeException()
    {
        var generator = new FakeGenerator(_ => throw new ArgumentOutOfRangeException("kind"));
        var hasher = new FakeHasher(_ => "hash");
        var issuer = new TokenIssuer(generator, hasher);

        var act = () => issuer.Issue((TokenKind)999);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Issue_WithRealImplementations_ShouldProduceHashMatchingHashOfPlain()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        var generator = new RandomTokenGenerator();
        var hasher = new HmacSha256TokenHasher(new TokenHashingOptions { ActiveKey = key });
        var issuer = new TokenIssuer(generator, hasher);

        var issued = issuer.Issue(TokenKind.Session);
        var recomputed = hasher.Hash(issued.PlainToken);

        issued.HashedToken.Should().Be(recomputed);
        issued.PlainToken.Should().StartWith("cs_sess_");
    }
}