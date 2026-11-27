using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Mounts.UserPermissionReplacements;
using NUnit.Framework;
using static CloudSharp.Core.Tests.UseCases.Mounts.MountMutationTestHarness;

namespace CloudSharp.Core.Tests.UseCases.Mounts.UserPermissionReplacements;

[TestFixture]
public sealed class ReplaceMountUserPermissionsUseCaseTests
{
    private static readonly DateTimeOffset Now = TestMountReadSnapshots.Now;

    [Test]
    public async Task ExecuteAsync_ShouldReplaceOnlyDesignatedUserOverwrite()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(MountOverwriteTargetType.User, UserTargetId, allow: [SpacePermission.MountView]),
            Overwrite(MountOverwriteTargetType.User, OtherUserTargetId, allow: [SpacePermission.MountView]),
            Overwrite(MountOverwriteTargetType.Role, OtherRoleTargetId, allow: [SpacePermission.MountManage]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedUser();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [SpacePermission.FilesWrite],
            deny: [SpacePermission.MountView]);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionResult(
            result,
            MountOverwriteTargetType.User,
            target.PublicId,
            [SpacePermission.FilesWrite],
            [SpacePermission.MountView],
            mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Overwrites, Has.Count.EqualTo(3));
            Assert.That(update.FindOverwrite(MountOverwriteTargetType.User, target.Id)!.Deny,
                Is.EquivalentTo(new[] { SpacePermission.MountView }));
            Assert.That(update.FindOverwrite(MountOverwriteTargetType.User, OtherUserTargetId)!.Allow,
                Is.EquivalentTo(new[] { SpacePermission.MountView }));
            Assert.That(update.FindOverwrite(MountOverwriteTargetType.Role, OtherRoleTargetId)!.Allow,
                Is.EquivalentTo(new[] { SpacePermission.MountManage }));
            Assert.That(harness.GrantTargetReader.RequestedRolePublicIds, Is.Empty);
            Assert.That(harness.GrantTargetReader.RequestedUserPublicIds, Is.EqualTo(new[] { target.PublicId }));
            Assert.That(harness.Events, Is.EqualTo(
                new[] { "authorize", "read-mount", "read-targets", "stage", "save" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldAllowSpaceOwnerAsTargetWithoutMembership()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var owner = harness.SeedUser(TestMountReadSnapshots.SpaceOwnerUserId, membershipSpaceId: null);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, owner.PublicId, deny: [SpacePermission.FilesWrite], allow: []));

        // Assert
        AssertPermissionResult(
            result,
            MountOverwriteTargetType.User,
            owner.PublicId,
            [],
            [SpacePermission.FilesWrite],
            mount.Version + 1);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPermissionDeniedWhenChangingPermissionActorLacks()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedUser();
        var command = Command(
            harness,
            mount,
            target.PublicId,
            allow: [],
            deny: [SpacePermission.FilesDownload],
            actorPermissions: [SpacePermission.MountView, SpacePermission.MountManage]);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(command);

        // Assert
        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
        AssertRejectedWithoutWrites(harness);
    }

