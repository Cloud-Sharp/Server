using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Tests.TestSupport;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Auth.Tokens;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace CloudSharp.Api.Tests.Auth.Authentication;

[TestFixture]
public class SessionAuthenticationHandlerTests
{
    private const string SessionToken = "cs_sess_valid-session-token";
    private const string McpToken = "cs_mcp_valid-mcp-token";

    private static readonly HmacSha256TokenHasher TokenHasher = new(
        new TokenHashingOptions
        {
            ActiveKey = Convert.ToBase64String(new byte[32]),
        });

    [Test]
    public async Task AuthenticateAsync_WithValidSession_ShouldCreateSessionPrincipalAndAccessor()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser(role: SystemRole.SystemAdmin);
        var hash = TokenHasher.Hash(SessionToken);
        var session = harness.SeedUserSession(user, TokenHash.Create(hash).Value);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var accessor = new CurrentActorAccessor();
        var handler = CreateHandler(harness, accessor);

        await handler.InitializeAsync(Scheme(), context);
        var result = await handler.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        var principal = result.Principal!;
        principal.FindFirst(SessionClaimTypes.UserId)!.Value.Should().Be(user.Id.ToString());
        principal.FindFirst(SessionClaimTypes.UserPublicId)!.Value.Should().Be(user.PublicId.ToString("D"));
        principal.FindFirst(SessionClaimTypes.SessionId)!.Value.Should().Be(session.SessionId.ToString("D"));
        principal.FindFirst(SessionClaimTypes.ActorType)!.Value.Should().Be("SESSION");
        principal.FindFirst(SessionClaimTypes.SystemRole)!.Value.Should().Be("SYSTEM_ADMIN");

        accessor.CurrentActor.Should().NotBeNull();
        accessor.CurrentActor!.ActorType.Should().Be(CloudSharp.Core.Common.Actors.ActorType.Session);
        accessor.CurrentActor.UserId.Should().Be(user.Id);
        accessor.CurrentActor.UserPublicId.Should().Be(user.PublicId);
        accessor.CurrentActor.SystemRole.Should().Be(SystemRole.SystemAdmin);
        accessor.CurrentActor.SessionId.Should().Be(session.SessionId);
        accessor.SessionTokenHash.Should().Be(hash);

