using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Policies.Authorization;

[TestFixture]
public class SpaceAuthorizationQueryTests
{
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(999)]
    public void Constructor_ShouldRejectUndefinedRequiredPermission(int permission)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TestSpaceAuthorizationSnapshots.Query(
            actor: TestSpaceAuthorizationSnapshots.Session(systemRole: SystemRole.SystemAdmin),
            permissions: [(SpacePermission)permission]));
    }

    [TestCase(-1)]
    [TestCase(999)]
    public void Constructor_ShouldRejectUndefinedAllowedSpaceStatus(int status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TestSpaceAuthorizationSnapshots.Query(statuses: [(SpaceStatus)status]));
    }

    [Test]
    public void Constructor_ShouldRejectEmptyAllowedSpaceStatuses()
    {
        Assert.Throws<ArgumentException>(() => TestSpaceAuthorizationSnapshots.Query(statuses: []));
    }

    [Test]
    public void Constructor_ShouldCopyConfigurationIntoImmutableSets()
    {
        var permissions = new List<SpacePermission> { SpacePermission.SpaceView };
        var statuses = new List<SpaceStatus> { SpaceStatus.Active };
        var query = TestSpaceAuthorizationSnapshots.Query(permissions: permissions, statuses: statuses);

        permissions.Add(SpacePermission.FilesWrite);
        statuses.Add(SpaceStatus.Archived);

        Assert.Multiple(() =>
        {
            Assert.That(query.RequiredPermissions.SetEquals([SpacePermission.SpaceView]), Is.True);
            Assert.That(query.AllowedSpaceStatuses.SetEquals([SpaceStatus.Active]), Is.True);
            Assert.That(() => ((ICollection<SpacePermission>)query.RequiredPermissions).Add(SpacePermission.FilesWrite),
                Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ((ICollection<SpaceStatus>)query.AllowedSpaceStatuses).Clear(),
                Throws.TypeOf<NotSupportedException>());
        });
    }

    [Test]
    public void Snapshot_ShouldCopyRoleCollection()
    {
        var roles = new List<SpaceRole> { TestSpaceAuthorizationSnapshots.Role() };
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(), TestSpaceAuthorizationSnapshots.Membership(), roles);

        roles.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Roles, Has.Count.EqualTo(1));
            Assert.That(() => ((ICollection<SpaceRole>)snapshot.Roles).Clear(),
                Throws.TypeOf<NotSupportedException>());
        });
    }
}
