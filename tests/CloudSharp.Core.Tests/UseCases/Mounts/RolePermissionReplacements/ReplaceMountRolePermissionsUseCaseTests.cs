using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.RolePermissionReplacements;
using NUnit.Framework;
using static CloudSharp.Core.Tests.UseCases.Mounts.MountMutationTestHarness;

namespace CloudSharp.Core.Tests.UseCases.Mounts.RolePermissionReplacements;

[TestFixture]
public sealed class ReplaceMountRolePermissionsUseCaseTests
{
    private static readonly DateTimeOffset Now = TestMountReadSnapshots.Now;

    [Test]
    public async Task ExecuteAsync_ShouldReplaceOnlyDesignatedRoleGrant()
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                RoleTargetId,
                MountPermission.FilesDownload),
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                OtherRoleTargetId,
                MountPermission.FilesRead),
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.User,
                OtherUserTargetId,
                MountPermission.FilesWrite),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            permissions: [MountPermission.FilesWrite, MountPermission.FilesRead, MountPermission.FilesWrite]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountGrantTargetType.Role,
            target.PublicId,
            [MountPermission.FilesRead, MountPermission.FilesWrite],
            mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        var targetGrant = update.Grants.Single(grant =>
            grant.TargetType == MountGrantTargetType.Role && grant.TargetId == target.Id);
        var otherRoleGrant = update.Grants.Single(grant =>
            grant.TargetType == MountGrantTargetType.Role && grant.TargetId == OtherRoleTargetId);
        var userGrant = update.Grants.Single(grant => grant.TargetType == MountGrantTargetType.User);
        Assert.Multiple(() =>
        {
            Assert.That(update.Grants, Has.Count.EqualTo(3));
            Assert.That(targetGrant.Permissions, Is.EquivalentTo(
                new[] { MountPermission.FilesRead, MountPermission.FilesWrite }));
            Assert.That(otherRoleGrant.Permissions, Is.EquivalentTo(new[] { MountPermission.FilesRead }));
            Assert.That(userGrant.TargetId, Is.EqualTo(OtherUserTargetId));
            Assert.That(userGrant.Permissions, Is.EquivalentTo(new[] { MountPermission.FilesWrite }));
            Assert.That(update.UpdatedAt, Is.EqualTo(Now));
            Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
            Assert.That(harness.GrantTargetReader.SpaceId, Is.EqualTo(TestMountReadSnapshots.SpaceId));
            Assert.That(harness.GrantTargetReader.RequestedRolePublicIds, Is.EqualTo(new[] { target.PublicId }));
            Assert.That(harness.GrantTargetReader.RequestedUserPublicIds, Is.Empty);
            Assert.That(harness.AuthorizationReader.WasInTransaction, Is.True);
            Assert.That(harness.MountReader.WasInTransaction, Is.True);
            Assert.That(harness.GrantTargetReader.WasInTransaction, Is.True);
            Assert.That(harness.Events, Is.EqualTo(
                new[] { "authorize", "read-mount", "read-targets", "stage", "save" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnCurrentGrantForEquivalentSetWithoutWriting()
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                RoleTargetId,
                MountPermission.FilesRead,
                MountPermission.FilesWrite),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            permissions: [MountPermission.FilesWrite, MountPermission.FilesRead, MountPermission.FilesWrite]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountGrantTargetType.Role,
            target.PublicId,
            [MountPermission.FilesRead, MountPermission.FilesWrite],
            mount.Version);
        AssertNoOp(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.LastReadMount!.Version, Is.EqualTo(mount.Version));
            Assert.That(harness.LastReadMount.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount", "read-targets" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeTargetLookupForEquivalentSet()
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                RoleTargetId,
                MountPermission.FilesRead),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(
                harness,
                mount,
                target.PublicId,
                mount.Version - 1,
                [MountPermission.FilesRead]));

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertRejectedWithoutWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
        });
    }

    [TestCase("Missing")]
    [TestCase("CrossSpace")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundForMissingOrCrossSpaceTarget(string scenario)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = scenario == "CrossSpace"
            ? harness.SeedRole(spaceId: TestMountReadSnapshots.SpaceId + 1)
            : null;
        var targetPublicId = target?.PublicId ?? TestMountReadSnapshots.PublicId(900_001);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, targetPublicId));

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
        AssertRejectedWithoutWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
            Assert.That(harness.GrantTargetReader.WasInTransaction, Is.True);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnRequestConflictForDeletedRoleWithNonemptyReplacement()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole(deletedAt: Now.AddDays(-1));

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId));

        // Assert
        AssertError(result, ErrorCodes.Common.RequestConflict);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRemoveDeletedRoleGrantForEmptyReplacement()
    {
        // Arrange
        var mount = StoredMount(grants:
        [
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                RoleTargetId,
                MountPermission.FilesWrite),
            TestMountReadSnapshots.Grant(
                MountGrantTargetType.Role,
                OtherRoleTargetId,
                MountPermission.FilesRead),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole(deletedAt: Now.AddDays(-1));

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, permissions: []));

        // Assert
        AssertPermissionResult(result, MountGrantTargetType.Role, target.PublicId, [], mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Grants, Has.Count.EqualTo(1));
            Assert.That(update.Grants.Single().TargetId, Is.EqualTo(OtherRoleTargetId));
            Assert.That(update.UpdatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmptyGrantForDeletedRoleWithoutExistingGrant()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole(deletedAt: Now.AddDays(-1));

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, permissions: []));

        // Assert
        AssertPermissionResult(result, MountGrantTargetType.Role, target.PublicId, [], mount.Version);
        AssertNoOp(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.LastReadMount!.Version, Is.EqualTo(mount.Version));
            Assert.That(harness.LastReadMount.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
        });
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
        var harness = Administrator(storedMount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, requestedMount, target.PublicId, requestedMount.Version - 1));

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
        AssertRejectedWithoutWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
        });
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
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, mount.Version - 1));

        // Assert
        AssertError(result, expectedError);
        AssertRejectedWithoutWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
        });
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectInactiveSpaceBeforeMountRead(SpaceStatus spaceStatus)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount, spaceStatus);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, mount.Version - 1));

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        AssertRejectedWithoutWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
            Assert.That(harness.Events, Is.EqualTo(new[] { "authorize" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackStagingFailureWithoutSaving()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var failure = CommonError.DependencyUnavailable();
        harness.Writer.Failure = failure;

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId));

        // Assert
        AssertStagingFailure(harness, result, failure);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveConflictWithoutCommitting()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var failure = CommonError.PreconditionFailed();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId));

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
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, expectedVersion));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.ExpectedVersionInvalid,
            nameof(ReplaceMountRolePermissionsCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnTargetValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, Guid.Empty));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.TargetIdInvalid,
            nameof(ReplaceMountRolePermissionsCommand.RolePublicId));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPermissionsValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, permissions: [(MountPermission)(-1)]));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.PermissionsInvalid,
            nameof(ReplaceMountRolePermissionsCommand.Permissions));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(
        MountVisibility visibility = MountVisibility.Public,
        long spaceId = TestMountReadSnapshots.SpaceId,
        IEnumerable<MountAccessGrant>? grants = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            updatedAt: Now.AddHours(-1),
            grants: grants);

    private static ReplaceMountRolePermissionsCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        Guid targetPublicId,
        long? expectedVersion = null,
        IReadOnlyList<MountPermission>? permissions = null) =>
        new(
            harness.Actor,
            TestMountReadSnapshots.SpacePublicId,
            mount.PublicId,
            targetPublicId,
            expectedVersion ?? mount.Version,
            permissions ?? [MountPermission.FilesWrite]);
}
