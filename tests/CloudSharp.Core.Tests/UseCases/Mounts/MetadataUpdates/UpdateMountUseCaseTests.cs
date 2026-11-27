using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
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
        var mount = StoredMount(overwrites:
        [
            TestMountReadSnapshots.Overwrite(MountOverwriteTargetType.User, TestMountReadSnapshots.ActorUserId, allow: [SpacePermission.MountManage]),
            TestMountReadSnapshots.Overwrite(MountOverwriteTargetType.Role, OtherRoleTargetId, allow: [SpacePermission.MountView]),
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
            Assert.That(update.Overwrites, Is.EquivalentTo(mount.Overwrites));
            Assert.That(dto.Name, Is.EqualTo(expectedName));
            Assert.That(dto.Visibility, Is.EqualTo(expectedVisibility));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(
                MountScopedPermissions.All));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(dto.CreatedAt, Is.EqualTo(mount.CreatedAt));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount", "stage", "save" }));
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.MountReader.WasInTransaction, Is.True);
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
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldAllowDelegatedManagementOfArchivedMount()
    {
        // Arrange
        var mount = StoredMount(
            MountStatus.Archived,
            overwrites:
            [
                TestMountReadSnapshots.Overwrite(MountOverwriteTargetType.User, TestMountReadSnapshots.ActorUserId, allow: [SpacePermission.MountManage]),
            ]);
        var harness = OrdinaryMember(mount);
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
                new[] { SpacePermission.MountView, SpacePermission.MountManage, SpacePermission.FilesRead }));
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
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
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
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
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
        IEnumerable<MountPermissionOverwrite>? overwrites = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            status,
            updatedAt: Now.AddHours(-1),
            overwrites: overwrites);

    private static UpdateMountCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        long? expectedVersion = null,
        MountNameChange? name = null,
        MountVisibilityChange? visibility = null) =>
        new(
            harness.MountContext(mount),
            expectedVersion ?? mount.Version,
            name,
            visibility);
}
