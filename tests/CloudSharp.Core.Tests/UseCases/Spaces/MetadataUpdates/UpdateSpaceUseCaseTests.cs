using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.MetadataUpdates;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.MetadataUpdates;

[TestFixture]
public class UpdateSpaceUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 4, 10, 0, TimeSpan.Zero);
    private static readonly Guid OwnerUserPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec099");
    private static readonly Guid RootFolderPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec100");

    private sealed class Harness
    {
        public Harness(Space? storedSpace = null)
        {
            DetailsReader = new FakeSpaceDetailsReader();
            SpaceRepository = new FakeSpaceRepository();
            if (storedSpace is not null)
            {
                DetailsReader.Snapshot = new SpaceDetailsSnapshot(
                    storedSpace,
                    OwnerUserPublicId,
                    RootFolderPublicId);
                SpaceRepository.Seed(storedSpace);
            }

            UnitOfWork = new FakeSpaceUnitOfWork(SpaceRepository);
            TransactionExecutor = new FakeTransactionExecutor();
            Clock = new FakeClock(Now);
        }

        public FakeSpaceDetailsReader DetailsReader { get; }
        public FakeSpaceRepository SpaceRepository { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor TransactionExecutor { get; }
        public FakeClock Clock { get; }

        public UpdateSpaceUseCase UseCase => new(
            DetailsReader,
            SpaceRepository,
            UnitOfWork,
            TransactionExecutor,
            Clock);
    }

    [Test]
    public async Task ExecuteAsync_ShouldUpdateNameAndPreserveDescription()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(context, space.Version, "Renamed Product", null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSuccessfulWrite(
            harness,
            space,
            context,
            result,
            expectedName: "Renamed Product",
            expectedDescription: "Original description");
    }

    [Test]
    public async Task ExecuteAsync_ShouldUpdateDescriptionAndPreserveName()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(
            context,
            space.Version,
            null,
            new SpaceDescriptionChange("Revised description"));

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSuccessfulWrite(
            harness,
            space,
            context,
            result,
            expectedName: "Product",
            expectedDescription: "Revised description");
    }

    [Test]
    public async Task ExecuteAsync_ShouldClearDescription()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(
            context,
            space.Version,
            null,
            new SpaceDescriptionChange(null));

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSuccessfulWrite(
            harness,
            space,
            context,
            result,
            expectedName: "Product",
            expectedDescription: null);
    }

    [TestCase(null, null)]
    [TestCase(" Product ", " Original description ")]
    public async Task ExecuteAsync_ShouldTreatEquivalentPatchAsNoOp(string? name, string? description)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        SpaceDescriptionChange? descriptionChange = name is null && description is null
            ? null
            : new SpaceDescriptionChange(description);
        var command = new UpdateSpaceCommand(context, space.Version, name, descriptionChange);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var persistedSpace = harness.SpaceRepository.PersistedSpacesById[space.Id];
        Assert.Multiple(() =>
        {
            Assert.That(persistedSpace.Name, Is.EqualTo("Product"));
            Assert.That(persistedSpace.Description, Is.EqualTo("Original description"));
            Assert.That(persistedSpace.Version, Is.EqualTo(space.Version));
            Assert.That(persistedSpace.UpdatedAt, Is.EqualTo(space.UpdatedAt));
            Assert.That(result.Value.Space.Name, Is.EqualTo("Product"));
            Assert.That(result.Value.Space.Description, Is.EqualTo("Original description"));
            Assert.That(result.Value.Space.UpdatedAt, Is.EqualTo(space.UpdatedAt));
            Assert.That(result.Value.Version, Is.EqualTo(space.Version));
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { space.Id }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness();
        var command = new UpdateSpaceCommand(context, space.Version, "Renamed Product", null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpaceNotFound));
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyInsufficientPermissionWithoutDependencyCalls()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(
            space,
            grantedPermissions: [SpacePermission.SpaceView],
            requiredPermissions: [SpacePermission.SpaceView]);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(context, space.Version, "Renamed Product", null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.EffectivePermissions, Does.Not.Contain(SpacePermission.SpaceUpdate));
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpacePermissionDenied));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedState(SpaceStatus persistedStatus)
    {
        // Arrange
        var authorizedSpace = StoredSpace(SpaceStatus.Active);
        var persistedSpace = StoredSpace(persistedStatus);
        var context = MemberContext(authorizedSpace);
        var harness = new Harness(persistedSpace);
        var command = new UpdateSpaceCommand(
            context,
            persistedSpace.Version,
            "Renamed Product",
            null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertStateNotAllowed(result, persistedStatus);
        AssertNoWrites(harness);
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeStagingNoOp()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(context, space.Version - 1, null, null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        AssertNoWrites(harness);
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveConflict()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        var command = new UpdateSpaceCommand(context, space.Version, "Renamed Product", null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.UpdateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var harness = new Harness();
        var command = new UpdateSpaceCommand(null!, 1, null, null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.AuthorizationContextRequired,
            nameof(UpdateSpaceCommand.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldReturnVersionValidationErrorWithoutDependencyCalls(long expectedVersion)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(context, expectedVersion, null, null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(UpdateSpaceCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    private static IEnumerable<TestCaseData> InvalidNames()
    {
        yield return new TestCaseData("").SetName("Name is empty");
        yield return new TestCaseData("   ").SetName("Name is blank");
        yield return new TestCaseData(new string('n', 201)).SetName("Name exceeds 200 characters");
    }

    [TestCaseSource(nameof(InvalidNames))]
    public async Task ExecuteAsync_ShouldReturnNameValidationErrorWithoutDependencyCalls(string name)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        var command = new UpdateSpaceCommand(context, space.Version, name, null);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(result, ErrorCodes.Space.NameInvalid, nameof(UpdateSpaceCommand.Name));
        AssertNoDependencyCalls(harness);
    }

    private static Space StoredSpace(SpaceStatus status) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId: 21,
            name: "Product",
            description: "Original description",
            allowedBytes: 1000,
            usedBytes: status == SpaceStatus.Deleted ? 0 : 400,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : 50,
            version: 3,
            createdAt: Now.AddDays(-7),
            updatedAt: Now.AddDays(-1));

    private static AuthorizedSpaceContext MemberContext(
        Space space,
        IEnumerable<SpacePermission>? grantedPermissions = null,
        IEnumerable<SpacePermission>? requiredPermissions = null)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(userId: 42);
        var membership = TestSpaceAuthorizationSnapshots.Membership(
            userId: actor.UserId,
            spaceId: space.Id);
        var role = TestSpaceAuthorizationSnapshots.Role(
            spaceId: space.Id,
            permissions: grantedPermissions ?? [SpacePermission.SpaceView, SpacePermission.SpaceUpdate]);
        var snapshot = new SpaceAuthorizationSnapshot(space, membership, [role]);
        var query = TestSpaceAuthorizationSnapshots.Query(
            actor: actor,
            permissions: requiredPermissions ?? [SpacePermission.SpaceUpdate],
            statuses: [SpaceStatus.Active],
            spacePublicId: space.PublicId);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static void AssertSuccessfulWrite(
        Harness harness,
        Space originalSpace,
        AuthorizedSpaceContext context,
        Result<SpaceResultDto> result,
        string expectedName,
        string? expectedDescription)
    {
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(context.UserId, Is.Not.EqualTo(originalSpace.OwnerUserId));
        var update = harness.SpaceRepository.UpdateCalls.Single();
        var persistedSpace = harness.SpaceRepository.PersistedSpacesById[originalSpace.Id];
        var dto = result.Value.Space;
        Assert.Multiple(() =>
        {
            Assert.That(update.ExpectedVersion, Is.EqualTo(originalSpace.Version));
            Assert.That(update.Space.PublicId, Is.EqualTo(originalSpace.PublicId));
            Assert.That(update.Space.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(persistedSpace.OwnerUserId, Is.EqualTo(originalSpace.OwnerUserId));
            Assert.That(persistedSpace.Name, Is.EqualTo(expectedName));
            Assert.That(persistedSpace.Description, Is.EqualTo(expectedDescription));
            Assert.That(persistedSpace.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(persistedSpace.AllowedBytes, Is.EqualTo(originalSpace.AllowedBytes));
            Assert.That(persistedSpace.UsedBytes, Is.EqualTo(originalSpace.UsedBytes));
            Assert.That(persistedSpace.ReservedBytes, Is.EqualTo(originalSpace.ReservedBytes));
            Assert.That(persistedSpace.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(persistedSpace.CreatedAt, Is.EqualTo(originalSpace.CreatedAt));
            Assert.That(persistedSpace.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.SpacePublicId, Is.EqualTo(originalSpace.PublicId));
            Assert.That(dto.OwnerUserPublicId, Is.EqualTo(OwnerUserPublicId));
            Assert.That(dto.Name, Is.EqualTo(expectedName));
            Assert.That(dto.Description, Is.EqualTo(expectedDescription));
            Assert.That(dto.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(dto.RootFolderPublicId, Is.EqualTo(RootFolderPublicId));
            Assert.That(dto.MyAccess.Source, Is.EqualTo(context.AuthorizationSource));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(context.EffectivePermissions));
            Assert.That(dto.CreatedAt, Is.EqualTo(originalSpace.CreatedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { originalSpace.Id }));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertStateNotAllowed(Result<SpaceResultDto> result, SpaceStatus expectedStatus)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                error.Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Auth.SpaceStateNotAllowed));
            Assert.That(error, Is.InstanceOf<SpaceAuthorizationError>());
            Assert.That(((SpaceAuthorizationError)error).ActualSpaceStatus, Is.EqualTo(expectedStatus));
        });
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedPublicIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertValidationError(
        Result<SpaceResultDto> result,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
    }

    private static string FirstErrorCode(Result<SpaceResultDto> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
