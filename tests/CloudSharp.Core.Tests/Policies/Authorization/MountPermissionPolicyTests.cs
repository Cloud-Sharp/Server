using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.Policies.Authorization;

[TestFixture]
public class MountPermissionPolicyTests
{
    private static readonly SpacePermission[] ViewerBase =
    [
        SpacePermission.SpaceView,
        SpacePermission.MountView,
        SpacePermission.FilesRead,
        SpacePermission.FilesDownload,
    ];

    [Test]
    public void Evaluate_ShouldUseMountScopedSpacePermissionsAsBase()
    {
        var actor = Actor();
        var role = Role(11, permissions: [.. ViewerBase, SpacePermission.MembersView]);
        var mount = Mount(1);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [role]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.EquivalentTo(new[]
        {
            SpacePermission.MountView,
            SpacePermission.FilesRead,
            SpacePermission.FilesDownload,
        }));
    }

    [TestCase("Owner")]
    [TestCase("Admin")]
    public void Evaluate_ShouldBypassOverwritesForAdministrators(string scenario)
    {
        var actor = Actor();
        var mount = Mount(
            2,
            visibility: MountVisibility.Private,
            overwrites: [Overwrite(MountOverwriteTargetType.User, actor.UserId, deny: [SpacePermission.MountView])]);
        var snapshot = scenario == "Owner"
            ? Owner(actor)
            : Member(actor, [Role(12, systemKey: SpaceRoleSystemKey.Admin)]);

        var evaluation = MountPermissionPolicy.Evaluate(snapshot, actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.EquivalentTo(MountScopedPermissions.All));
        Assert.That(evaluation.Sources.Single().SourceType, Is.EqualTo(MountPermissionSourceType.Administrator));
    }

    [Test]
    public void Evaluate_ShouldHidePrivateMountFromBaseView()
    {
        var actor = Actor();
        var mount = Mount(3, visibility: MountVisibility.Private);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [Role(13, permissions: ViewerBase)]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.Empty);
    }

    [Test]
    public void Evaluate_ShouldApplyRoleDenyBeforeRoleAllowAcrossAllRoles()
    {
        var actor = Actor();
        var denying = Role(14, permissions: ViewerBase);
        var allowing = Role(15, permissions: ViewerBase);
        var mount = Mount(
            4,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.Role, denying.Id, deny: [SpacePermission.FilesDownload, SpacePermission.FilesRead]),
                Overwrite(MountOverwriteTargetType.Role, allowing.Id, allow: [SpacePermission.FilesDownload]),
            ]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [denying, allowing]), actor.UserId, mount);

        // FilesRead stays denied; FilesDownload is allowed by another role but then removed by the implicit read rule.
        Assert.That(evaluation.Permissions, Is.EquivalentTo(new[] { SpacePermission.MountView }));
    }

    [Test]
    public void Evaluate_ShouldLetRoleAllowWinOverAnotherRoleDeny()
    {
        var actor = Actor();
        var denying = Role(16, permissions: ViewerBase);
        var allowing = Role(17, permissions: ViewerBase);
        var mount = Mount(
            5,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.Role, denying.Id, deny: [SpacePermission.FilesWrite]),
                Overwrite(MountOverwriteTargetType.Role, allowing.Id, allow: [SpacePermission.FilesWrite]),
            ]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [denying, allowing]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Does.Contain(SpacePermission.FilesWrite));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Evaluate_ShouldApplyUserOverwriteAfterRoleOverwrites(bool userAllows)
    {
        var actor = Actor();
        var role = Role(18, permissions: ViewerBase);
        var mount = Mount(
            6,
            overwrites:
            [
                Overwrite(
                    MountOverwriteTargetType.Role,
                    role.Id,
                    allow: userAllows ? [] : [SpacePermission.FilesWrite],
                    deny: userAllows ? [SpacePermission.FilesWrite] : []),
                Overwrite(
                    MountOverwriteTargetType.User,
                    actor.UserId,
                    allow: userAllows ? [SpacePermission.FilesWrite] : [],
                    deny: userAllows ? [] : [SpacePermission.FilesWrite]),
            ]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [role]), actor.UserId, mount);

        Assert.That(evaluation.Permissions.Contains(SpacePermission.FilesWrite), Is.EqualTo(userAllows));
    }

    [Test]
    public void Evaluate_ShouldRevealPrivateMountThroughOverwrite()
    {
        var actor = Actor();
        var role = Role(19, permissions: ViewerBase);
        var mount = Mount(
            7,
            visibility: MountVisibility.Private,
            overwrites: [Overwrite(MountOverwriteTargetType.Role, role.Id, allow: [SpacePermission.MountView])]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [role]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.EquivalentTo(new[]
        {
            SpacePermission.MountView,
            SpacePermission.FilesRead,
            SpacePermission.FilesDownload,
        }));
        Assert.That(evaluation.Sources.Select(source => source.SourceType), Is.EqualTo(new[]
        {
            MountPermissionSourceType.Base,
            MountPermissionSourceType.Visibility,
            MountPermissionSourceType.Role,
        }));
    }

    [Test]
    public void Evaluate_ShouldClearEveryPermissionWhenMountViewIsDenied()
    {
        var actor = Actor();
        var role = Role(20, permissions: [.. ViewerBase, SpacePermission.FilesWrite, SpacePermission.MountManage]);
        var mount = Mount(
            8,
            overwrites: [Overwrite(MountOverwriteTargetType.User, actor.UserId, deny: [SpacePermission.MountView])]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [role]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.Empty);
    }

    [Test]
    public void Evaluate_ShouldRemoveDownloadAndWriteWithoutFilesRead()
    {
        var actor = Actor();
        var role = Role(21, permissions: [.. ViewerBase, SpacePermission.FilesWrite, SpacePermission.MountManage]);
        var mount = Mount(
            9,
            overwrites: [Overwrite(MountOverwriteTargetType.Role, role.Id, deny: [SpacePermission.FilesRead])]);

        var evaluation = MountPermissionPolicy.Evaluate(Member(actor, [role]), actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.EquivalentTo(new[]
        {
            SpacePermission.MountView,
            SpacePermission.MountManage,
        }));
    }

    [Test]
    public void Evaluate_ShouldRemoveWriteAndManagementInArchivedSpace()
    {
        var actor = Actor();
        var role = Role(22, permissions: [.. ViewerBase, SpacePermission.FilesWrite, SpacePermission.MountManage]);
        var mount = Mount(10);

        var evaluation = MountPermissionPolicy.Evaluate(
            Member(actor, [role], spaceStatus: SpaceStatus.Archived),
            actor.UserId,
            mount);

        Assert.That(evaluation.Permissions, Is.EquivalentTo(new[]
        {
            SpacePermission.MountView,
            SpacePermission.FilesRead,
            SpacePermission.FilesDownload,
        }));
    }

    [Test]
    public void Evaluate_ShouldUseProposedVisibility()
    {
        var actor = Actor();
        var mount = Mount(11);

        var evaluation = MountPermissionPolicy.Evaluate(
            Member(actor, [Role(23, permissions: ViewerBase)]),
            actor.UserId,
            mount,
            MountVisibility.Private);

        Assert.That(evaluation.Permissions, Is.Empty);
    }

    [TestCase("NoMembership")]
    [TestCase("SuspendedMember")]
    [TestCase("OtherSpaceMount")]
    public void Evaluate_ShouldReturnNoPermissionsWithoutSpaceAccess(string scenario)
    {
        var actor = Actor();
        var snapshot = scenario switch
        {
            "NoMembership" => new SpaceAuthorizationSnapshot(Space(), null, []),
            "SuspendedMember" => Member(actor, membershipStatus: SpaceMembershipStatus.Suspended),
            _ => Member(actor),
        };
        var mount = Mount(12, spaceId: scenario == "OtherSpaceMount" ? SpaceId + 1 : SpaceId);

        var evaluation = MountPermissionPolicy.Evaluate(snapshot, actor.UserId, mount);

        Assert.That(evaluation.Permissions, Is.Empty);
        Assert.That(evaluation.Sources, Is.Empty);
    }

    [Test]
    public void Evaluate_ShouldIgnoreDeletedAndUnassignedRoleOverwrites()
    {
        var actor = Actor();
        var assigned = Role(24, permissions: ViewerBase);
        var deleted = Role(25, deletedAt: Now, permissions: ViewerBase);
        var unassigned = Role(26, permissions: ViewerBase);
        var mount = Mount(
            13,
            overwrites:
            [
                Overwrite(MountOverwriteTargetType.Role, deleted.Id, deny: [SpacePermission.MountView]),
                Overwrite(MountOverwriteTargetType.Role, unassigned.Id, deny: [SpacePermission.MountView]),
            ]);
        var snapshot = Member(actor, [assigned, deleted, unassigned], assignedRoleIds: [assigned.Id, deleted.Id]);

        var evaluation = MountPermissionPolicy.Evaluate(snapshot, actor.UserId, mount);

        Assert.That(evaluation.Permissions, Does.Contain(SpacePermission.MountView));
    }

    [Test]
    public void CanChangeOverwrite_ShouldAllowOnlyChangesWithinActorPermissions()
    {
        var actorPermissions = new HashSet<SpacePermission> { SpacePermission.MountView, SpacePermission.FilesRead };
        var current = Overwrite(MountOverwriteTargetType.Role, 1, allow: [SpacePermission.FilesWrite]);
        var keepsUnownedPermission = Overwrite(
            MountOverwriteTargetType.Role,
            1,
            allow: [SpacePermission.FilesWrite],
            deny: [SpacePermission.FilesRead]);
        var removesUnownedPermission = Overwrite(MountOverwriteTargetType.Role, 1, deny: [SpacePermission.FilesRead]);

        Assert.Multiple(() =>
        {
            Assert.That(MountPermissionPolicy.CanChangeOverwrite(actorPermissions, current, keepsUnownedPermission), Is.True);
            Assert.That(MountPermissionPolicy.CanChangeOverwrite(actorPermissions, current, removesUnownedPermission), Is.False);
            Assert.That(MountPermissionPolicy.CanChangeOverwrite(actorPermissions, null, current), Is.False);
        });
    }
}
