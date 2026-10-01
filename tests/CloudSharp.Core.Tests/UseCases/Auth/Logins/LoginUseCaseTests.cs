using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Sessions;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Auth.Logins;

[TestFixture]
public class LoginUseCaseTests
{
    private static readonly DateTimeOffset InitialNow = new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private const string RegisteredEmail = "user@example.com";

    private const string RegisteredPassword = "example-password";

    private sealed class Harness
    {
        public Harness()
        {
            Clock = new FakeClock(InitialNow);
            UserRepository = new FakeUserRepository { FoundUser = RegisteredAccount() };
        }

        public FakeClock Clock { get; }

        public FakePasswordHasher PasswordHasher { get; } = new();

        public FakeTokenIssuer TokenIssuer { get; } = new();

        public FakeUserRepository UserRepository { get; }

        public FakeSessionStore SessionStore { get; } = new();

        public FakeTransactionExecutor TransactionExecutor { get; } = new();

        public LoginUseCase UseCase =>
            new(UserRepository, SessionStore, PasswordHasher, TokenIssuer, TransactionExecutor, Clock);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnLoginResult()
    {
        // Arrange
        var harness = new Harness();
        var account = harness.UserRepository.FoundUser!;
        var command = TestLoginCommands.Valid(
            loginId: "  User@Example.com  ",
            password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var login = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(login.AccessToken, Is.EqualTo(harness.TokenIssuer.IssuedTokens[0].PlainToken));
            Assert.That(login.TokenType, Is.EqualTo("Bearer"));
            Assert.That(login.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(24)));
            Assert.That(login.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7)));
            Assert.That(login.User.UserPublicId, Is.EqualTo(account.PublicId));
            Assert.That(login.User.Email, Is.EqualTo(RegisteredEmail));
            Assert.That(login.User.UserName, Is.EqualTo("cloud-user"));
            Assert.That(login.User.DisplayName, Is.EqualTo("Cloud User"));
            Assert.That(login.User.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(login.User.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(login.User.CreatedAt, Is.EqualTo(InitialNow));
            Assert.That(login.User.UpdatedAt, Is.EqualTo(InitialNow));
        });

