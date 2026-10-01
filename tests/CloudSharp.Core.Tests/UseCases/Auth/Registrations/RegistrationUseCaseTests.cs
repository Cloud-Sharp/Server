using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Registrations;
using CloudSharp.Core.UseCases.Auth.Sessions;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Auth.Registrations;

[TestFixture]
public class RegistrationUseCaseTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness()
        {
            Clock = new FakeClock(InitialNow);
        }

        public FakeClock Clock { get; }

        public FakePasswordHasher PasswordHasher { get; } = new();

        public FakeTokenIssuer TokenIssuer { get; } = new();

        public FakeUserRepository UserRepository { get; } = new();

        public FakeSessionStore SessionStore { get; } = new();

        public FakeTransactionExecutor TransactionExecutor { get; } = new();

        public RegistrationUseCase UseCase =>
            new(UserRepository, PasswordHasher,
                new SessionIssuanceService(TokenIssuer, SessionStore, Clock), TransactionExecutor, Clock);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnRegistrationResult()
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(
            email: "  User@Example.com  ",
            userName: " cloud-user ",
            displayName: "Cloud User",
            password: "example-password");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var user = result.Value.User;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.AccessToken, Is.EqualTo(harness.TokenIssuer.IssuedTokens[0].PlainToken));
            Assert.That(result.Value.TokenType, Is.EqualTo("Bearer"));
            Assert.That(result.Value.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(24)));
            Assert.That(result.Value.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7)));
            Assert.That(user.UserPublicId, Is.EqualTo(harness.UserRepository.AddedUsers[0].PublicId));
            Assert.That(user.Email, Is.EqualTo("User@Example.com"));
            Assert.That(user.UserName, Is.EqualTo("cloud-user"));
            Assert.That(user.DisplayName, Is.EqualTo("Cloud User"));
            Assert.That(user.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(user.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(user.CreatedAt, Is.EqualTo(InitialNow));
            Assert.That(user.UpdatedAt, Is.EqualTo(InitialNow));
        });

        var session = harness.SessionStore.StoredSessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(session.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.UserId, Is.EqualTo(harness.UserRepository.AssignedId));
            Assert.That(session.UserPublicId, Is.EqualTo(user.UserPublicId));
            Assert.That(session.SecurityVersion, Is.EqualTo(1L));
            Assert.That(session.TokenHash.Value, Is.EqualTo(harness.TokenIssuer.IssuedTokens[0].HashedToken));
        });

        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.CheckedNormalizedEmails,
                Is.EqualTo(new[] { "USER@EXAMPLE.COM" }));
            Assert.That(harness.UserRepository.CheckedNormalizedUserNames,
                Is.EqualTo(new[] { "CLOUD-USER" }));
            Assert.That(harness.UserRepository.AddedUsers, Has.Count.EqualTo(1));
            Assert.That(harness.UserRepository.AddedUsers[0].PasswordHash,
                Is.Not.EqualTo("example-password"));
            Assert.That(harness.UserRepository.AddedUsers[0].Version, Is.EqualTo(1L));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldIssueSessionWithFreshClockReading()
    {
        // Arrange
        var harness = new Harness();
        harness.TransactionExecutor.OnCommitted = () => harness.Clock.Advance(TimeSpan.FromHours(1));
        var command = TestRegistrationCommands.Valid();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(25)));
            Assert.That(result.Value.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7).AddHours(1)));
            Assert.That(result.Value.User.CreatedAt, Is.EqualTo(InitialNow));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRegisterWithoutOptionalFields()
    {
        // Arrange
        var harness = new Harness();
        var command = new RegistrationCommand("user@example.com", null, null, "example-password");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.User.UserName, Is.Null);
            Assert.That(result.Value.User.DisplayName, Is.Null);
            Assert.That(harness.UserRepository.CheckedNormalizedUserNames, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldStoreNullForBlankDisplayName()
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(displayName: "   ");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.User.DisplayName, Is.Null);
    }

    [Test]
    public async Task ExecuteAsync_ShouldHashPasswordWithoutTrimming()
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(password: "  ValidPass1  ");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.HashedInputs, Is.EqualTo(new[] { "  ValidPass1  " }));
            Assert.That(harness.UserRepository.AddedUsers[0].PasswordHash, Does.Not.Contain("ValidPass1"));
        });
    }

    private static IEnumerable<TestCaseData> PasswordsViolatingPolicy()
    {
        yield return new TestCaseData(new string('p', 7)).SetName("Password is 7 characters");
        yield return new TestCaseData(new string('p', 129)).SetName("Password is 129 characters");
        yield return new TestCaseData(null).SetName("Password is null");
        yield return new TestCaseData("").SetName("Password is empty");
        yield return new TestCaseData(new string(' ', 8)).SetName("Password is whitespace only");
    }

    [TestCaseSource(nameof(PasswordsViolatingPolicy))]
    public async Task ExecuteAsync_ShouldRejectPasswordViolatingPolicy(string? password)
    {
        // Arrange
        var harness = new Harness();
        var command = CreateCommand(password);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.User.PasswordInvalid));
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.HashedInputs, Is.Empty);
            Assert.That(harness.UserRepository.CheckedNormalizedEmails, Is.Empty);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> PasswordsAtPolicyBounds()
    {
        yield return new TestCaseData(new string('p', 8)).SetName("Password is 8 characters");
        yield return new TestCaseData(new string('p', 128)).SetName("Password is 128 characters");
    }

    [TestCaseSource(nameof(PasswordsAtPolicyBounds))]
    public async Task ExecuteAsync_ShouldRegisterWithPasswordAtPolicyBounds(string password)
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(password: password);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    private static IEnumerable<TestCaseData> ProfileFieldsViolatingDomainRules()
    {
        yield return new TestCaseData(
                TestRegistrationCommands.Valid(email: "not-an-email"),
                ErrorCodes.User.InvalidEmail)
            .SetName("Email format is invalid");
        yield return new TestCaseData(
                TestRegistrationCommands.Valid(userName: new string('n', 31)),
                ErrorCodes.User.NameInvalid)
            .SetName("User name is too long");
        yield return new TestCaseData(
                TestRegistrationCommands.Valid(displayName: new string('d', 101)),
                ErrorCodes.User.DisplayNameInvalid)
            .SetName("Display name is too long");
    }

    [TestCaseSource(nameof(ProfileFieldsViolatingDomainRules))]
    public async Task ExecuteAsync_ShouldFailWithDomainValidationError(
        RegistrationCommand command,
        string expectedErrorCode)
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(expectedErrorCode));
        Assert.Multiple(() =>
        {
            Assert.That(harness.PasswordHasher.HashedInputs, Has.Count.EqualTo(1));
            Assert.That(harness.UserRepository.CheckedNormalizedEmails, Is.Empty);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmailConflict()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.EmailExists = true;

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.User.EmailConflict));
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnNameConflict()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.UserNameExists = true;

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.User.NameConflict));
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmailConflictBeforeNameConflict()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.EmailExists = true;
        harness.UserRepository.UserNameExists = true;

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.User.EmailConflict));
        Assert.That(harness.UserRepository.CheckedNormalizedUserNames, Is.Empty);
    }

    private static IEnumerable<TestCaseData> SaveConflictErrors()
    {
        yield return new TestCaseData(UserError.EmailConflict())
            .SetName("Save conflicts on email uniqueness");
        yield return new TestCaseData(UserError.NameConflict())
            .SetName("Save conflicts on user name uniqueness");
    }

    [TestCaseSource(nameof(SaveConflictErrors))]
    public async Task ExecuteAsync_ShouldReturnConflictReportedBySave(Error conflictError)
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.SaveResultFactory =
            _ => Result.Fail<User>(conflictError);

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result),
            Is.EqualTo(conflictError.Metadata[CloudSharpError.ErrorCodeMetadataKey]));
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.AddedUsers, Has.Count.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> DependencyFailureModes()
    {
        yield return new TestCaseData(
                new Action<FakeUserRepository>(
                    repository => repository.ExistenceCheckFailure = CommonError.DependencyUnavailable()))
            .SetName("Existence check fails");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(
                    repository => repository.AddResult = Result.Fail(CommonError.DependencyUnavailable())))
            .SetName("Add fails");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(
                    repository => repository.SaveResultFactory =
                        _ => Result.Fail<User>(CommonError.DependencyUnavailable())))
            .SetName("Save fails");
    }

    [TestCaseSource(nameof(DependencyFailureModes))]
    public async Task ExecuteAsync_ShouldReturnDependencyUnavailable(
        Action<FakeUserRepository> arrangeFailure)
    {
        // Arrange
        var harness = new Harness();
        arrangeFailure(harness.UserRepository);

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> SavedUserContractViolations()
    {
        yield return new TestCaseData(
                new Func<User, Result<User>>(added => ReconstituteUser(added, 0L, added.PublicId)))
            .SetName("Save returns user without generated id");
        yield return new TestCaseData(
                new Func<User, Result<User>>(added => ReconstituteUser(added, 42L, Guid.NewGuid())))
            .SetName("Save returns user with mismatched identity");
    }

    [TestCaseSource(nameof(SavedUserContractViolations))]
    public async Task ExecuteAsync_ShouldThrowForSavedUserContractViolation(
        Func<User, Result<User>> saveResult)
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.SaveResultFactory = saveResult;

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid()));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSessionStoreFailure_ShouldFailAndKeepCommittedAccount()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.StoreFailure = Result.Fail(CommonError.DependencyUnavailable());

        // Act
        var result = await harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid());

        // Assert
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
            Assert.That(harness.UserRepository.AddedUsers, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TokenIssuer.IssuedTokens, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteAsync_WithUnexpectedPortException_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.SaveResultFactory =
            _ => throw new InvalidOperationException("unexpected port failure");

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(TestRegistrationCommands.Valid()));
        Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(
                TestRegistrationCommands.Valid(), cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public void ExecuteAsync_WithCancellationAfterCommit_ShouldNotIssueTokenAndKeepCommittedAccount()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        harness.TransactionExecutor.OnCommitted = cancellationSource.Cancel;

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(
                TestRegistrationCommands.Valid(), cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
            Assert.That(harness.UserRepository.AddedUsers, Has.Count.EqualTo(1));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldNotExposePlainTextPasswordOrTokenToPersistence()
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(password: "super-secret-password");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.AddedUsers[0].PasswordHash,
                Does.Not.Contain("super-secret-password"));
            Assert.That(harness.UserRepository.CheckedNormalizedEmails[0],
                Does.Not.Contain("super-secret-password"));
            Assert.That(harness.UserRepository.CheckedNormalizedUserNames[0],
                Does.Not.Contain("super-secret-password"));
            Assert.That(harness.SessionStore.StoredSessions[0].TokenHash.Value,
                Does.Not.Contain(result.Value.AccessToken));
        });
    }

    [Test]
    public void RegistrationContracts_ShouldExcludeInternalIdentifiersAndSecrets()
    {
        // Arrange & Act & Assert
        Assert.That(
            typeof(RegistrationCommand).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "Email", "UserName", "DisplayName", "Password" }));
        Assert.That(
            typeof(RegistrationResultDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "AccessToken", "TokenType", "IdleExpiresAt", "AbsoluteExpiresAt", "User" }));
        Assert.That(
            typeof(UserDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[]
            {
                "UserPublicId", "Email", "UserName", "DisplayName",
                "SystemRole", "Status", "CreatedAt", "UpdatedAt",
            }));
    }

    [Test]
    public async Task RegistrationContracts_ShouldRedactSecretsInToString()
    {
        // Arrange
        var harness = new Harness();
        var command = TestRegistrationCommands.Valid(password: "super-secret-password");

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(command.ToString(), Does.Not.Contain("super-secret-password"));
            Assert.That(command.ToString(), Does.Contain("[redacted]"));
            Assert.That(result.Value.ToString(), Does.Not.Contain(result.Value.AccessToken));
            Assert.That(result.Value.ToString(), Does.Contain("[redacted]"));
        });
    }

    private static RegistrationCommand CreateCommand(string? password) =>
        new("user@example.com", "cloud-user", "Cloud User", password!);

    private static User ReconstituteUser(User template, long id, Guid publicId) =>
        User.Reconstitute(
            id,
            publicId,
            template.Email,
            template.UserName,
            template.DisplayName,
            template.PasswordHash,
            template.SystemRole,
            template.Status,
            template.SecurityVersion,
            template.Version,
            template.CreatedAt,
            template.UpdatedAt,
            template.SuspendedAt,
            template.DeletedAt);

    private static string FirstErrorCode<T>(Result<T> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