    [TestCase("Missing")]
    [TestCase("CrossSpace")]
    public async Task ExecuteAsync_ShouldReturnResourceNotFoundForMissingOrCrossSpaceTarget(string scenario)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = scenario == "CrossSpace"
            ? harness.SeedUser(membershipSpaceId: TestMountReadSnapshots.SpaceId + 1)
            : null;
        var targetPublicId = target?.PublicId ?? TestMountReadSnapshots.PublicId(900_002);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, targetPublicId));

        // Assert
        AssertError(result, ErrorCodes.Common.ResourceNotFound);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.GrantTargetReader.ReadCount, Is.EqualTo(1));
    }

    [TestCase("SuspendedMember")]
    [TestCase("SuspendedUser")]
    [TestCase("LeftMember")]
    public async Task ExecuteAsync_ShouldReturnRequestConflictForIneligibleTargetWithNonemptyReplacement(
        string scenario)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = scenario switch
        {
            "SuspendedMember" => harness.SeedUser(membershipStatus: SpaceMembershipStatus.Suspended),
            "SuspendedUser" => harness.SeedUser(userStatus: UserStatus.Suspended),
            "LeftMember" => harness.SeedUser(membershipStatus: SpaceMembershipStatus.Left),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId));

        // Assert
        AssertError(result, ErrorCodes.Common.RequestConflict);
        AssertRejectedWithoutWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRemoveSuspendedMemberOverwriteForEmptyReplacement()
    {
        // Arrange
        var mount = StoredMount(overwrites:
        [
            Overwrite(MountOverwriteTargetType.User, UserTargetId, allow: [SpacePermission.MountManage]),
            Overwrite(MountOverwriteTargetType.User, OtherUserTargetId, allow: [SpacePermission.MountView]),
        ]);
        var harness = Administrator(mount);
        var target = harness.SeedUser(membershipStatus: SpaceMembershipStatus.Suspended);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: []));

        // Assert
        AssertPermissionResult(result, MountOverwriteTargetType.User, target.PublicId, [], [], mount.Version + 1);
        AssertSuccessfulWrite(harness, mount.Version);
        var update = harness.Writer.UpdateCalls.Single().Mount;
        Assert.Multiple(() =>
        {
            Assert.That(update.Overwrites, Has.Count.EqualTo(1));
            Assert.That(update.Overwrites.Single().TargetId, Is.EqualTo(OtherUserTargetId));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmptyOverwriteForSuspendedMemberWithoutExistingOverwrite()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedUser(membershipStatus: SpaceMembershipStatus.Suspended);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, allow: []));

        // Assert
        AssertPermissionResult(result, MountOverwriteTargetType.User, target.PublicId, [], [], mount.Version);
        AssertNoOp(harness);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    public async Task ExecuteAsync_ShouldRejectInactiveSpaceBeforeMountRead(SpaceStatus spaceStatus)
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount, spaceStatus);
        var target = harness.SeedUser();

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, mount.Version - 1));

        // Assert
        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.Events, Is.EqualTo(new[] { "authorize" }));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnPreconditionFailureBeforeTargetLookup()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedUser();

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, mount.Version - 1));

        // Assert
        AssertError(result, ErrorCodes.Common.PreconditionFailed);
        AssertRejectedWithoutWrites(harness);
        Assert.That(harness.GrantTargetReader.ReadCount, Is.Zero);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRollbackSaveConflictWithoutCommitting()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedUser();
        var failure = CommonError.PreconditionFailed();
        harness.UnitOfWork.SaveChangesFailureFactory = _ => failure;

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId));

        // Assert
        AssertSaveFailure(harness, result, failure);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnTargetValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, Guid.Empty));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.TargetIdInvalid,
            nameof(ReplaceMountUserPermissionsCommand.UserPublicId));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnDenyValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var mount = StoredMount();
        var harness = Administrator(mount);
        var target = harness.SeedUser();

        // Act
        var result = await harness.ReplaceUserPermissionsUseCase.ExecuteAsync(
            Command(harness, mount, target.PublicId, deny: [SpacePermission.MembersManage]));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Mount.PermissionsInvalid,
            nameof(ReplaceMountUserPermissionsCommand.Deny));
        AssertNoDependencyCalls(harness);
    }

    private static Mount StoredMount(IEnumerable<MountPermissionOverwrite>? overwrites = null) =>
        TestMountReadSnapshots.Mount(
            MountId,
            TestMountReadSnapshots.SpaceId,
            MountVisibility.Public,
            updatedAt: Now.AddHours(-1),
            overwrites: overwrites);

    private static MountPermissionOverwrite Overwrite(
        MountOverwriteTargetType targetType,
        long targetId,
        SpacePermission[]? allow = null,
        SpacePermission[]? deny = null) =>
        TestMountReadSnapshots.Overwrite(targetType, targetId, allow, deny);

    private static ReplaceMountUserPermissionsCommand Command(
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
