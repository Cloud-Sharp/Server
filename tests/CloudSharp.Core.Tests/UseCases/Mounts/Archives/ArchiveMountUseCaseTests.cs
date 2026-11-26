using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.Archives;
using NUnit.Framework;
using static CloudSharp.Core.Tests.UseCases.Mounts.MountMutationTestHarness;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Archives;

[TestFixture]
public sealed class ArchiveMountUseCaseTests
{
    private static readonly DateTimeOffset Now = TestMountReadSnapshots.Now;

    [Test]
    public async Task ExecuteAsync_ShouldArchiveMountAndSuppressEffectiveWriteAccess()
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.User,
                TestMountReadSnapshots.ActorUserId,
                MountPermission.FilesWrite),
        ]);
        var harness = Administrator(mount);
        var command = Command(harness, mount);

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        var dto = result.Value.Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(update.ArchivedAt, Is.EqualTo(Now));
            Assert.That(update.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(update.Grants, Is.EquivalentTo(mount.Grants));
            Assert.That(dto.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(dto.ArchivedAt, Is.EqualTo(Now));
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.RootFolderPublicId, Is.EqualTo(DefaultRootFolderPublicId));
            Assert.That(dto.UsedBytes, Is.EqualTo(DefaultUsedBytes));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(
                new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
            Assert.That(dto.MyAccess.Permissions, Does.Not.Contain(MountPermission.FilesWrite));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details", "stage", "save" }));
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.DetailsReader.WasInTransaction, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnRequestConflictForCurrentArchivedMount()
    {
        // Arrange
        var mount = StoredMount(MountStatus.Archived);
        var harness = Owner(mount);

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(Command(harness, mount));

        // Assert
        AssertError(result, ErrorCodes.Common.RequestConflict);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-details" }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeLifecycleValidation()
    {
        // Arrange
        var mount = StoredMount(MountStatus.Archived);
        var harness = Owner(mount);

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(
            Command(harness, mount, mount.Version - 1));

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

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(
            Command(harness, requestedMount, requestedMount.Version - 1));

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

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(
            Command(harness, mount, mount.Version - 1));

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

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(
            Command(harness, mount, mount.Version - 1));

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
        var result = await harness.ArchiveUseCase.ExecuteAsync(Command(harness, mount));

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
        var result = await harness.ArchiveUseCase.ExecuteAsync(Command(harness, mount));

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

        // Act
        var result = await harness.ArchiveUseCase.ExecuteAsync(Command(harness, mount, expectedVersion));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.ExpectedVersionInvalid,
            nameof(ArchiveMountCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(
        MountStatus status = MountStatus.Active,
        MountVisibility visibility = MountVisibility.Private,
        long spaceId = TestMountReadSnapshots.SpaceId,
        IEnumerable<MountAccessGrant>? grants = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            status,
            updatedAt: Now.AddHours(-1),
            grants: grants);

    private static ArchiveMountCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        long? expectedVersion = null) =>
        new(
            harness.Actor,
            TestMountReadSnapshots.SpacePublicId,
            mount.PublicId,
            expectedVersion ?? mount.Version);
}
