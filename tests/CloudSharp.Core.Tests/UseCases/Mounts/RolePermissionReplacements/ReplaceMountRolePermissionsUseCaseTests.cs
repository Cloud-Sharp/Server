using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
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
    public async Task ExecuteAsync_ShouldReplaceOnlyDesignatedRoleOverwrite()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(MountOverwriteTargetType.Role, RoleTargetId, allow: [SpacePermission.MountView]),
            Overwrite(MountOverwriteTargetType.Role, OtherRoleTargetId, deny: [SpacePermission.FilesWrite]),
            Overwrite(MountOverwriteTargetType.User, OtherUserTargetId, allow: [SpacePermission.MountManage]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [SpacePermission.MountManage, SpacePermission.MountManage],
            deny: [SpacePermission.FilesDownload]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountOverwriteTargetType.Role,
            target.PublicId,
            [SpacePermission.MountManage],
            [SpacePermission.FilesDownload],
            mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        var targetOverwrite = update.FindOverwrite(MountOverwriteTargetType.Role, target.Id)!;
        Assert.Multiple(() =>
        {
            Assert.That(update.Overwrites, Has.Count.EqualTo(3));
            Assert.That(targetOverwrite.Allow, Is.EquivalentTo(new[] { SpacePermission.MountManage }));
            Assert.That(targetOverwrite.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesDownload }));
            Assert.That(update.FindOverwrite(MountOverwriteTargetType.Role, OtherRoleTargetId)!.Deny,
                Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
            Assert.That(update.FindOverwrite(MountOverwriteTargetType.User, OtherUserTargetId)!.Allow,
                Is.EquivalentTo(new[] { SpacePermission.MountManage }));
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
    public async Task ExecuteAsync_ShouldReturnCurrentOverwriteForEquivalentSetsWithoutWriting()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(
                MountOverwriteTargetType.Role,
                RoleTargetId,
                allow: [SpacePermission.MountView],
                deny: [SpacePermission.FilesWrite]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [SpacePermission.MountView, SpacePermission.MountView],
            deny: [SpacePermission.FilesWrite]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountOverwriteTargetType.Role,
            target.PublicId,
            [SpacePermission.MountView],
            [SpacePermission.FilesWrite],
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
    public async Task ExecuteAsync_ShouldReturnPermissionDeniedWhenChangingPermissionActorLacks()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [SpacePermission.FilesWrite],
            actorPermissions: [SpacePermission.MountView, SpacePermission.MountManage, SpacePermission.FilesRead]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount", "read-targets" }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldKeepUnchangedPermissionActorLacks()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(MountOverwriteTargetType.Role, RoleTargetId, allow: [SpacePermission.FilesWrite]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [SpacePermission.FilesWrite],
            deny: [SpacePermission.MountManage],
            actorPermissions: [SpacePermission.MountView, SpacePermission.MountManage]);

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountOverwriteTargetType.Role,
            target.PublicId,
            [SpacePermission.FilesWrite],
            [SpacePermission.MountManage],
            mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeTargetLookup()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, mount.Version - 1));

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
        Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
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
    }

    [Test]
    public async Task ExecuteAsync_ShouldRemoveDeletedRoleOverwriteForEmptyReplacement()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(MountOverwriteTargetType.Role, RoleTargetId, allow: [SpacePermission.MountManage]),
            Overwrite(MountOverwriteTargetType.Role, OtherRoleTargetId, allow: [SpacePermission.MountView]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedRole(deletedAt: Now.AddDays(-1));

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: []));

        // Assert
        AssertPermissionResult(result, MountOverwriteTargetType.Role, target.PublicId, [], [], mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Overwrites, Has.Count.EqualTo(1));
            Assert.That(update.Overwrites.Single().TargetId, Is.EqualTo(OtherRoleTargetId));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmptyOverwriteForDeletedRoleWithoutExistingOverwrite()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole(deletedAt: Now.AddDays(-1));

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: []));

        // Assert
        AssertPermissionResult(result, MountOverwriteTargetType.Role, target.PublicId, [], [], mount.Version);
        AssertNoOp(harness);
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
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize", "read-mount" }));
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
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize" }));
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

    [Test]
    public async Task ExecuteAsync_ShouldReturnOverlapErrorWithoutWriting()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: [SpacePermission.FilesRead], deny: [SpacePermission.FilesRead]));

        // Assert
        AssertError(result, ErrorCodes.Mount.PermissionsInvalid);
        AssertRejectedWithoutWrites(harness);
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

    [TestCase(SpacePermission.SpaceView)]
    [TestCase((SpacePermission)(-1))]
    public async Task ExecuteAsync_ShouldReturnPermissionsValidationErrorWithoutDependencyCalls(
        SpacePermission permission)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: [permission]));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.PermissionsInvalid,
            nameof(ReplaceMountRolePermissionsCommand.Allow));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedRole();

        // Act
        var result = await harness.ReplaceRolePermissionsUseCase.ExecuteAsync(
            new ReplaceMountRolePermissionsCommand(null!, target.PublicId, mount.Version, [], []));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.AuthorizationContextRequired,
            nameof(ReplaceMountRolePermissionsCommand.Mount));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(
        MountVisibility visibility = MountVisibility.Public,
        long spaceId = TestMountReadSnapshots.SpaceId,
        IEnumerable<MountPermissionOverwrite>? overwrites = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            spaceId,
            visibility,
            updatedAt: Now.AddHours(-1),
            overwrites: overwrites);

    private static MountPermissionOverwrite Overwrite(
        MountOverwriteTargetType targetType,
        long targetId,
        SpacePermission[]? allow = null,
        SpacePermission[]? deny = null) =>
        TestMountReadSnapshots.Overwrite(targetType, targetId, allow, deny);

    private static ReplaceMountRolePermissionsCommand Command(
        MountMutationTestHarness harness,
        Mount mount,
        Guid targetPublicId,
        long? expectedVersion = null,
        IReadOnlyList<SpacePermission>? allow = null,
        IReadOnlyList<SpacePermission>? deny = null,
        IEnumerable<SpacePermission>? actorPermissions = null) =>
        new(
            harness.MountContext(mount, actorPermissions),
            targetPublicId,
            expectedVersion ?? mount.Version,
            allow ?? [SpacePermission.MountManage],
            deny ?? []);
}
