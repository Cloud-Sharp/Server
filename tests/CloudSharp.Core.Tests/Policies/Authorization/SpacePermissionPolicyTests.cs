using System.Collections.Frozen;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Policies.Authorization;

[TestFixture]
public class SpacePermissionPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 3, 30, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<SpacePermission> AllDefined =
        Enum.GetValues<SpacePermission>().ToFrozenSet();

    [Test]
    public void Evaluate_WithOwnerWithoutMembership_ShouldReturnEveryDefinedPermission()
    {
        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(ownerUserId: 21), 21, null, []);

        Assert.That(effective.SetEquals(AllDefined), Is.True);
    }

    [Test]
    public void Evaluate_WithSuspendedMembership_ForOwner_ShouldStillReturnEveryDefinedPermission()
    {
        var membership = RestoreMembership(21, 7, SpaceMembershipStatus.Suspended, 101);

        var effective = SpacePermissionPolicy.Evaluate(
            RestoreSpace(ownerUserId: 21), 21, membership,
            [RestoreRole(101, permissions: [SpacePermission.SpaceView])]);

        Assert.That(effective.SetEquals(AllDefined), Is.True);
    }

    [TestCaseSource(nameof(AdminStoredPermissionCases))]
    public void Evaluate_WithAssignedAdminRole_ShouldReturnEveryDefinedPermission(
        SpacePermission[] storedPermissions)
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);
        var roles = new[]
        {
            RestoreRole(101, name: "Admin", systemKey: SpaceRoleSystemKey.Admin, permissions: storedPermissions),
        };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective.SetEquals(AllDefined), Is.True);
    }

    [Test]
    public void Evaluate_WithMultipleRoles_ShouldReturnUnionOfPermissions()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101, 102);
        var roles = new[]
        {
            RestoreRole(101, permissions: [SpacePermission.SpaceView, SpacePermission.FilesRead]),
            RestoreRole(102,
                permissions: [SpacePermission.FilesRead, SpacePermission.FilesWrite, SpacePermission.MembersView]),
        };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective.SetEquals([
            SpacePermission.SpaceView,
            SpacePermission.FilesRead,
            SpacePermission.FilesWrite,
            SpacePermission.MembersView,
        ]), Is.True);
    }

    [Test]
    public void Evaluate_WithoutMembership_ShouldReturnEmpty()
    {
        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, null, [RestoreRole(101)]);

        Assert.That(effective, Is.Empty);
    }

    [TestCase("user")]
    [TestCase("space")]
    public void Evaluate_WithForeignMembership_ShouldReturnEmpty(string mismatch)
    {
        var membership = mismatch == "user"
            ? RestoreMembership(userId: 43, spaceId: 7, status: SpaceMembershipStatus.Active, roleIds: [101])
            : RestoreMembership(userId: 42, spaceId: 8, status: SpaceMembershipStatus.Active, roleIds: [101]);

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, [RestoreRole(101)]);

        Assert.That(effective, Is.Empty);
    }

    [TestCase(SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceMembershipStatus.Left)]
    public void Evaluate_OnInactiveMembership_ShouldReturnEmpty(SpaceMembershipStatus status)
    {
        // 정지·탈퇴한 멤버에게는 ADMIN 역할이 남아 있어도 권한을 부여하지 않는다.
        var membership = RestoreMembership(42, 7, status, 101);
        var roles = new[]
        {
            RestoreRole(101, name: "Admin", systemKey: SpaceRoleSystemKey.Admin, permissions: []),
        };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective, Is.Empty);
    }

    [Test]
    public void Evaluate_WithUnassignedAdminRole_ShouldNotGrantItsPermissions()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);
        var roles = new[]
        {
            RestoreRole(101, permissions: [SpacePermission.SpaceView]),
            RestoreRole(102, name: "Admin", systemKey: SpaceRoleSystemKey.Admin, permissions: []),
        };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective.SetEquals([SpacePermission.SpaceView]), Is.True);
    }

    [Test]
    public void Evaluate_WithMissingRoleSnapshot_ShouldSkipThatRole()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101, 102);
        var roles = new[] { RestoreRole(101, permissions: [SpacePermission.SpaceView]) };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective.SetEquals([SpacePermission.SpaceView]), Is.True);
    }

    [TestCaseSource(nameof(ExcludedRoles))]
    public void Evaluate_WithExcludedRole_ShouldNotGrantItsPermissions(SpaceRole role)
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, [role]);

        Assert.That(effective, Is.Empty);
    }

    [Test]
    public void Evaluate_ShouldDependOnStoredPermissionsNotRoleName()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);
        var roles = new[]
        {
            RestoreRole(101, name: "Admin", systemKey: null, permissions: [SpacePermission.SpaceView]),
        };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        Assert.That(effective.SetEquals([SpacePermission.SpaceView]), Is.True);
    }

    [TestCase(0)]
    [TestCase(-5)]
    public void Evaluate_WithNonPositiveUserId_ShouldReturnEmpty(long userId)
    {
        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), userId, null, [RestoreRole(101)]);

        Assert.That(effective, Is.Empty);
    }

    [Test]
    public void Evaluate_WithUnsavedSpace_ShouldReturnEmpty()
    {
        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(id: 0), 21, null, [RestoreRole(101)]);

        Assert.That(effective, Is.Empty);
    }

    [Test]
    public void Evaluate_WithNullSpace_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SpacePermissionPolicy.Evaluate(null!, 42, null, []));
    }

    [Test]
    public void Evaluate_WithNullRoles_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, null, null!));
    }

    [Test]
    public void Evaluate_WithNullRoleElement_ShouldThrowArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, null, [RestoreRole(101), null!]));
    }

    [Test]
    public void Evaluate_WithDuplicatePersistedRoleIds_ForOwner_ShouldThrowArgumentException()
    {
        // 구조적 입력 검증은 OWNER 조기 반환보다 먼저 수행한다.
        SpaceRole[] roles = [RestoreRole(101), RestoreRole(101, name: "Auditor", systemKey: null)];

        Assert.Throws<ArgumentException>(() =>
            SpacePermissionPolicy.Evaluate(RestoreSpace(ownerUserId: 21), 21, null, roles));
    }

    [Test]
    public void Evaluate_WithUnpersistedDuplicateRoles_ShouldNotThrow()
    {
        Assert.DoesNotThrow(() => SpacePermissionPolicy.Evaluate(
            RestoreSpace(), 42, null,
            [
                SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Member, Now).Value,
                SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Viewer, Now).Value,
            ]));
    }

    [Test]
    public void Evaluate_ShouldReturnFrozenSetResistantToMutation()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);
        var memberEffective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, [RestoreRole(101)]);
        var ownerEffective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 21, null, []);

        var memberCollection = (ICollection<SpacePermission>)memberEffective;
        var ownerCollection = (ICollection<SpacePermission>)ownerEffective;

        Assert.Multiple(() =>
        {
            Assert.That(() => memberCollection.Add(SpacePermission.TagsManage),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(() => memberCollection.Clear(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ownerCollection.Add(SpacePermission.TagsManage),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ownerCollection.Clear(), Throws.TypeOf<NotSupportedException>());
        });
        Assert.That(memberEffective.SetEquals([SpacePermission.SpaceView, SpacePermission.FilesRead]), Is.True);
        Assert.That(ownerEffective.SetEquals(AllDefined), Is.True);
    }

    [Test]
    public void Evaluate_ShouldReturnSnapshotIndependentFromSourceCollections()
    {
        var membership = RestoreMembership(42, 7, SpaceMembershipStatus.Active, 101);
        var roles = new List<SpaceRole> { RestoreRole(101, permissions: [SpacePermission.SpaceView]) };

        var effective = SpacePermissionPolicy.Evaluate(RestoreSpace(), 42, membership, roles);

        roles.Add(RestoreRole(102, permissions: [SpacePermission.TagsManage]));
        roles.Clear();

        Assert.That(effective.SetEquals([SpacePermission.SpaceView]), Is.True);
    }

    [Test]
    public void HasAll_WhenEveryRequiredPermissionIsPresent_ShouldReturnTrue()
    {
        Assert.That(SpacePermissionPolicy.HasAll(
            AllDefined,
            [SpacePermission.SpaceView, SpacePermission.FilesRead, SpacePermission.MembersManage]), Is.True);
    }

    [Test]
    public void HasAll_WhenAnyRequiredPermissionIsMissing_ShouldReturnFalse()
    {
        var effective = new HashSet<SpacePermission>
        {
            SpacePermission.SpaceView,
            SpacePermission.FilesRead,
        }.ToFrozenSet();

        Assert.That(SpacePermissionPolicy.HasAll(
            effective, [SpacePermission.SpaceView, SpacePermission.TagsManage]), Is.False);
    }

    [Test]
    public void HasAll_WithEmptyRequirements_ShouldReturnTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SpacePermissionPolicy.HasAll(AllDefined, []), Is.True);
            Assert.That(SpacePermissionPolicy.HasAll(FrozenSet<SpacePermission>.Empty, []), Is.True);
        });
    }

    [Test]
    public void HasAll_WithUndefinedPermission_ShouldReturnFalse()
    {
        Assert.That(SpacePermissionPolicy.HasAll(AllDefined, [(SpacePermission)99]), Is.False);
    }

    [TestCase("effective")]
    [TestCase("required")]
    public void HasAll_WithNullArgument_ShouldThrowArgumentNullException(string argument)
    {
        if (argument == "effective")
        {
            Assert.Throws<ArgumentNullException>(() =>
                SpacePermissionPolicy.HasAll(null!, [SpacePermission.SpaceView]));
        }
        else
        {
            Assert.Throws<ArgumentNullException>(() =>
                SpacePermissionPolicy.HasAll(AllDefined, null!));
        }
    }

    private static IEnumerable<TestCaseData> AdminStoredPermissionCases()
    {
        yield return new TestCaseData(Array.Empty<SpacePermission>())
            .SetArgDisplayNames("empty stored set");
        yield return new TestCaseData(new[] { SpacePermission.SpaceView })
            .SetArgDisplayNames("partial stored set");
        yield return new TestCaseData(Enum.GetValues<SpacePermission>())
            .SetArgDisplayNames("complete stored set");
    }

    private static IEnumerable<TestCaseData> ExcludedRoles()
    {
        yield return ExcludedRole("role from another space", RestoreRole(101, spaceId: 8));
        yield return ExcludedRole("deleted role", RestoreRole(101, deletedAt: Now.AddHours(-1)));
        yield return ExcludedRole("unpersisted role",
            SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Admin, Now).Value);
    }

    private static TestCaseData ExcludedRole(string name, SpaceRole role) =>
        new TestCaseData(role).SetArgDisplayNames(name);

    private static Space RestoreSpace(long id = 7, long ownerUserId = 21) =>
        Space.Reconstitute(
            id, Guid.CreateVersion7(), ownerUserId, "Product", null, SpaceStatus.Active,
            null, 0, 0, 1, Now.AddDays(-7), Now, null, null, null);

    private static SpaceMembership RestoreMembership(
        long userId,
        long spaceId,
        SpaceMembershipStatus status,
        params long[] roleIds) =>
        SpaceMembership.Reconstitute(
            55, Guid.CreateVersion7(), spaceId, userId, status, 3,
            Now.AddDays(-3), Now,
            status == SpaceMembershipStatus.Suspended ? Now : null,
            status == SpaceMembershipStatus.Left ? Now : null,
            roleIds.Select(id => MembershipRoleAssignment.Create(id, 21, Now.AddDays(-3))).ToArray());

    private static SpaceRole RestoreRole(
        long id = 101,
        long spaceId = 7,
        string name = "Member",
        SpaceRoleSystemKey? systemKey = SpaceRoleSystemKey.Member,
        DateTimeOffset? deletedAt = null,
        IEnumerable<SpacePermission>? permissions = null) =>
        SpaceRole.Reconstitute(
            id, Guid.CreateVersion7(), spaceId, name, name.Trim().ToUpperInvariant(), systemKey,
            null, 1, true, 4, Now.AddDays(-2), Now, deletedAt,
            permissions ?? [SpacePermission.SpaceView, SpacePermission.FilesRead]);
}