        var session = harness.SessionStore.StoredSessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(session.SessionId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(session.UserId, Is.EqualTo(account.Id));
            Assert.That(session.UserPublicId, Is.EqualTo(account.PublicId));
            Assert.That(session.SecurityVersion, Is.EqualTo(account.SecurityVersion));
            Assert.That(session.IssuedAt, Is.EqualTo(InitialNow));
            Assert.That(session.TokenHash.Value, Is.EqualTo(harness.TokenIssuer.IssuedTokens[0].HashedToken));
        });

        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedNormalizedEmails,
                Is.EqualTo(new[] { "USER@EXAMPLE.COM" }));
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.PasswordHasher.HashedInputs, Is.Empty);
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
        var command = TestLoginCommands.Valid(
            loginId: RegisteredEmail,
            password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var session = harness.SessionStore.StoredSessions[0];
        Assert.Multiple(() =>
        {
            Assert.That(session.IssuedAt, Is.EqualTo(InitialNow.AddHours(1)));
            Assert.That(result.Value.IdleExpiresAt, Is.EqualTo(InitialNow.AddHours(25)));
            Assert.That(result.Value.AbsoluteExpiresAt, Is.EqualTo(InitialNow.AddDays(7).AddHours(1)));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldIssueSessionWithUserSecurityVersion()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FoundUser = RegisteredAccount(securityVersion: 5L);
        var command = TestLoginCommands.Valid(
            loginId: RegisteredEmail,
            password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.SessionStore.StoredSessions[0].SecurityVersion, Is.EqualTo(5L));
    }

    [Test]
    public async Task ExecuteAsync_WithRepeatedLogin_ShouldIssueSeparateSessions()
    {
        // Arrange
        var harness = new Harness();
        var command = TestLoginCommands.Valid(
            loginId: RegisteredEmail,
            password: RegisteredPassword);

        // Act
        var first = await harness.UseCase.ExecuteAsync(command);
        var second = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(first.Value.AccessToken, Is.Not.EqualTo(second.Value.AccessToken));
            Assert.That(harness.TokenIssuer.IssuedTokens, Has.Count.EqualTo(2));
            Assert.That(harness.SessionStore.StoredSessions, Has.Count.EqualTo(2));
            Assert.That(harness.SessionStore.StoredSessions[0].SessionId,
                Is.Not.EqualTo(harness.SessionStore.StoredSessions[1].SessionId));
            Assert.That(harness.SessionStore.StoredSessions[0].TokenHash.Value,
                Is.Not.EqualTo(harness.SessionStore.StoredSessions[1].TokenHash.Value));
            Assert.That(first.Value.IdleExpiresAt, Is.EqualTo(second.Value.IdleExpiresAt));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldForwardSubmittedPasswordWithoutTrimming()
    {
        // Arrange
        var paddedPassword = "  example-password  ";
        var harness = new Harness();
        harness.UserRepository.FoundUser = RegisteredAccount(password: paddedPassword);
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: paddedPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.PasswordHasher.VerifiedInputs, Is.EqualTo(new[] { paddedPassword }));
    }

    [Test]
    public async Task ExecuteAsync_WithPaddedPasswordAgainstTrimmedAccount_ShouldReturnInvalidCredentials()
    {
        // Arrange
        var harness = new Harness();
        var paddedPassword = " example-password ";
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: paddedPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertInvalidCredentials(result);
        Assert.That(harness.PasswordHasher.VerifiedInputs, Is.EqualTo(new[] { paddedPassword }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldAllowPasswordShorterThanRegistrationPolicy()
    {
        // Arrange
        var shortPassword = "abc";
        var harness = new Harness();
        harness.UserRepository.FoundUser = RegisteredAccount(password: shortPassword);
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: shortPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(harness.SessionStore.StoredSessions, Has.Count.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> InvalidLoginInputs()
    {
        yield return new TestCaseData(new LoginCommand(null!, RegisteredPassword))
            .SetName("Login id is null");
        yield return new TestCaseData(new LoginCommand("", RegisteredPassword))
            .SetName("Login id is empty");
        yield return new TestCaseData(new LoginCommand("   ", RegisteredPassword))
            .SetName("Login id is whitespace only");
        yield return new TestCaseData(new LoginCommand("not-an-email", RegisteredPassword))
            .SetName("Login id is not an email");
        yield return new TestCaseData(new LoginCommand(RegisteredEmail, null!))
            .SetName("Password is null");
        yield return new TestCaseData(new LoginCommand(RegisteredEmail, ""))
            .SetName("Password is empty");
        yield return new TestCaseData(new LoginCommand(RegisteredEmail, "   "))
            .SetName("Password is whitespace only");
        yield return new TestCaseData(new LoginCommand(RegisteredEmail, new string('p', 129)))
            .SetName("Password exceeds 128 characters");
    }

    [TestCaseSource(nameof(InvalidLoginInputs))]
    public async Task ExecuteAsync_WithInvalidInput_ShouldReturnInvalidCredentialsWithoutLookup(
        LoginCommand command)
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertInvalidCredentials(result);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedNormalizedEmails, Is.Empty);
            Assert.That(harness.PasswordHasher.VerifiedInputs, Is.Empty);
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> InvalidCredentialScenarios()
    {
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository => repository.FoundUser = null),
                TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword))
            .SetName("Account does not exist");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository =>
                    repository.FoundUser = RegisteredAccount(status: UserStatus.Suspended)),
                TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword))
            .SetName("User is suspended");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository =>
                    repository.FoundUser = RegisteredAccount(status: UserStatus.Deleted)),
                TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword))
            .SetName("User is deleted");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(_ => { }),
                TestLoginCommands.Valid(loginId: RegisteredEmail, password: "wrong-password"))
            .SetName("Password does not match");
    }

    [TestCaseSource(nameof(InvalidCredentialScenarios))]
    public async Task ExecuteAsync_WithFailedAuthentication_ShouldReturnInvalidCredentials(
        Action<FakeUserRepository> arrangeAccount,
        LoginCommand command)
    {
        // Arrange
        var harness = new Harness();
        arrangeAccount(harness.UserRepository);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertInvalidCredentials(result);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedNormalizedEmails, Has.Count.EqualTo(1));
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public async Task ExecuteAsync_WithLookupFailure_ShouldReturnDependencyUnavailableWithoutIssuingToken()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FindByEmailFailure = CommonError.DependencyUnavailable();
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithSessionStoreFailure_ShouldReturnDependencyUnavailable()
    {
        // Arrange
        var harness = new Harness();
        harness.SessionStore.StoreFailure = Result.Fail(CommonError.DependencyUnavailable());
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Has.Count.EqualTo(1));
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteAsync_WithNonPositiveUserId_ShouldThrowForRepositoryContractViolation()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FoundUser = RegisteredAccount(id: 0);
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(command));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public void ExecuteAsync_WithUnexpectedPortException_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FindByEmailException = new InvalidOperationException("unexpected port failure");
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(command));
        Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
    }

    [Test]
    public void ExecuteAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(command, cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
        });
    }

    [Test]
    public void ExecuteAsync_WithCancellationAfterLookup_ShouldNotIssueToken()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        harness.TransactionExecutor.OnCommitted = cancellationSource.Cancel;
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(command, cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.TokenIssuer.IssuedTokens, Is.Empty);
            Assert.That(harness.SessionStore.StoredSessions, Is.Empty);
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

    [Test]
    public async Task ExecuteAsync_ShouldNotChangeUserState()
    {
        // Arrange
        var harness = new Harness();
        var account = harness.UserRepository.FoundUser!;
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(account.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(account.Version, Is.EqualTo(1L));
            Assert.That(account.SecurityVersion, Is.EqualTo(1L));
            Assert.That(account.UpdatedAt, Is.EqualTo(account.CreatedAt));
            Assert.That(account.SuspendedAt, Is.Null);
            Assert.That(account.DeletedAt, Is.Null);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
        });
    }

    [Test]
    public async Task LoginContracts_ShouldRedactSecretsInToString()
    {
        // Arrange
        var harness = new Harness();
        var command = TestLoginCommands.Valid(loginId: RegisteredEmail, password: RegisteredPassword);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(command.ToString(), Does.Not.Contain(RegisteredPassword));
            Assert.That(command.ToString(), Does.Contain("[redacted]"));
            Assert.That(result.Value.ToString(), Does.Not.Contain(result.Value.AccessToken));
            Assert.That(result.Value.ToString(), Does.Contain("[redacted]"));
        });
    }

    [Test]
    public void LoginContracts_ShouldExcludeInternalIdentifiersAndSecrets()
    {
        // Arrange & Act & Assert
        Assert.That(
            typeof(LoginCommand).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "LoginId", "Password" }));
        Assert.That(
            typeof(LoginResultDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "AccessToken", "TokenType", "IdleExpiresAt", "AbsoluteExpiresAt", "User" }));
        Assert.That(
            typeof(UserDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[]
            {
                "UserPublicId", "Email", "UserName", "DisplayName",
                "SystemRole", "Status", "CreatedAt", "UpdatedAt",
            }));
    }

    private static User RegisteredAccount(
        string email = RegisteredEmail,
        string password = RegisteredPassword,
        UserStatus status = UserStatus.Active,
        long id = 42L,
        long securityVersion = 1L)
    {
        var passwordHash = new FakePasswordHasher().Hash(password);
        return User.Reconstitute(
            id,
            Guid.CreateVersion7(),
            EmailAddress.Create(email).Value,
            NormalizedName.Create("cloud-user").Value,
            "Cloud User",
            passwordHash,
            SystemRole.User,
            status,
            securityVersion,
            1L,
            InitialNow,
            InitialNow,
            status == UserStatus.Suspended ? InitialNow : null,
            status == UserStatus.Deleted ? InitialNow : null);
    }

    private static void AssertInvalidCredentials(Result<LoginResultDto> result)
    {
        var expected = SessionError.AuthInvalidCredentials();
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors[0], Is.InstanceOf<SessionError>());
            Assert.That(
                result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Session.AuthInvalidCredentials));
            Assert.That(result.Errors[0].Message, Is.EqualTo(expected.Message));
        });
    }

    private static string FirstErrorCode<T>(Result<T> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}