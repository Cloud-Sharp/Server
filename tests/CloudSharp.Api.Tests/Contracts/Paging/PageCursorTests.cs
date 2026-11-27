using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Paging;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Contracts.Paging;

[TestFixture]
public sealed class PageCursorTests
{
    private const string Position = "reader-position-token";
    private const string Route = "/api/v2/spaces/{spacePublicId}/memberships";
    private static readonly Guid SpacePublicId = Guid.Parse("01977b58-32aa-7c60-a17e-0840c6df1200");
    private static readonly CurrentActor Actor = CurrentActor.ForSession(
        42,
        Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327845"),
        SystemRole.User,
        Guid.Parse("01977b5f-779e-742c-ad25-9870a44724f7"));
    private static readonly CurrentActor OtherActor = CurrentActor.ForSession(
        43,
        Guid.Parse("01977b5e-1700-7f20-bdd7-55d41f327846"),
        SystemRole.User,
        Guid.Parse("01977b5f-779e-742c-ad25-9870a44724f8"));

    [TestCase(1)]
    [TestCase(100)]
    public void TryOpen_ShouldReturnSealedPositionForSameRequestRegardlessOfLimit(int nextLimit)
    {
        var request = new TestPageRequest();
        var cursor = PageCursor.Seal(CreateContext(), request, Position);

        var opened = PageCursor.TryOpen(
            CreateContext(),
            request with { Cursor = cursor, Limit = nextLimit },
            cursor,
            out var position);

        Assert.Multiple(() =>
        {
            Assert.That(cursor, Does.Match("^[A-Za-z0-9_-]+$"));
            Assert.That(opened, Is.True);
            Assert.That(position, Is.EqualTo(Position));
        });
    }

    [Test]
    public void TryOpen_ShouldAcceptRouteUuidInAnotherCase()
    {
        var request = new TestPageRequest();
        var cursor = PageCursor.Seal(CreateContext(), request, Position);

        var opened = PageCursor.TryOpen(
            CreateContext(spacePublicId: SpacePublicId.ToString("D").ToUpperInvariant()),
            request,
            cursor,
            out var position);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(position, Is.EqualTo(Position));
        });
    }

    [Test]
    public void TryOpen_ShouldSucceedWithoutPositionWhenCursorIsMissing()
    {
        var opened = PageCursor.TryOpen(CreateContext(), new TestPageRequest(), null, out var position);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.True);
            Assert.That(position, Is.Null);
        });
    }

    [Test]
    public void Seal_ShouldReturnNullWithoutNextPosition()
    {
        Assert.That(PageCursor.Seal(CreateContext(), new TestPageRequest(), null), Is.Null);
    }

    [TestCase("actor")]
    [TestCase("anonymous")]
    [TestCase("space")]
    [TestCase("route")]
    [TestCase("method")]
    [TestCase("sort")]
    [TestCase("direction")]
    public void TryOpen_ShouldRejectCursorFromDifferentRequestBinding(string changed)
    {
        var request = new TestPageRequest();
        var cursor = PageCursor.Seal(CreateContext(), request, Position);
        var context = changed switch
        {
            "actor" => CreateContext(actor: OtherActor),
            "anonymous" => CreateContext(anonymous: true),
            "space" => CreateContext(spacePublicId: Guid.CreateVersion7().ToString()),
            "route" => CreateContext(route: "/api/v2/spaces/{spacePublicId}/invites"),
            "method" => CreateContext(method: HttpMethods.Post),
            _ => CreateContext(),
        };
        var nextRequest = changed switch
        {
            "sort" => request with { Sort = "updatedAt" },
            "direction" => request with { Direction = "ASC" },
            _ => request,
        };

        var opened = PageCursor.TryOpen(context, nextRequest, cursor, out var position);

        Assert.Multiple(() =>
        {
            Assert.That(opened, Is.False);
            Assert.That(position, Is.Null);
        });
    }

    [TestCase("")]
    [TestCase("not-a-cursor")]
    [TestCase("e30")]
    public void TryOpen_ShouldRejectMalformedCursor(string cursor)
    {
        Assert.That(PageCursor.TryOpen(CreateContext(), new TestPageRequest(), cursor, out _), Is.False);
    }

    [Test]
    public void TryOpen_ShouldRejectReplacedPosition()
    {
        var request = new TestPageRequest();
        var cursor = PageCursor.Seal(CreateContext(), request, Position)!;
        var envelope = JsonNode.Parse(Base64Url.DecodeFromChars(cursor))!.AsObject();
        envelope["p"] = "other-position-token";
        var replaced = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(envelope.ToJsonString()));

        Assert.That(PageCursor.TryOpen(CreateContext(), request, replaced, out _), Is.False);
    }

    private static DefaultHttpContext CreateContext(
        CurrentActor? actor = null,
        bool anonymous = false,
        string? spacePublicId = null,
        string route = Route,
        string method = "GET")
    {
        var accessor = new CurrentActorAccessor();
        if (!anonymous)
        {
            accessor.Set(actor ?? Actor, "session-hash-value");
        }

        var services = new ServiceCollection();
        services.AddSingleton<ICurrentActorAccessor>(accessor);
        services.AddSingleton<ITokenHasher, Sha256TokenHasher>();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = method;
        context.Request.RouteValues["spacePublicId"] = spacePublicId ?? SpacePublicId.ToString("D");
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(route),
            0,
            EndpointMetadataCollection.Empty,
            "page-cursor-test"));
        return context;
    }

    private sealed record TestPageRequest(
        string? Cursor = null,
        int Limit = 50,
        string Sort = "joinedAt",
        string Direction = "DESC");

    private sealed class Sha256TokenHasher : ITokenHasher
    {
        public string Hash(string plainToken) =>
            Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(plainToken)));

        public bool Verify(string plainToken, string expectedHashedToken) =>
            string.Equals(Hash(plainToken), expectedHashedToken, StringComparison.Ordinal);
    }
}
