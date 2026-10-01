using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Auth.Sessions;

[TestFixture]
public class SessionIssuanceServiceTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(InitialNow);

        public FakeTokenIssuer TokenIssuer { get; } = new();

        public FakeSessionStore SessionStore { get; } = new();

        public SessionIssuanceService Service => new(TokenIssuer, SessionStore, Clock);
    }

    [Test]
    public async Task IssueAsync_ShouldStoreSessionAndReturnTokenWithExpirationTimes()
    {
        // Arrange
        var harness = new Harness();
        var user = RegisteredUser();
        var service = harness.Service;
        harness.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var result = await service.IssueAsync(user);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.TokenIssuer.IssuedTokens, Has.Count.EqualTo(1));
        Assert.That(harness.SessionStore.StoredSessions, Has.Count.EqualTo(1));
        var token = harness.TokenIssuer.IssuedTokens[0];
        var session = harness.SessionStore.StoredSessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(token.PlainToken, Does.StartWith(TokenPrefixes.Get(TokenKind.Session)));
            Assert.That(session.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.UserId, Is.EqualTo(user.Id));
            Assert.That(session.UserPublicId, Is.EqualTo(user.PublicId));
            Assert.That(session.SecurityVersion, Is.EqualTo(user.SecurityVersion));
            Assert.That(session.IssuedAt, Is.EqualTo(InitialNow.AddHours(1)));
            Assert.That(session.TokenHash.Value, Is.EqualTo(token.HashedToken));
            Assert.That(session.TokenHash.Value, Does.Not.Contain(token.PlainToken));
            Assert.That(result.Value.AccessToken, Is.EqualTo(token.PlainToken));
            Assert.That(result.Value.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(25)));
            Assert.That(result.Value.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7).AddHours(1)));
            Assert.That(result.Value.IdleExpiresAt, Is.EqualTo(session.IdleExpiresAt));
            Assert.That(result.Value.AbsoluteExpiresAt, Is.EqualTo(session.AbsoluteExpiresAt));
        });
    }

    [Test]
    public void IssueAsync_WithUnpersistedUser_ShouldThrowBeforeIssuingToken()
    {
        // Arrange
        var harness = new Harness();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(() => harness.Service.IssueAsync(RegisteredUser(id: 0)));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public void IssueAsync_WithNullUser_ShouldThrowBeforeIssuingToken()
    {
        // Arrange
        var harness = new Harness();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => harness.Service.IssueAsync(null!));
        Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
        Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
    }

    [Test]
    public void IssueAsync_WithCanceledRequest_ShouldNotIssueTokenOrStoreSession()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act & Assert
        var exception = Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.Service.IssueAsync(RegisteredUser(), cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("cs_session_plain-token")]
    public async Task IssueAsync_WithInvalidHash_ShouldForwardValidationErrorsWithoutStoring(string? hash)
    {
        // Arrange
        var harness = new Harness();
        var issuer = new StubTokenIssuer(_ => new IssuedToken("cs_session_plain-token", hash!));
        var service = new SessionIssuanceService(issuer, harness.SessionStore, harness.Clock);
        var expected = TokenHash.Create(hash);

        // Act
        var result = await service.IssueAsync(RegisteredUser());

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Select(error => error.Message),
                Is.EqualTo(expected.Errors.Select(error => error.Message)));
            Assert.That(result.Errors.Select(error => error.Metadata[CloudSharpError.ErrorCodeMetadataKey]),
                Is.EqualTo(expected.Errors.Select(error => error.Metadata[CloudSharpError.ErrorCodeMetadataKey])));
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public async Task IssueAsync_WithStoreFailure_ShouldForwardOriginalErrors()
    {
        // Arrange
        var harness = new Harness();
        var failure = Result.Fail(CommonError.DependencyUnavailable());
        harness.SessionStore.StoreFailure = failure;

        // Act
        var result = await harness.Service.IssueAsync(RegisteredUser());

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors[0], Is.SameAs(failure.Errors[0]));
            Assert.That(harness.TokenIssuer.IssuedTokens, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public async Task IssueAsync_ShouldWaitForStorageAndForwardCancellationToken()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        var completion = new TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken receivedToken = default;
        var store = new StubSessionStore((_, ct) =>
        {
            receivedToken = ct;
            return completion.Task;
        });
        var service = new SessionIssuanceService(harness.TokenIssuer, store, harness.Clock);

        // Act
        var issuance = service.IssueAsync(RegisteredUser(), cancellationSource.Token);
        var completedBeforeStorage = issuance.IsCompleted;
        completion.SetResult(Result.Ok());
        var result = await issuance;

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(completedBeforeStorage, Is.False);
            Assert.That(receivedToken, Is.EqualTo(cancellationSource.Token));
            Assert.That(result.IsSuccess, Is.True);
        });
    }

    [Test]
    public void IssueAsync_WithUnexpectedIssuerException_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        var expected = new InvalidOperationException("unexpected issuer failure");
        var issuer = new StubTokenIssuer(_ => throw expected);
        var service = new SessionIssuanceService(issuer, harness.SessionStore, harness.Clock);

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(RegisteredUser()));
        Assert.That(exception, Is.SameAs(expected));
        Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
    }

    [Test]
    public void IssueAsync_WithUnexpectedStoreException_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        var expected = new InvalidOperationException("unexpected store failure");
        var store = new StubSessionStore((_, _) => Task.FromException<Result>(expected));
        var service = new SessionIssuanceService(harness.TokenIssuer, store, harness.Clock);

        // Act & Assert
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(RegisteredUser()));
        Assert.That(exception, Is.SameAs(expected));
    }

    [Test]
    public void IssueAsync_WithCancellationDuringStorage_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        var store = new StubSessionStore((_, ct) =>
        {
            cancellationSource.Cancel();
            return Task.FromCanceled<Result>(ct);
        });
        var service = new SessionIssuanceService(harness.TokenIssuer, store, harness.Clock);

        // Act & Assert
        var exception = Assert.ThrowsAsync<TaskCanceledException>(
            () => service.IssueAsync(RegisteredUser(), cancellationSource.Token));
        Assert.That(exception!.CancellationToken, Is.EqualTo(cancellationSource.Token));
    }

    [Test]
    public async Task SessionIssuanceResult_ShouldRedactAccessTokenInToString()
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.Service.IssueAsync(RegisteredUser());

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.ToString(), Does.Not.Contain(result.Value.AccessToken));
            Assert.That(result.Value.ToString(), Does.Contain("[redacted]"));
        });
    }

    private static User RegisteredUser(long id = 42) =>
        User.Reconstitute(
            id,
            Guid.NewGuid(),
            EmailAddress.Create("user@example.com").Value,
            null,
            null,
            "stored-password-hash",
            SystemRole.User,
            UserStatus.Active,
            securityVersion: 5,
            version: 1,
            createdAt: InitialNow,
            updatedAt: InitialNow,
            suspendedAt: null,
            deletedAt: null);

    private sealed class StubTokenIssuer(Func<TokenKind, IssuedToken> issue) : ITokenIssuer
    {
        public IssuedToken Issue(TokenKind kind) => issue(kind);
    }

    private sealed class StubSessionStore(
        Func<UserSession, CancellationToken, Task<Result>> store) : ISessionStore
    {
        public Task<Result> StoreAsync(UserSession session, CancellationToken cancellationToken = default) =>
            store(session, cancellationToken);

        public Task<Result<UserSession?>> FindByTokenHashAsync(
            TokenHash tokenHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> RemoveAsync(UserSession session, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<bool>> FinalizePasswordChangeAsync(
            UserSession currentSession,
            long newSecurityVersion,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
