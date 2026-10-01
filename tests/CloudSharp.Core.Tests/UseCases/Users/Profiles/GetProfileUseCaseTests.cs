using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Users.Dtos;
using CloudSharp.Core.UseCases.Users.Profiles;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Users.Profiles;

[TestFixture]
public class GetProfileUseCaseTests
{
    private const string RegisteredEmail = "user@example.com";

    private const string RegisteredPassword = "example-password";

    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 26, 3, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset UpdatedAt = new(2026, 7, 26, 5, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness()
        {
            UserRepository = new FakeUserRepository { FoundByIdUser = SeededAccount() };
        }

        public FakeUserRepository UserRepository { get; }

        public FakeTransactionExecutor TransactionExecutor { get; } = new();

        public GetProfileUseCase UseCase => new(UserRepository, TransactionExecutor);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnProfileWithConcurrencyVersion()
    {
        // Arrange
        var harness = new Harness();
        var account = harness.UserRepository.FoundByIdUser!;
        var query = TestGetProfileQueries.For(account);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var profile = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(profile.User.UserPublicId, Is.EqualTo(account.PublicId));
            Assert.That(profile.User.Email, Is.EqualTo(RegisteredEmail));
            Assert.That(profile.User.UserName, Is.EqualTo("cloud-user"));
            Assert.That(profile.User.DisplayName, Is.EqualTo("Cloud User"));
            Assert.That(profile.User.SystemRole, Is.EqualTo(SystemRole.User));
            Assert.That(profile.User.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(profile.User.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(profile.User.UpdatedAt, Is.EqualTo(UpdatedAt));
            Assert.That(profile.Version, Is.EqualTo(3L));
            Assert.That(profile.Version, Is.Not.EqualTo(account.SecurityVersion));
        });
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { account.Id }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnAdministratorProfile()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FoundByIdUser = SeededAccount(role: SystemRole.SystemAdmin);
        var account = harness.UserRepository.FoundByIdUser!;
        var query = TestGetProfileQueries.For(account);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.User.UserPublicId, Is.EqualTo(account.PublicId));
            Assert.That(result.Value.User.SystemRole, Is.EqualTo(SystemRole.SystemAdmin));
            Assert.That(result.Value.User.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(result.Value.Version, Is.EqualTo(account.Version));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithMissingOptionalProfileFields_ShouldReturnNullProfileFields()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FoundByIdUser = SeededAccount(userName: null, displayName: null);
        var query = TestGetProfileQueries.For(harness.UserRepository.FoundByIdUser!);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.User.UserName, Is.Null);
            Assert.That(result.Value.User.DisplayName, Is.Null);
            Assert.That(result.Value.User.Email, Is.EqualTo(RegisteredEmail));
        });
    }

    private static IEnumerable<TestCaseData> InvalidAccountScenarios()
    {
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository => repository.FoundByIdUser = null))
            .SetName("Account does not exist");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository =>
                    repository.FoundByIdUser = SeededAccount(status: UserStatus.Suspended)))
            .SetName("User is suspended");
        yield return new TestCaseData(
                new Action<FakeUserRepository>(repository =>
                    repository.FoundByIdUser = SeededAccount(status: UserStatus.Deleted)))
            .SetName("User is deleted");
    }

    [TestCaseSource(nameof(InvalidAccountScenarios))]
    public async Task ExecuteAsync_WithInactiveAccount_ShouldReturnAuthSessionInvalid(
        Action<FakeUserRepository> arrangeAccount)
    {
        // Arrange
        var harness = new Harness();
        var account = harness.UserRepository.FoundByIdUser!;
        var query = TestGetProfileQueries.For(account);
        arrangeAccount(harness.UserRepository);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        AssertAuthSessionInvalid(result);
        Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { account.Id }));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_WithNonPositiveUserId_ShouldReturnAuthSessionInvalidWithoutLookup(
        long userId)
    {
        // Arrange
        var harness = new Harness();
        var query = TestGetProfileQueries.For(harness.UserRepository.FoundByIdUser!, userId: userId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        AssertAuthSessionInvalid(result);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ExecuteAsync_WithLookupFailure_ShouldReturnDependencyFailure()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FindByIdFailure = CommonError.DependencyUnavailable();
        var query = TestGetProfileQueries.For(harness.UserRepository.FoundByIdUser!);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { 42L }));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void ExecuteAsync_WithUnexpectedPortException_ShouldPropagate()
    {
        // Arrange
        var harness = new Harness();
        harness.UserRepository.FindByIdException = new InvalidOperationException("unexpected port failure");
        var query = TestGetProfileQueries.For(harness.UserRepository.FoundByIdUser!);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.UseCase.ExecuteAsync(query));
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteAsync_WithCanceledToken_ShouldPropagateCancellation()
    {
        // Arrange
        var harness = new Harness();
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var query = TestGetProfileQueries.For(harness.UserRepository.FoundByIdUser!);

        // Act & Assert
        Assert.ThrowsAsync<OperationCanceledException>(
            () => harness.UseCase.ExecuteAsync(query, cancellationSource.Token));
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(0));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ExecuteAsync_WithNullQuery_ShouldThrowArgumentNullException()
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
        var account = harness.UserRepository.FoundByIdUser!;
        var query = TestGetProfileQueries.For(account);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(account.Status, Is.EqualTo(UserStatus.Active));
            Assert.That(account.Version, Is.EqualTo(3L));
            Assert.That(account.SecurityVersion, Is.EqualTo(5L));
            Assert.That(account.UpdatedAt, Is.EqualTo(UpdatedAt));
            Assert.That(account.SuspendedAt, Is.Null);
            Assert.That(account.DeletedAt, Is.Null);
            Assert.That(harness.UserRepository.AddedUsers, Is.Empty);
            Assert.That(harness.UserRepository.SavedByIdUsers, Is.Empty);
            Assert.That(
                harness.UserRepository.PersistedUsersById[account.Id].Version,
                Is.EqualTo(account.Version));
        });
    }

    [Test]
    public void GetProfileContracts_ShouldExcludeInternalIdentifiersAndSecrets()
    {
        // Arrange & Act & Assert
        Assert.That(
            typeof(GetProfileQuery).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "UserId" }));
        Assert.That(
            typeof(GetProfileResultDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[] { "User", "Version" }));
        Assert.That(
            typeof(UserDto).GetProperties().Select(property => property.Name),
            Is.EquivalentTo(new[]
            {
                "UserPublicId", "Email", "UserName", "DisplayName",
                "SystemRole", "Status", "CreatedAt", "UpdatedAt",
            }));
    }

    [Test]
    public async Task GetProfileContracts_ShouldExcludeSecretsInToString()
    {
        // Arrange
        var harness = new Harness();
        var account = harness.UserRepository.FoundByIdUser!;
        var query = TestGetProfileQueries.For(account);

        // Act
        var result = await harness.UseCase.ExecuteAsync(query);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(query.ToString(), Does.Not.Contain(account.PasswordHash));
            Assert.That(result.Value.ToString(), Does.Not.Contain(account.PasswordHash));
            Assert.That(result.Value.ToString(), Does.Contain(account.PublicId.ToString()));
        });
    }

    private static User SeededAccount(
        string email = RegisteredEmail,
        string? userName = "cloud-user",
        string? displayName = "Cloud User",
        SystemRole role = SystemRole.User,
        UserStatus status = UserStatus.Active,
        long id = 42L,
        long securityVersion = 5L,
        long version = 3L)
    {
        var passwordHash = new FakePasswordHasher().Hash(RegisteredPassword);
        return User.Reconstitute(
            id,
            Guid.CreateVersion7(),
            EmailAddress.Create(email).Value,
            userName is null ? null : NormalizedName.Create(userName).Value,
            displayName,
            passwordHash,
            role,
            status,
            securityVersion,
            version,
            CreatedAt,
            UpdatedAt,
            status == UserStatus.Suspended ? UpdatedAt : null,
            status == UserStatus.Deleted ? UpdatedAt : null);
    }

    private static void AssertAuthSessionInvalid(Result<GetProfileResultDto> result)
    {
        var expected = SessionError.AuthSessionInvalid();
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors[0], Is.InstanceOf<SessionError>());
            Assert.That(
                result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Session.AuthSessionInvalid));
            Assert.That(result.Errors[0].Message, Is.EqualTo(expected.Message));
        });
    }

    private static string FirstErrorCode(Result<GetProfileResultDto> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}