        principal.Claims.Should().NotContain(claim => claim.Value.Contains(hash, StringComparison.Ordinal));
        principal.Claims.Should().NotContain(claim => claim.Value.Contains(SessionToken, StringComparison.Ordinal));
        harness.SessionStore.PersistedSessions[hash].LastSeenAt.Should().Be(session.LastSeenAt);
    }

    [Test]
    public async Task AuthenticateAsync_WithoutAuthorizationHeader_ShouldChallengeWithTokenRequired()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        var result = await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        result.None.Should().BeTrue();
        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenRequired);
        context.Response.Headers["WWW-Authenticate"].ToString().Should().Be("Bearer");
    }

    [TestCase("Basic abc")]
    [TestCase("Bearer")]
    [TestCase("Bearer ")]
    [TestCase("bearer-without-space")]
    public async Task AuthenticateAsync_WithMalformedBearerHeader_ShouldChallengeWithTokenInvalid(
        string header)
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = header;
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        var result = await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        result.Succeeded.Should().BeFalse();
        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task AuthenticateAsync_WithMultipleAuthorizationHeaders_ShouldChallengeWithTokenInvalid()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = new[] { $"Bearer {SessionToken}", $"Bearer {SessionToken}" };
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task AuthenticateAsync_WithOversizedToken_ShouldChallengeWithTokenInvalid()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization =
            "Bearer " + TokenPrefixes.Get(TokenKind.Session) + new string('a', 600);
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task AuthenticateAsync_WithMcpToken_ShouldChallengeWithTokenTypeNotAllowed()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {McpToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(
            context,
            HttpStatusCode.Unauthorized,
            ErrorCodes.Auth.TokenTypeNotAllowed);
        harness.SessionStore.SearchedTokenHashes.Should().BeEmpty();
    }

    [TestCase("cs_share_capability-token")]
    [TestCase("cs_dl_capability-token")]
    [TestCase("cs_inv_capability-token")]
    [TestCase("not-a-token")]
    public async Task AuthenticateAsync_WithCapabilityOrUnknownToken_ShouldChallengeWithTokenInvalid(
        string token)
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
        harness.SessionStore.SearchedTokenHashes.Should().BeEmpty();
    }

    [Test]
    public async Task AuthenticateAsync_WithMissingSession_ShouldChallengeWithTokenInvalid()
    {
        var harness = new ApiTestHarness();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
    }

    private static IEnumerable<TestCaseData> InvalidSessionScenarios()
    {
        yield return new TestCaseData(
                new Action<ApiTestHarness, User>(static (harness, user) =>
                    harness.UserRepository.FoundByIdUser = null))
            .SetName("User is missing");
        yield return new TestCaseData(
                new Action<ApiTestHarness, User>(static (harness, user) =>
                    harness.SeedUser(status: UserStatus.Suspended)))
            .SetName("User is suspended");
        yield return new TestCaseData(
                new Action<ApiTestHarness, User>(static (harness, user) =>
                    harness.UserRepository.BumpStoredSecurityVersion(user.Id)))
            .SetName("SecurityVersion mismatch");
    }

    [TestCaseSource(nameof(InvalidSessionScenarios))]
    public async Task AuthenticateAsync_WithInvalidSessionState_ShouldChallengeWithGenericTokenInvalid(
        Action<ApiTestHarness, User> arrange)
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var hash = TokenHasher.Hash(SessionToken);
        harness.SeedUserSession(user, TokenHash.Create(hash).Value);
        arrange(harness, user);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        var result = await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        result.Succeeded.Should().BeFalse();
        var error = await ReadErrorAsync(context);
        error.Error.Code.Should().Be(ErrorCodes.Auth.TokenInvalid);
        error.Error.Message.Should().Be("The authentication token is invalid.");
        error.Error.Message.Should().NotContain(ErrorCodes.Session.AuthSessionExpired);
        error.Error.Message.Should().NotContain(ErrorCodes.Session.AuthUserInactive);
        error.Error.Message.Should().NotContain(ErrorCodes.Session.AuthSessionInvalid);
    }

    [Test]
    public async Task AuthenticateAsync_WithExpiredSession_ShouldChallengeWithGenericTokenInvalid()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var hash = TokenHasher.Hash(SessionToken);
        var session = harness.SeedUserSession(user, TokenHash.Create(hash).Value);
        harness.Clock.SetUtcNow(session.IdleExpiresAt);
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(context, HttpStatusCode.Unauthorized, ErrorCodes.Auth.TokenInvalid);
    }

    [Test]
    public async Task AuthenticateAsync_WithSessionStoreFailure_ShouldChallengeWith503()
    {
        var harness = new ApiTestHarness();
        harness.SessionStore.FindByTokenHashFailure = CommonError.DependencyUnavailable();
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        await handler.InitializeAsync(Scheme(), context);
        await handler.AuthenticateAsync();
        await handler.ChallengeAsync(new AuthenticationProperties());

        await AssertChallengeAsync(
            context,
            HttpStatusCode.ServiceUnavailable,
            ErrorCodes.Common.DependencyUnavailable);
    }

    [Test]
    public void AuthenticateAsync_WithUnexpectedException_ShouldPropagate()
    {
        var harness = new ApiTestHarness();
        var user = harness.SeedUser();
        var hash = TokenHasher.Hash(SessionToken);
        harness.SeedUserSession(user, TokenHash.Create(hash).Value);
        harness.UserRepository.FindByIdException = new InvalidOperationException("unexpected failure");
        var context = ApiTestHarness.CreateHttpContext();
        context.Request.Headers.Authorization = $"Bearer {SessionToken}";
        var handler = CreateHandler(harness, new CurrentActorAccessor());

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await handler.InitializeAsync(Scheme(), context);
            await handler.AuthenticateAsync();
        });
    }

    private static SessionAuthenticationHandler CreateHandler(
        ApiTestHarness harness,
        ICurrentActorAccessor accessor)
    {
        var loggerFactory = LoggerFactory.Create(_ => { });
        return new SessionAuthenticationHandler(
            new StaticOptionsMonitor<AuthenticationSchemeOptions>(new AuthenticationSchemeOptions()),
            loggerFactory,
            UrlEncoder.Default,
            harness.AuthenticateSessionUseCase,
            TokenHasher,
            accessor);
    }

    private static AuthenticationScheme Scheme() =>
        new(SessionAuthenticationDefaults.Scheme, null, typeof(SessionAuthenticationHandler));

    private static async Task AssertChallengeAsync(
        HttpContext context,
        HttpStatusCode statusCode,
        string code)
    {
        context.Response.StatusCode.Should().Be((int)statusCode);
        var error = await ReadErrorAsync(context);
        error.Error.Code.Should().Be(code);
        error.RequestId.Should().Be(context.TraceIdentifier);
    }

    private static async Task<ErrorResponse> ReadErrorAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var error = JsonSerializer.Deserialize<ErrorResponse>(body, ApiTestHarness.CamelCaseJson);
        error.Should().NotBeNull();
        return error!;
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;

        public StaticOptionsMonitor(T value)
        {
            _value = value;
        }

        public T CurrentValue => _value;

        public T Get(string? name) => _value;

        public IDisposable OnChange(Action<T, string?> listener) => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
