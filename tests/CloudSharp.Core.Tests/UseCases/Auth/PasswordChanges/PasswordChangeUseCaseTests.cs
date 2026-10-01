using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Auth.PasswordChanges;

[TestFixture]
public class PasswordChangeUseCaseTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private const long UserId = 42L;

    private const string CurrentSessionTokenHash = "hmac-current-session-value";

    private const string OtherSessionTokenHash = "hmac-other-session-value";

    private sealed class Harness
    {
        public Harness()
        {
            Clock = new FakeClock(InitialNow);
            UserRepository = new FakeUserRepository();
            Account = ActiveAccount();
            UserRepository.FoundByIdUser = Account;
            CurrentSession = Session(Account, CurrentSessionTokenHash, Account.SecurityVersion);
            OtherSession = Session(
                Account, OtherSessionTokenHash, Account.SecurityVersion, issuedAt: InitialNow.AddHours(-1));
            SessionStore = new FakeSessionStore();
            SessionStore.Seed(CurrentSession);
            SessionStore.Seed(OtherSession);
        }

        public FakeClock Clock { get; }

        public FakePasswordHasher PasswordHasher { get; } = new();

        public FakeUserRepository UserRepository { get; }

        public FakeSessionStore SessionStore { get; }

        public FakeTransactionExecutor TransactionExecutor { get; } = new();

        public User Account { get; set; }

        public UserSession CurrentSession { get; set; }

        public UserSession OtherSession { get; }

        public PasswordChangeUseCase UseCase =>
            new(UserRepository, SessionStore, PasswordHasher, TransactionExecutor, Clock);

        public PasswordChangeCommand ValidCommand(
            string? currentPassword = null,
            string? newPassword = null,
            long? expectedVersion = null)
            => TestPasswordChangeCommands.For(Account, CurrentSession, currentPassword, newPassword, expectedVersion);
    }

    [Test]
    public async Task ExecuteAsync_ShouldChangePasswordHashAndFinalizeSessions()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand();
        var originalHash = harness.Account.PasswordHash;
        var expectedNewHash = new FakePasswordHasher().Hash(command.NewPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Version, Is.EqualTo(2L));
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.PasswordHash, Is.EqualTo(expectedNewHash));
            Assert.That(persisted.PasswordHash, Is.Not.EqualTo(originalHash));
            Assert.That(persisted.SecurityVersion, Is.EqualTo(2L));
            Assert.That(persisted.Version, Is.EqualTo(2L));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(InitialNow));
        });

        var current = harness.SessionStore.PersistedSessions[CurrentSessionTokenHash];
        Assert.Multiple(() =>
        {
            Assert.That(current.SecurityVersion, Is.EqualTo(2L));
            Assert.That(current.SessionId, Is.EqualTo(harness.CurrentSession.SessionId));
            Assert.That(current.TokenHash.Value, Is.EqualTo(CurrentSessionTokenHash));
            Assert.That(current.IssuedAt, Is.EqualTo(InitialNow));
            Assert.That(current.LastSeenAt, Is.EqualTo(InitialNow));
            Assert.That(current.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(24)));
            Assert.That(current.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7)));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(OtherSessionTokenHash), Is.False);
        });

        var finalize = harness.SessionStore.FinalizePasswordChangeCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(finalize.NewSecurityVersion, Is.EqualTo(2L));
            Assert.That(finalize.Now, Is.EqualTo(InitialNow));
            Assert.That(finalize.CurrentSession.SessionId, Is.EqualTo(harness.CurrentSession.SessionId));
        });

        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.VerifiedInputs,
                Is.EqualTo(new[] { TestPasswordChangeCommands.DefaultCurrentPassword }));
            Assert.That(harness.PasswordHasher.HashedInputs,
                Is.EqualTo(new[] { TestPasswordChangeCommands.DefaultNewPassword }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.SessionStore.RemovedSessions, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldForwardSubmittedPasswordsWithoutTrimming()
    {
        // Arrange
        var paddedCurrentPassword = "  current-password  ";
        var paddedNewPassword = "  new-strong-password  ";
        var harness = new Harness();
        UseAccount(harness, ActiveAccount(password: paddedCurrentPassword));
        var command = harness.ValidCommand(
            currentPassword: paddedCurrentPassword,
            newPassword: paddedNewPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.EqualTo(new[] { paddedCurrentPassword }));
            Assert.That(harness.PasswordHasher.HashedInputs, Is.EqualTo(new[] { paddedNewPassword }));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithNewPasswordEqualToCurrent_ShouldStillReplaceHashAndBumpVersions()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand(newPassword: TestPasswordChangeCommands.DefaultCurrentPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].PasswordHash,
                Is.EqualTo(harness.Account.PasswordHash));
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].Version, Is.EqualTo(2L));
            Assert.That(harness.PasswordHasher.HashedInputs,
                Is.EqualTo(new[] { TestPasswordChangeCommands.DefaultCurrentPassword }));
            Assert.That(harness.SessionStore.PersistedSessions[CurrentSessionTokenHash].SecurityVersion,
                Is.EqualTo(2L));
        });
    }

    private static IEnumerable<TestCaseData> BoundaryPasswordCases()
    {
        yield return new TestCaseData(
                new string('c', 128), TestPasswordChangeCommands.DefaultNewPassword)
            .SetName("Current password has 128 characters");
        yield return new TestCaseData(
                TestPasswordChangeCommands.DefaultCurrentPassword, "12345678")
            .SetName("New password has 8 characters");
        yield return new TestCaseData(
                TestPasswordChangeCommands.DefaultCurrentPassword, new string('n', 128))
            .SetName("New password has 128 characters");
    }

    [TestCaseSource(nameof(BoundaryPasswordCases))]
    public async Task ExecuteAsync_WithBoundaryLengthPasswords_ShouldChangePassword(
        string currentPassword,
        string newPassword)
    {
        // Arrange
        var harness = new Harness();
        UseAccount(harness, ActiveAccount(password: currentPassword));
        var command = harness.ValidCommand(currentPassword: currentPassword, newPassword: newPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Version, Is.EqualTo(2L));
    }

    private static IEnumerable<TestCaseData> InvalidInputCases()
    {
        const string current = TestPasswordChangeCommands.DefaultCurrentPassword;
        const string newPassword = TestPasswordChangeCommands.DefaultNewPassword;
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, null!, newPassword))
            .SetName("Current password is null");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, "", newPassword))
            .SetName("Current password is empty");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, "   ", newPassword))
            .SetName("Current password is whitespace only");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, new string('c', 129), newPassword))
            .SetName("Current password exceeds 128 characters");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, current, null!))
            .SetName("New password is null");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, current, ""))
            .SetName("New password is empty");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, current, "   "))
            .SetName("New password is whitespace only");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, current, "short"))
            .SetName("New password is shorter than 8 characters");
        yield return new TestCaseData(
                new PasswordChangeCommand(UserId, AnySessionId(), "any-token-hash", 1L, current, new string('n', 129)))
            .SetName("New password exceeds 128 characters");
    }

    [TestCaseSource(nameof(InvalidInputCases))]
    public async Task ExecuteAsync_WithInvalidInput_ShouldReturnPasswordInvalidWithoutLookup(
        PasswordChangeCommand command)
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertAllErrorCodes(result, ErrorCodes.User.PasswordInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.SearchedTokenHashes, Is.Empty);
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.PasswordHasher.HashedInputs, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithRawTokenHash_ShouldReturnTokenHashInvalidWithoutLookup()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand() with { TokenHash = "cs_raw_token" };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.TokenHashInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.SearchedTokenHashes, Is.Empty);
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
        });
    }

    [Test]
    public async Task PasswordChangeContracts_ShouldRedactSecretsInToString()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(command.ToString(),
                Does.Not.Contain(TestPasswordChangeCommands.DefaultCurrentPassword));
            Assert.That(command.ToString(), Does.Not.Contain(TestPasswordChangeCommands.DefaultNewPassword));
            Assert.That(command.ToString(), Does.Not.Contain(CurrentSessionTokenHash));
            Assert.That(command.ToString(), Does.Contain("[redacted]"));
        });
    }

    [Test]
    public void PasswordChangeContracts_ShouldExposeOnlyVersionInResult()
    {
        // Arrange & Act & Assert
        Assert.That(
            typeof(PasswordChangeCommand).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[]
            {
                "UserId", "SessionId", "TokenHash", "ExpectedVersion", "CurrentPassword", "NewPassword",
            }));
        Assert.That(
            typeof(PasswordChangeResultDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "Version" }));
    }

    [Test]
    public async Task ExecuteAsync_WithMissingSession_ShouldReturnAuthSessionInvalidWithoutUserLookup()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand() with { TokenHash = "hmac-not-seeded" };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithMismatchedUserId_ShouldReturnAuthSessionInvalidWithoutUserLookup()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand() with { UserId = UserId + 1 };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WithMismatchedSessionId_ShouldReturnAuthSessionInvalidWithoutUserLookup()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand() with { SessionId = Guid.CreateVersion7() };

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_WithSessionLookupFailure_ShouldReturnDependencyUnavailable()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FindByTokenHashFailure = CommonError.DependencyUnavailable();
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithMissingUser_ShouldReturnAuthSessionInvalid()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand();
        harness.UserRepository.FoundByIdUser = null;

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    private static IEnumerable<TestCaseData> InactiveUserCases()
    {
        yield return new TestCaseData(UserStatus.Suspended).SetName("User is suspended");
        yield return new TestCaseData(UserStatus.Deleted).SetName("User is deleted");
    }

    [TestCaseSource(nameof(InactiveUserCases))]
    public async Task ExecuteAsync_WithInactiveUser_ShouldReturnAuthUserInactive(UserStatus status)
    {
        // Arrange
        var harness = new Harness();
        UseAccount(harness, ActiveAccount(status: status));
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthUserInactive);
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.UserRepository.SavedByIdUsers, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> ExpiredSessionCases()
    {
        yield return new TestCaseData(InitialNow.AddHours(-25))
            .SetName("Session idle timeout has passed");
        yield return new TestCaseData(InitialNow.AddDays(-8))
            .SetName("Session absolute timeout has passed");
    }

    [TestCaseSource(nameof(ExpiredSessionCases))]
    public async Task ExecuteAsync_WithExpiredSession_ShouldReturnAuthSessionExpired(
        DateTimeOffset currentSessionIssuedAt)
    {
        // Arrange
        var harness = new Harness();
        ReseedCurrentSession(harness, currentSessionIssuedAt);
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionExpired);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithStaleSessionSecurityVersion_ShouldReturnAuthSessionInvalid()
    {
        // Arrange
        var harness = new Harness();
        UseAccount(harness, ActiveAccount(securityVersion: 2L));
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Session.AuthSessionInvalid);
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithStaleExpectedVersion_ShouldReturnPreconditionFailedWithoutVerifyingPassword()
    {
        // Arrange
        var harness = new Harness();
        var command = harness.ValidCommand(expectedVersion: 99L);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.PreconditionFailed);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.UserRepository.SavedByIdUsers, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithWrongCurrentPassword_ShouldReturnPasswordMismatchWithoutChangingState()
    {
        // Arrange
        var harness = new Harness();
        var originalHash = harness.Account.PasswordHash;
        var command = harness.ValidCommand(currentPassword: "wrong-password");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.User.PasswordMismatch);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.EqualTo(new[] { "wrong-password" }));
            Assert.That(harness.PasswordHasher.HashedInputs, Is.Empty);
            Assert.That(persisted.PasswordHash, Is.EqualTo(originalHash));
            Assert.That(persisted.SecurityVersion, Is.EqualTo(1L));
            Assert.That(persisted.Version, Is.EqualTo(1L));
            Assert.That(harness.UserRepository.SavedByIdUsers, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.SessionStore.PersistedSessions[CurrentSessionTokenHash].SecurityVersion,
                Is.EqualTo(1L));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithUserLookupFailureInsideTransaction_ShouldReturnDependencyUnavailable()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FindByIdFailure = CommonError.DependencyUnavailable();
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithPersistenceConflict_ShouldReturnPreconditionFailedWithoutFinalizingSessions()
    {
        // Arrange
        var harness = new Harness();
        var originalHash = harness.Account.PasswordHash;
        harness.UserRepository.BeforeSaveById = user =>
            harness.UserRepository.BumpStoredVersion(user.Id);
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.PreconditionFailed);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.PasswordHash, Is.EqualTo(originalHash));
            Assert.That(persisted.SecurityVersion, Is.EqualTo(1L));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.SessionStore.RemovedSessions, Is.Empty);
            Assert.That(harness.SessionStore.PersistedSessions[CurrentSessionTokenHash].SecurityVersion,
                Is.EqualTo(1L));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithPersistenceFailure_ShouldReturnDependencyUnavailableWithoutFinalizingSessions()
    {
        // Arrange
        var harness = new Harness();
        var originalHash = harness.Account.PasswordHash;
        harness.UserRepository.SaveByIdErrorFactory = (_, call) =>
            call == 1 ? CommonError.DependencyUnavailable() : null;
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].PasswordHash,
                Is.EqualTo(originalHash));
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.SessionStore.RemovedSessions, Is.Empty);
            Assert.That(harness.SessionStore.PersistedSessions, Has.Count.EqualTo(2));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithFinalizationFailure_ShouldDeleteCurrentSessionAndReturnDependencyUnavailable()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FinalizePasswordChangeFailure =
            Result.Fail(CommonError.DependencyUnavailable());
        var command = harness.ValidCommand();
        var expectedNewHash = new FakePasswordHasher().Hash(command.NewPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].PasswordHash,
                Is.EqualTo(expectedNewHash));
            Assert.That(harness.SessionStore.RemovedSessions, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.RemovedSessions[0].TokenHash.Value,
                Is.EqualTo(CurrentSessionTokenHash));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(CurrentSessionTokenHash), Is.False);
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(OtherSessionTokenHash), Is.True);
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId }));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithFinalizationAndDeletionFailure_ShouldCompensateSecurityVersionInDatabase()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FinalizePasswordChangeFailure =
            Result.Fail(CommonError.DependencyUnavailable());
        harness.SessionStore.RemoveFailure = Result.Fail(CommonError.DependencyUnavailable());
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SecurityVersion, Is.EqualTo(3L));
            Assert.That(persisted.Version, Is.EqualTo(3L));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId, UserId }));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(2));
            Assert.That(harness.SessionStore.RemovedSessions, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(CurrentSessionTokenHash), Is.True);
            Assert.That(harness.SessionStore.PersistedSessions[CurrentSessionTokenHash].SecurityVersion,
                Is.EqualTo(1L));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(2));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithCompensationFailure_ShouldKeepChangedPasswordAndReturnDependencyUnavailable()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FinalizePasswordChangeFailure =
            Result.Fail(CommonError.DependencyUnavailable());
        harness.SessionStore.RemoveFailure = Result.Fail(CommonError.DependencyUnavailable());
        harness.UserRepository.SaveByIdErrorFactory = (_, call) =>
            call >= 2 ? CommonError.DependencyUnavailable() : null;
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SecurityVersion, Is.EqualTo(2L));
            Assert.That(persisted.Version, Is.EqualTo(2L));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId, UserId }));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(2));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithCompensationConflict_ShouldRetryWithFreshLookup()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FinalizePasswordChangeFailure =
            Result.Fail(CommonError.DependencyUnavailable());
        harness.SessionStore.RemoveFailure = Result.Fail(CommonError.DependencyUnavailable());
        harness.UserRepository.BeforeSaveById = user =>
        {
            if (harness.UserRepository.SavedByIdUsers.Count == 2)
            {
                harness.UserRepository.BumpStoredVersion(user.Id);
            }
        };
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SecurityVersion, Is.EqualTo(3L));
            Assert.That(persisted.Version, Is.EqualTo(4L));
            Assert.That(harness.UserRepository.SearchedIds,
                Is.EqualTo(new[] { UserId, UserId, UserId }));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(3));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(2));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSecurityVersionAlreadyAdvanced_ShouldNotRevokeAgain()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.FinalizePasswordChangeFailure =
            Result.Fail(CommonError.DependencyUnavailable());
        harness.SessionStore.RemoveFailure = Result.Fail(CommonError.DependencyUnavailable());
        var advanced = false;
        harness.TransactionExecutor.OnCommitted = () =>
        {
            if (advanced)
            {
                return;
            }

            advanced = true;
            harness.UserRepository.BumpStoredSecurityVersion(UserId);
        };
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSingleError(result, ErrorCodes.Common.DependencyUnavailable);
        var persisted = harness.UserRepository.PersistedUsersById[UserId];
        Assert.Multiple(() =>
        {
            Assert.That(persisted.SecurityVersion, Is.EqualTo(3L));
            Assert.That(persisted.Version, Is.EqualTo(3L));
            Assert.That(harness.UserRepository.SavedByIdUsers, Has.Count.EqualTo(1));
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { UserId, UserId }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(2));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ExecuteAsync_WithCanceledToken_ShouldPropagateCancellationWithoutChanges()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var command = harness.ValidCommand();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(command, cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.SearchedTokenHashes, Is.Empty);
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.UserRepository.SavedByIdUsers, Is.Empty);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Is.Empty);
            Assert.That(harness.SessionStore.PersistedSessions, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithCancellationAfterCommit_ShouldFinalizeSessionsIndependentlyAndSucceed()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        harness.TransactionExecutor.OnCommitted = cancellationSource.Cancel;
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command, cancellationSource.Token);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Version, Is.EqualTo(2L));
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.PersistedSessions[CurrentSessionTokenHash].SecurityVersion,
                Is.EqualTo(2L));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(OtherSessionTokenHash), Is.False);
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSessionDeletedBeforeFinalization_ShouldNotRecreateSession()
    {
        // Arrange
        var harness = new Harness();
        harness.TransactionExecutor.OnCommitted = () =>
            harness.SessionStore
                .RemoveAsync(harness.CurrentSession, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Version, Is.EqualTo(2L));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(CurrentSessionTokenHash), Is.False);
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(OtherSessionTokenHash), Is.False);
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls[0].NewSecurityVersion,
                Is.EqualTo(2L));
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSessionExpiredBeforeFinalization_ShouldNotRecreateOrUpgradeSession()
    {
        // Arrange
        var harness = new Harness();
        harness.TransactionExecutor.OnCommitted = () => harness.Clock.Advance(TimeSpan.FromDays(8));
        var command = harness.ValidCommand();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var current = harness.SessionStore.PersistedSessions[CurrentSessionTokenHash];
        Assert.Multiple(() =>
        {
            Assert.That(harness.SessionStore.FinalizePasswordChangeCalls[0].Now,
                Is.EqualTo(InitialNow.AddDays(8)));
            Assert.That(current.SecurityVersion, Is.EqualTo(1L));
            Assert.That(current.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(24)));
            Assert.That(current.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7)));
            Assert.That(harness.SessionStore.PersistedSessions.ContainsKey(OtherSessionTokenHash), Is.False);
            Assert.That(harness.UserRepository.PersistedUsersById[UserId].SecurityVersion, Is.EqualTo(2L));
        });
    }

    [Test]
    public void ExecuteAsync_WithNullCommand_ShouldThrowArgumentNullException()
    {
        // Arrange
        var harness = new Harness();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(() => harness.UseCase.ExecuteAsync(null!));
    }

    private static Guid AnySessionId() => Guid.CreateVersion7();

    private static void UseAccount(Harness harness, User account)
    {
        harness.Account = account;
        harness.UserRepository.FoundByIdUser = account;
    }

    private static void ReseedCurrentSession(Harness harness, DateTimeOffset issuedAt)
    {
        harness.CurrentSession = Session(harness.Account, CurrentSessionTokenHash, harness.Account.SecurityVersion, issuedAt);
        harness.SessionStore.Seed(harness.CurrentSession);
    }

    private static User ActiveAccount(
        string password = TestPasswordChangeCommands.DefaultCurrentPassword,
        UserStatus status = UserStatus.Active,
        long securityVersion = 1L,
        long version = 1L)
    {
        var passwordHash = new FakePasswordHasher().Hash(password);
        return User.Reconstitute(
            UserId,
            Guid.CreateVersion7(),
            EmailAddress.Create("user@example.com").Value,
            NormalizedName.Create("cloud-user").Value,
            "Cloud User",
            passwordHash,
            SystemRole.User,
            status,
            securityVersion,
            version,
            InitialNow,
            InitialNow,
            status == UserStatus.Suspended ? InitialNow : null,
            status == UserStatus.Deleted ? InitialNow : null);
    }

    private static UserSession Session(
        User owner,
        string tokenHash,
        long securityVersion,
        DateTimeOffset? issuedAt = null)
    {
        var issued = issuedAt ?? InitialNow;
        return UserSession.Reconstitute(
            Guid.CreateVersion7(),
            owner.Id,
            owner.PublicId,
            TokenHash.Create(tokenHash).Value,
            securityVersion,
            issued,
            issued,
            issued.AddHours(24),
            issued.AddDays(7));
    }

    private static void AssertSingleError(Result<PasswordChangeResultDto> result, string expectedErrorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.That(
            result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey],
            Is.EqualTo(expectedErrorCode));
    }

    private static void AssertAllErrorCodes(Result<PasswordChangeResultDto> result, string expectedErrorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(
            result.Errors.Select(error => error.Metadata[CloudSharpError.ErrorCodeMetadataKey]),
            Is.All.EqualTo(expectedErrorCode));
    }
}