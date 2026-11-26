using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.MetadataUpdates;
using NUnit.Framework;
using static CloudSharp.Core.Tests.UseCases.Mounts.MountMutationTestHarness;

namespace CloudSharp.Core.Tests.UseCases.Mounts.MetadataUpdates;

[TestFixture]
public sealed class UpdateMountUseCaseTests
{
    private static readonly DateTimeOffset Now = TestMountReadSnapshots.Now;

    [TestCase("Name")]
    [TestCase("Visibility")]
    public async Task ExecuteAsync_ShouldChangeOnlySuppliedSettings(string changedField)
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.User,
                TestMountReadSnapshots.ActorUserId,
                MountPermission.FilesWrite),
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                OtherRoleTargetId,
                MountPermission.FilesDownload),
        ]);
        var harness = Owner(mount);
        var command = changedField == "Name"
            ? Command(harness, mount, name: new MountNameChange("  Renamed Mount  "))
            : Command(harness, mount, visibility: new MountVisibilityChange(MountVisibility.Private));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        var dto = result.Value.Mount;
        var expectedName = changedField == "Name" ? "Renamed Mount" : mount.Name;
        var expectedVisibility = changedField == "Visibility" ? MountVisibility.Private : mount.Visibility;
        Assert.Multiple(() =>
        {
            Assert.That(update.Name, Is.EqualTo(expectedName));
            Assert.That(update.Visibility, Is.EqualTo(expectedVisibility));
            Assert.That(update.Grants, Is.EquivalentTo(mount.Grants));
            Assert.That(dto.Name, Is.EqualTo(expectedName));
            Assert.That(dto.Visibility, Is.EqualTo(expectedVisibility));
            Assert.That(dto.RootFolderPublicId, Is.EqualTo(DefaultRootFolderPublicId));
            Assert.That(dto.UsedBytes, Is.EqualTo(DefaultUsedBytes));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(
                new[] { MountPermission.FilesRead, MountPermission.FilesDownload, MountPermission.FilesWrite }));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(dto.CreatedAt, Is.EqualTo(mount.CreatedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details", "stage", "save" }));
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.DetailsReader.WasInTransaction, Is.True);
        });
    }

    [TestCase("Name")]
    [TestCase("Visibility")]
    [TestCase("Both")]
    public async Task ExecuteAsync_ShouldPreserveVersionForEquivalentSettings(string suppliedFields)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);
        var command = Command(
            harness,
            mount,
            name: suppliedFields is "Name" or "Both" ? new MountNameChange($"  {mount.Name}  ") : null,
            visibility: suppliedFields is "Visibility" or "Both"
                ? new MountVisibilityChange(mount.Visibility)
                : null);

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertNoOp(harness);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version));
            Assert.That(result.Value.Mount.Name, Is.EqualTo(mount.Name));
            Assert.That(result.Value.Mount.Visibility, Is.EqualTo(mount.Visibility));
            Assert.That(result.Value.Mount.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(result.Value.Mount.RootFolderPublicId, Is.EqualTo(DefaultRootFolderPublicId));
            Assert.That(result.Value.Mount.UsedBytes, Is.EqualTo(DefaultUsedBytes));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldEditArchivedMountWithoutRestoringWriteAccess()
    {
        // Arrange
        var mount = StoredMount(
            MountStatus.Archived,
            grants:
            [
                TestMountReadSnapshots.Grant(
                    MountGrantTargetType.User,
                    TestMountReadSnapshots.ActorUserId,
                    MountPermission.FilesWrite),
            ]);
        var harness = Owner(mount);
        var command = Command(harness, mount, name: new MountNameChange("Archived Mount Renamed"));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertSuccessfulWrite(harness, mount.Version);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Mount.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(result.Value.Mount.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(result.Value.Mount.MyAccess.Permissions, Is.EquivalentTo(
                new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
            Assert.That(result.Value.Mount.MyAccess.Permissions, Does.Not.Contain(MountPermission.FilesWrite));
            Assert.That(result.Value.Mount.MyAccess.CanManage, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeStagingEquivalentUpdate()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);
        var command = Command(
            harness,
            mount,
            expectedVersion: mount.Version - 1,
            name: new MountNameChange(mount.Name));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details" }));
    }

    [TestCase("Missing")]
    [TestCase("CrossSpace")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundBeforeVersionCheck(string scenario)
    {
        // Arrange
        var requestedMount = StoredMount();
        var storedMount = scenario == "CrossSpace"
            ? StoredMount(spaceId: TestMountReadSnapshots.SpaceId + 1)
            : null;
        var harness = Owner(storedMount);
        var command = Command(
            harness,
            requestedMount,
            expectedVersion: requestedMount.Version - 1,
            name: new MountNameChange("Renamed Mount"));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details" }));
    }

    [TestCase(MountVisibility.Public, ErrorCodes.Auth.SpacePermissionDenied)]
    [TestCase(MountVisibility.Private, ErrorCodes.Common.ResourceNotFound)]
    public async Task ExecuteAsync_ShouldCheckReadabilityAndManagerEligibilityBeforeVersion(
        MountVisibility visibility,
        string expectedError)
    {
        // Arrange
        var mount = StoredMount(visibility: visibility);
        var harness = OrdinaryMember(mount);
        var command = Command(
            harness,
            mount,
            expectedVersion: mount.Version - 1,
            name: new MountNameChange("Renamed Mount"));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, expectedError);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details" }));
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectInactiveSpaceBeforeMountRead(SpaceStatus spaceStatus)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount, spaceStatus);
        var command = Command(
            harness,
            mount,
            expectedVersion: mount.Version - 1,
            name: new MountNameChange("Renamed Mount"));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize" }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackStagingFailureWithoutSaving()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);
        var failure = CommonError.DependencyUnavailable();
        harness.Writer.Failure = failure;

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(
            Command(harness, mount, name: new MountNameChange("Renamed Mount")));

        // Assert
        AssertStagingFailure(harness, result, failure);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveConflictWithoutCommitting()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);
        var failure = CommonError.PreconditionFailed();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(
            Command(harness, mount, name: new MountNameChange("Renamed Mount")));

        // Assert
        AssertSaveFailure(harness, result, failure);
        Assert.That(harness.Writer.UpdateCalls.Single().ExpectedVersion, Is.EqualTo(mount.Version));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldReturnVersionValidationErrorWithoutDependencyCalls(long expectedVersion)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);
        var command = Command(
            harness,
            mount,
            expectedVersion: expectedVersion,
            name: new MountNameChange("Renamed Mount"));

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.ExpectedVersionInvalid,
            nameof(UpdateMountCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task ExecuteAsync_ShouldReturnNameValidationErrorWithoutDependencyCalls(string name)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Owner(mount);

        // Act
        var result = await harness.UpdateUseCase.ExecuteAsync(
            Command(harness, mount, name: new MountNameChange(name)));

        // Assert
        AssertValidationError(result, ErrorCodes.Mount.NameInvalid, nameof(UpdateMountCommand.Name));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(
        MountStatus status = MountStatus.Active,
        MountVisibility visibility = MountVisibility.Public,
        long spaceId = TestMountReadSnapshots.SpaceId,
        IEnumerable<MountAccessGrant>? grants = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            status,
            updatedAt: Now.AddHours(-1),
            grants: grants);

    private static UpdateMountCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        long? expectedVersion = null,
        MountNameChange? name = null,
        MountVisibilityChange? visibility = null) =>
        new(
            harness.Actor,
            TestMountReadSnapshots.SpacePublicId,
            mount.PublicId,
            expectedVersion ?? mount.Version,
            name,
            visibility);
}
