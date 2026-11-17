using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Creations;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.Creations;

[TestFixture]
public class CreateSpaceUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 3, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness(UserStatus userStatus = UserStatus.Active)
        {
            Account = CreateAccount(userStatus);
            UserRepository = new FakeUserRepository { FoundByIdUser = Account };
            SpaceRepository = new FakeSpaceRepository();
            BootstrapWriter = new FakeSpaceBootstrapWriter();
            UnitOfWork = new FakeSpaceUnitOfWork(SpaceRepository, BootstrapWriter);
            TransactionExecutor = new FakeTransactionExecutor();
            Clock = new FakeClock(Now);
        }

        public User Account { get; }
        public FakeUserRepository UserRepository { get; }
        public FakeSpaceRepository SpaceRepository { get; }
        public FakeSpaceBootstrapWriter BootstrapWriter { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor TransactionExecutor { get; }
        public FakeClock Clock { get; }

        public CreateSpaceUseCase UseCase => new(
            UserRepository,
            SpaceRepository,
            BootstrapWriter,
            UnitOfWork,
            TransactionExecutor,
            Clock);
    }

    [TestCase(1024L)]
    [TestCase(null)]
    public async Task ExecuteAsync_ShouldCreateOwnedActiveSpace(long? allowedBytes)
    {
        // Arrange
        var harness = new Harness();
        var command = ValidCommand(allowedBytes: allowedBytes);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var addedSpace = harness.SpaceRepository.AddedSpaces.Single();
        var savedSpace = harness.SpaceRepository.PersistedSpacesById.Values.Single();
        var bootstrap = harness.BootstrapWriter.Added.Single();
        Assert.Multiple(() =>
        {
            Assert.That(addedSpace.OwnerUserId, Is.EqualTo(harness.Account.Id));
            Assert.That(addedSpace.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(addedSpace.AllowedBytes, Is.EqualTo(allowedBytes));
            Assert.That(addedSpace.UsedBytes, Is.Zero);
            Assert.That(addedSpace.ReservedBytes, Is.Zero);
            Assert.That(addedSpace.CreatedAt, Is.EqualTo(Now));
            Assert.That(addedSpace.UpdatedAt, Is.EqualTo(Now));
            Assert.That(savedSpace.Id, Is.Positive);
            Assert.That(bootstrap.SpaceId, Is.EqualTo(savedSpace.Id));
            Assert.That(bootstrap.CreatedAt, Is.EqualTo(Now));
            Assert.That(bootstrap.Roles.Select(role => role.SystemKey), Is.EquivalentTo(new[]
            {
                SpaceRoleSystemKey.Admin,
                SpaceRoleSystemKey.Member,
                SpaceRoleSystemKey.Viewer,
            }));
            Assert.That(bootstrap.Roles.Select(role => role.SpaceId), Is.All.EqualTo(savedSpace.Id));
            Assert.That(bootstrap.Roles.Select(role => role.IsManaged), Is.All.True);
        });

        var space = result.Value.Space;
        Assert.Multiple(() =>
        {
            Assert.That(space.SpacePublicId, Is.EqualTo(savedSpace.PublicId));
            Assert.That(space.OwnerUserPublicId, Is.EqualTo(harness.Account.PublicId));
            Assert.That(space.Name, Is.EqualTo("Product Space"));
            Assert.That(space.Description, Is.EqualTo("Product documents"));
            Assert.That(space.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(space.MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(space.MyAccess.Permissions, Is.EquivalentTo(Enum.GetValues<SpacePermission>()));
            Assert.That(space.CreatedAt, Is.EqualTo(Now));
            Assert.That(space.UpdatedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Version, Is.EqualTo(1L));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(2));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnBootstrapDependencyFailureAndRollback()
    {
        // Arrange
        var harness = new Harness();
        harness.UnitOfWork.SaveChangesFailureFactory = call =>
            call == 2 ? CommonError.DependencyUnavailable() : null;

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.AddedSpaces, Has.Count.EqualTo(1));
            Assert.That(harness.BootstrapWriter.Added, Has.Count.EqualTo(1));
            Assert.That(harness.BootstrapWriter.Saved, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(2));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectMcpActorWithoutStagingChanges()
    {
        // Arrange
        var harness = new Harness();
        var actor = CurrentActor.ForMcp(
            harness.Account.Id,
            harness.Account.PublicId,
            SystemRole.User,
            91,
            Guid.Parse("01977b58-9ccc-7674-9554-3f85adeea223"));
        var command = ValidCommand(actor: actor);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.TokenTypeNotAllowed));
        AssertNoWrites(harness);
        Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectInactiveUserWithoutStagingChanges()
    {
        // Arrange
        var harness = new Harness(UserStatus.Suspended);

        // Act
        var result = await harness.UseCase.ExecuteAsync(ValidCommand());

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Session.AuthUserInactive));
        AssertNoWrites(harness);
        Assert.That(harness.UserRepository.SearchedIds, Is.EqualTo(new[] { harness.Account.Id }));
    }

    private static IEnumerable<TestCaseData> InvalidCommands()
    {
        yield return new TestCaseData(
                ValidCommand(name: null!),
                ErrorCodes.Space.NameInvalid,
                nameof(CreateSpaceCommand.Name))
            .SetName("Name is null");
        yield return new TestCaseData(
                ValidCommand(name: "  "),
                ErrorCodes.Space.NameInvalid,
                nameof(CreateSpaceCommand.Name))
            .SetName("Name is blank");
        yield return new TestCaseData(
                ValidCommand(name: new string('n', 201)),
                ErrorCodes.Space.NameInvalid,
                nameof(CreateSpaceCommand.Name))
            .SetName("Name exceeds 200 characters");
        yield return new TestCaseData(
                ValidCommand(allowedBytes: -1),
                ErrorCodes.Space.QuotaInvalid,
                nameof(CreateSpaceCommand.AllowedBytes))
            .SetName("Quota is negative");
    }

    [TestCaseSource(nameof(InvalidCommands))]
    public async Task ExecuteAsync_ShouldReturnValidationErrorWithoutDependencyCalls(
        CreateSpaceCommand command,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
        AssertNoDependencyCalls(harness);
    }

    private static CreateSpaceCommand ValidCommand(
        CurrentActor? actor = null,
        string name = "Product Space",
        string? description = "Product documents",
        long? allowedBytes = 1024) =>
        new(actor ?? TestSpaceAuthorizationSnapshots.Session(), name, description, allowedBytes);

    private static User CreateAccount(UserStatus status) =>
        User.Reconstitute(
            42,
            TestSpaceAuthorizationSnapshots.Session().UserPublicId,
            EmailAddress.Create("owner@example.com").Value,
            NormalizedName.Create("owner").Value,
            "Space Owner",
            "hashed-password",
            SystemRole.User,
            status,
            1,
            3,
            Now.AddDays(-30),
            Now.AddDays(-1),
            status == UserStatus.Suspended ? Now.AddDays(-1) : null,
            status == UserStatus.Deleted ? Now.AddDays(-1) : null);

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.BootstrapWriter.Added, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.UserRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedPublicIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static string FirstErrorCode<T>(Result<T> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
