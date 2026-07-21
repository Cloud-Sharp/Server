using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Auth.Tokens;

[TestFixture]
public class TokenServiceRegistrationTests
{
    private static IConfiguration ConfigWithKey(string base64Key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TokenHashing:ActiveKey"] = base64Key,
            })
            .Build();

    private static string ValidKey(int bytes = 32) =>
        Convert.ToBase64String(Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());

    [Test]
    public void AddTokenHashing_WithValidKey_ShouldResolveAllSingletonServices()
    {
        var services = new ServiceCollection();
        services.AddTokenHashing(ConfigWithKey(ValidKey()));
        var provider = services.BuildServiceProvider();

        var generator = provider.GetRequiredService<ITokenGenerator>();
        var hasher = provider.GetRequiredService<ITokenHasher>();
        var issuer = provider.GetRequiredService<ITokenIssuer>();

        generator.Should().NotBeNull();
        hasher.Should().NotBeNull();
        issuer.Should().NotBeNull();
        generator.Should().BeSameAs(provider.GetRequiredService<ITokenGenerator>());
        hasher.Should().BeSameAs(provider.GetRequiredService<ITokenHasher>());
        issuer.Should().BeSameAs(provider.GetRequiredService<ITokenIssuer>());
    }

    [Test]
    public void AddTokenHashing_WithMissingKey_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var act = () => services.AddTokenHashing(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AddTokenHashing_WithShortKey_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();
        var shortKey = Convert.ToBase64String(new byte[31]);

        var act = () => services.AddTokenHashing(ConfigWithKey(shortKey));

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AddTokenHashing_WithNonBase64Key_ShouldThrowInvalidOperationException()
    {
        var services = new ServiceCollection();

        var act = () => services.AddTokenHashing(ConfigWithKey("not!base64!"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void AddTokenHashing_ErrorMessage_ShouldNotContainKeyValue()
    {
        var services = new ServiceCollection();
        var sensitiveKeyBytes = Enumerable.Range(0, 10).Select(i => (byte)(i + 100)).ToArray();
        var sensitiveKey = Convert.ToBase64String(sensitiveKeyBytes);
        var config = ConfigWithKey(sensitiveKey);

        var act = () => services.AddTokenHashing(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TokenHashing:ActiveKey*")
            .Which.Message.Should().NotContain(sensitiveKey);
    }

    [Test]
    public void AddTokenHashing_ResolvedIssuer_ShouldProduceConsistentHashForIssuedPlain()
    {
        var services = new ServiceCollection();
        services.AddTokenHashing(ConfigWithKey(ValidKey()));
        var provider = services.BuildServiceProvider();
        var issuer = provider.GetRequiredService<ITokenIssuer>();
        var hasher = provider.GetRequiredService<ITokenHasher>();

        var issued = issuer.Issue(TokenKind.Session);
        var recomputed = hasher.Hash(issued.PlainToken);

        issued.HashedToken.Should().Be(recomputed);
    }
}