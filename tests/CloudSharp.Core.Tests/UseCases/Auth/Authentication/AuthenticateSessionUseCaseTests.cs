using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Authentication;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Auth.Authentication;

[TestFixture]
public class AuthenticateSessionUseCaseTests
{
    private const long UserId = 42L;
    private const string StoredHash = "stored-session-hash";

    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public FakeClock Clock { get; } = new(Now);

        public FakePersistenceSession PersistenceSession { get; } = new();

        public FakeUserRepository UserRepository { get; }

        public FakeSessionStore SessionStore { get; } = new();

        public FakeTransactionExecutor TransactionExecutor { get; }

        public Harness()
        {
            UserRepository = new FakeUserRepository(PersistenceSession);
            TransactionExecutor = new FakeTransactionExecutor(PersistenceSession);
        }

        public AuthenticateSessionUseCase UseCase =>
            new(UserRepository, SessionStore, TransactionExecutor, Clock);
    }

    [Test]
    public async Task ExecuteAsync_WithValidSession_ShouldReturnLatestUserAndIndependentSessionId()
    {
        var harness = new Harness();
        var user = SeededUser(harness, role: SystemRole.SystemAdmin, securityVersion: 5L);
        var session = SeededSession(user);
        harness.SessionStore.Seed(session);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.UserId, Is.EqualTo(user.Id));
            Assert.That(result.Value.SessionId, Is.EqualTo(session.SessionId));
            Assert.That(result.Value.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(result.Value.User.UserPublicId, Is.EqualTo(user.PublicId));
            Assert.That(result.Value.User.Email, Is.EqualTo("user@example.com"));
            Assert.That(result.Value.User.SystemRole, Is.EqualTo(SystemRole.SystemAdmin));
            Assert.That(result.Value.User.Status, Is.EqualTo(UserStatus.Active));
        });
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.SearchedTokenHashes,
                Is.EqualTo(new[] { StoredHash }));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { user.Id }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
            Assert.That(harness.UserRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldNotExtendSessionActivity()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        var session = SeededSession(user);
        harness.SessionStore.Seed(session);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        Assert.That(result.IsSuccess, Is.True);
        var persisted = harness.SessionStore.PersistedSessions[StoredHash];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.LastSeenAt, Is.EqualTo(session.LastSeenAt));
            Assert.That(persisted.IdleExpiresAt, Is.EqualTo(session.IdleExpiresAt));
            Assert.That(persisted.AbsoluteExpiresAt, Is.EqualTo(session.AbsoluteExpiresAt));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithIdleExpiredSession_ShouldReturnAuthSessionExpired()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        var session = SeededSession(user);
        harness.SessionStore.Seed(session);
        harness.Clock.SetUtcNow(session.IdleExpiresAt);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [Test]
    public async Task ExecuteAsync_WithAbsoluteExpiredSession_ShouldReturnAuthSessionExpired()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        var session = SeededSession(user);
        harness.SessionStore.Seed(session);
        harness.Clock.SetUtcNow(session.AbsoluteExpiresAt);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionExpired);
    }

    [TestCase(UserStatus.Suspended)]
    [TestCase(UserStatus.Deleted)]
    public async Task ExecuteAsync_WithInactiveUser_ShouldReturnAuthUserInactive(UserStatus status)
    {
        var harness = new Harness();
        var user = SeededUser(harness, status: status);
        var session = SeededSession(user);
        harness.SessionStore.Seed(session);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthUserInactive);
    }

    [Test]
    public async Task ExecuteAsync_WithSecurityVersionMismatch_ShouldReturnAuthSessionInvalid()
    {
        var harness = new Harness();
        var user = SeededUser(harness, securityVersion: 6L);
        var session = SeededSession(user, securityVersion: 5L);
        harness.SessionStore.Seed(session);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionInvalid);
    }

    [Test]
    public async Task ExecuteAsync_WithUserPublicIdMismatch_ShouldReturnAuthSessionInvalid()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        var session = SeededSession(user, userPublicId: Guid.CreateVersion7());
        harness.SessionStore.Seed(session);

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionInvalid);
    }

    [Test]
    public async Task ExecuteAsync_WithMissingSession_ShouldReturnAuthSessionInvalidWithoutUserLookup()
    {
        var harness = new Harness();

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [TestCase("")]
    [TestCase("cs_sess_raw-token-is-not-a-hash")]
    public async Task ExecuteAsync_WithInvalidTokenHash_ShouldReturnAuthSessionInvalidWithoutLookup(
        string tokenHash)
    {
        var harness = new Harness();

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(tokenHash));

        AssertErrorCode(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.SearchedTokenHashes, Is.Empty);
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSessionStoreFailure_ShouldReturnDependencyUnavailable()
    {
        var harness = new Harness();
        harness.SessionStore.FindByTokenHashFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WithUserLookupFailure_ShouldReturnDependencyUnavailable()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        harness.SessionStore.Seed(SeededSession(user));
        harness.UserRepository.FindByIdFailure = CommonError.DependencyUnavailable();

        var result = await harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash));

        AssertErrorCode(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteAsync_WithUnexpectedPortException_ShouldPropagate()
    {
        var harness = new Harness();
        var user = SeededUser(harness);
        harness.SessionStore.Seed(SeededSession(user));
        harness.UserRepository.FindByIdException = new InvalidOperationException("unexpected port failure");

        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(new AuthenticateSessionQuery(StoredHash)));
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(
                new AuthenticateSessionQuery(StoredHash),
                cancellationSource.Token));
        Assert.That(harness.SessionStore.SearchedTokenHashes, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_WithNullQuery_ShouldThrowArgumentNullException()
    {
        var harness = new Harness();

        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
    }

    [Test]
    public void Contracts_ShouldNotExposeTokenHashInToString()
    {
        var query = new AuthenticateSessionQuery(StoredHash);

        Assert.That(query.ToString(), Does.Not.Contain(StoredHash));
        Assert.That(query.ToString(), Does.Contain("[redacted]"));
    }

    private static User SeededUser(
        Harness harness,
        SystemRole role = SystemRole.User,
        UserStatus status = UserStatus.Active,
        long securityVersion = 5L,
        long version = 3L)
    {
        var passwordHash = new FakePasswordHasher().Hash("example-password");
        var user = User.Reconstitute(
            UserId,
            Guid.CreateVersion7(),
            EmailAddress.Create("user@example.com").Value,
            NormalizedName.Create("cloud-user").Value,
            "Cloud User",
            passwordHash,
            role,
            status,
            securityVersion,
            version,
            Now,
            Now,
            status == UserStatus.Suspended ? Now : null,
            status == UserStatus.Deleted ? Now : null);

        harness.UserRepository.FoundByIdUser = user;
        return user;
    }

    private static UserSession SeededSession(
        User user,
        long? securityVersion = null,
        Guid? userPublicId = null)
    {
        var tokenHash = TokenHash.Create(StoredHash).Value;
        return UserSession.Issue(
            user.Id,
            userPublicId ?? user.PublicId,
            tokenHash,
            securityVersion ?? user.SecurityVersion,
            Now);
    }

    private static void AssertErrorCode<T>(Result<T> result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(expectedCode));
    }
}
