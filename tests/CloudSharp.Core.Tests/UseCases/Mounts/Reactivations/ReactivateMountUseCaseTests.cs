using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.Reactivations;
using NUnit.Framework;
using static CloudSharp.Core.Tests.UseCases.Mounts.MountMutationTestHarness;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Reactivations;

[TestFixture]
public sealed class ReactivateMountUseCaseTests
{
    private static readonly DateTimeOffset Now = TestMountReadSnapshots.Now;

    [Test]
    public async Task ExecuteAsync_ShouldReactivateMountWithDelegatedManagement()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            TestMountReadSnapshots.Overwrite(MountOverwriteTargetType.User, TestMountReadSnapshots.ActorUserId, allow: [SpacePermission.MountView, SpacePermission.MountManage]),
        ]);
        var harness = OrdinaryMember(mount);
        var command = Command(harness, mount);

        // Act
        var result = await harness.ReactivateUseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        var dto = result.Value.Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(update.ArchivedAt, Is.Null);
            Assert.That(update.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(update.Overwrites, Is.EquivalentTo(mount.Overwrites));
            Assert.That(dto.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(dto.ArchivedAt, Is.Null);
            Assert.That(dto.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.MyAccess.Permissions, Is.EquivalentTo(
                new[] { SpacePermission.MountView, SpacePermission.MountManage, SpacePermission.FilesRead }));
            Assert.That(dto.MyAccess.CanManage, Is.True);
            Assert.That(result.Value.Version, Is.EqualTo(mount.Version + 1));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount", "stage", "save" }));
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.MountReader.WasInTransaction, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnRequestConflictForCurrentActiveMount()
    {
        // Arrange
        var mount = StoredMount(MountStatus.Active);
        var harness = Owner(mount);

        // Act
        var result = await harness.ReactivateUseCase.ExecuteAsync(Command(harness, mount));

        // Assert
        AssertError(result, ErrorCodes.Common.RequestConflict);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeLifecycleValidation()
    {
        // Arrange
        var mount = StoredMount(MountStatus.Active);
        var harness = Owner(mount);

        // Act
        var result = await harness.ReactivateUseCase.ExecuteAsync(
            Command(harness, mount, mount.Version - 1));

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

        // Act
        var result = await harness.ReactivateUseCase.ExecuteAsync(
            Command(harness, requestedMount, requestedMount.Version - 1));

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

        // Act
        var result = await harness.ReactivateUseCase.ExecuteAsync(
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
        var result = await harness.ReactivateUseCase.ExecuteAsync(Command(harness, mount));

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
        var result = await harness.ReactivateUseCase.ExecuteAsync(Command(harness, mount));

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
        var result = await harness.ReactivateUseCase.ExecuteAsync(Command(harness, mount, expectedVersion));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.ExpectedVersionInvalid,
            nameof(ReactivateMountCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(
        MountStatus status = MountStatus.Archived,
        MountVisibility visibility = MountVisibility.Private,
        long spaceId = TestMountReadSnapshots.SpaceId,
        IEnumerable<MountPermissionOverwrite>? overwrites = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            status,
            updatedAt: Now.AddHours(-1),
            overwrites: overwrites);

    private static ReactivateMountCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        long? expectedVersion = null) =>
        new(
            harness.MountContext(mount),
            expectedVersion ?? mount.Version);
}
