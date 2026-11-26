using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.Entities;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Entities;

[TestFixture]
public sealed class MountEntityMappingTests
{
    private const long SpaceId = 21;
    private const long TargetId = 31;
    private static readonly Guid PublicId = new("019a9808-68b2-7000-8000-000000000002");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 26, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [TestCase(0L, MountVisibility.Public, MountStatus.Active)]
    [TestCase(41L, MountVisibility.Private, MountStatus.Archived)]
    public void MountEntity_ShouldRoundTripStoredFieldsAndBothGrantTargets(
        long id,
        MountVisibility visibility,
        MountStatus status)
    {
        var roleGrant = CreateGrant(MountGrantTargetType.Role, MountPermission.FilesRead, MountPermission.FilesWrite);
        var userGrant = CreateGrant(MountGrantTargetType.User, MountPermission.FilesDownload);
        var mount = Mount.Reconstitute(
            id, PublicId, SpaceId, " Project files ", visibility, status, 4,
            CreatedAt, UpdatedAt, status == MountStatus.Archived ? UpdatedAt : null, [roleGrant, userGrant]);

        var entity = MountEntity.FromDomain(mount);
        var roleEntity = MountRoleGrantEntity.FromDomain(entity, roleGrant);
        var userEntity = MountUserGrantEntity.FromDomain(entity, userGrant);
        var restored = entity.ToDomain([roleEntity], [userEntity]);
        var restoredRoleGrant = restored.Grants.Single(grant => grant.TargetType == MountGrantTargetType.Role);
        var restoredUserGrant = restored.Grants.Single(grant => grant.TargetType == MountGrantTargetType.User);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(roleEntity.MountId, Is.EqualTo(id));
            Assert.That(roleEntity.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(roleEntity.RoleId, Is.EqualTo(TargetId));
            Assert.That(roleEntity.Mount, Is.SameAs(entity));
            Assert.That(roleEntity.PermissionMask, Is.EqualTo(3L));
            Assert.That(userEntity.MountId, Is.EqualTo(id));
            Assert.That(userEntity.UserId, Is.EqualTo(TargetId));
            Assert.That(userEntity.Mount, Is.SameAs(entity));
            Assert.That(restored, Is.Not.SameAs(mount));
            Assert.That(restored.Id, Is.EqualTo(id));
            Assert.That(restored.PublicId, Is.EqualTo(PublicId));
            Assert.That(restored.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(restored.Name, Is.EqualTo(mount.Name));
            Assert.That(restored.Visibility, Is.EqualTo(visibility));
            Assert.That(restored.Status, Is.EqualTo(status));
            Assert.That(restored.Version, Is.EqualTo(4L));
            Assert.That(restored.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(restored.UpdatedAt, Is.EqualTo(UpdatedAt));
            Assert.That(restored.ArchivedAt, Is.EqualTo(mount.ArchivedAt));
            Assert.That(restored.Grants, Has.Count.EqualTo(2));
            Assert.That(restoredRoleGrant.TargetId, Is.EqualTo(TargetId));
            Assert.That(restoredRoleGrant.Permissions, Is.EquivalentTo(roleGrant.Permissions));
            Assert.That(restoredUserGrant.TargetId, Is.EqualTo(TargetId));
            Assert.That(restoredUserGrant.Permissions, Is.EquivalentTo(userGrant.Permissions));
        });
    }

    [Test]
    public void MountEntity_UpdateFrom_ShouldPreserveIdAndLeaveEarlierSnapshotUnchanged()
    {
        var mount = Mount.Reconstitute(
            41, PublicId, SpaceId, "Project files", MountVisibility.Private, MountStatus.Archived, 4,
            CreatedAt, UpdatedAt, UpdatedAt, []);
        var entity = MountEntity.FromDomain(mount);
        var earlierSnapshot = entity.ToDomain([], []);
        var reactivatedAt = UpdatedAt.AddHours(1);
        var activeMount = Mount.Reconstitute(
            0, PublicId, SpaceId, mount.Name, mount.Visibility, MountStatus.Active, 5,
            CreatedAt, reactivatedAt, null, []);

        entity.UpdateFrom(activeMount);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(41));
            Assert.That(entity.Status, Is.EqualTo(MountStatus.Active));
            Assert.That(entity.Version, Is.EqualTo(5L));
            Assert.That(entity.UpdatedAt, Is.EqualTo(reactivatedAt));
            Assert.That(entity.ArchivedAt, Is.Null);
            Assert.That(earlierSnapshot.Status, Is.EqualTo(MountStatus.Archived));
            Assert.That(earlierSnapshot.ArchivedAt, Is.EqualTo(UpdatedAt));
        });
    }

    [TestCase(MountPermission.FilesRead, 1L)]
    [TestCase(MountPermission.FilesWrite, 2L)]
    [TestCase(MountPermission.FilesDownload, 4L)]
    public void GrantEntities_ShouldRoundTripEachPermissionAtItsStoredBit(MountPermission permission, long expectedMask)
    {
        var mount = new MountEntity { Id = 41, SpaceId = SpaceId };
        var roleEntity = MountRoleGrantEntity.FromDomain(mount, CreateGrant(MountGrantTargetType.Role, permission));
        var userEntity = MountUserGrantEntity.FromDomain(mount, CreateGrant(MountGrantTargetType.User, permission));

        Assert.Multiple(() =>
        {
            Assert.That(roleEntity.PermissionMask, Is.EqualTo(expectedMask));
            Assert.That(userEntity.PermissionMask, Is.EqualTo(expectedMask));
            Assert.That(roleEntity.ToDomain().Permissions, Is.EquivalentTo(new[] { permission }));
            Assert.That(userEntity.ToDomain().Permissions, Is.EquivalentTo(new[] { permission }));
        });
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    [TestCase(9L)]
    public void GrantEntities_ToDomain_ShouldRejectInvalidStoredPermissionMask(long mask)
    {
        var roleEntity = new MountRoleGrantEntity
        {
            MountId = 41,
            SpaceId = SpaceId,
            RoleId = TargetId,
            PermissionMask = mask,
        };
        var userEntity = new MountUserGrantEntity { MountId = 41, UserId = TargetId, PermissionMask = mask };

        Assert.Multiple(() =>
        {
            Assert.That(() => roleEntity.ToDomain(), Throws.InvalidOperationException);
            Assert.That(() => userEntity.ToDomain(), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void GrantEntities_UpdateFrom_ShouldReplacePermissionsAndPreserveKeys()
    {
        var mount = new MountEntity { Id = 41, SpaceId = SpaceId };
        var roleEntity = MountRoleGrantEntity.FromDomain(
            mount, CreateGrant(MountGrantTargetType.Role, MountPermission.FilesRead));
        var userEntity = MountUserGrantEntity.FromDomain(
            mount, CreateGrant(MountGrantTargetType.User, MountPermission.FilesWrite));

        roleEntity.UpdateFrom(CreateGrant(MountGrantTargetType.Role, MountPermission.FilesWrite));
        userEntity.UpdateFrom(CreateGrant(MountGrantTargetType.User, MountPermission.FilesDownload));

        Assert.Multiple(() =>
        {
            Assert.That(roleEntity.MountId, Is.EqualTo(mount.Id));
            Assert.That(roleEntity.RoleId, Is.EqualTo(TargetId));
            Assert.That(roleEntity.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(roleEntity.PermissionMask, Is.EqualTo(2L));
            Assert.That(userEntity.MountId, Is.EqualTo(mount.Id));
            Assert.That(userEntity.UserId, Is.EqualTo(TargetId));
            Assert.That(userEntity.PermissionMask, Is.EqualTo(4L));
        });
    }

    [Test]
    public void GrantEntities_UpdateFrom_ShouldRejectAChangedTargetIdOrType()
    {
        var roleEntity = new MountRoleGrantEntity { RoleId = TargetId, PermissionMask = 1L };
        var userEntity = new MountUserGrantEntity { UserId = TargetId, PermissionMask = 1L };
        var otherRoleTarget = MountAccessGrant.Create(
            MountGrantTargetType.Role, TargetId + 1, [MountPermission.FilesWrite]).Value;
        var otherUserTarget = MountAccessGrant.Create(
            MountGrantTargetType.User, TargetId + 1, [MountPermission.FilesWrite]).Value;
        var roleGrant = CreateGrant(MountGrantTargetType.Role, MountPermission.FilesWrite);
        var userGrant = CreateGrant(MountGrantTargetType.User, MountPermission.FilesWrite);

        Assert.Multiple(() =>
        {
            Assert.That(() => roleEntity.UpdateFrom(otherRoleTarget), Throws.ArgumentException);
            Assert.That(() => roleEntity.UpdateFrom(userGrant), Throws.ArgumentException);
            Assert.That(() => userEntity.UpdateFrom(otherUserTarget), Throws.ArgumentException);
            Assert.That(() => userEntity.UpdateFrom(roleGrant), Throws.ArgumentException);
            Assert.That(roleEntity.PermissionMask, Is.EqualTo(1L));
            Assert.That(userEntity.PermissionMask, Is.EqualTo(1L));
        });
    }

    private static MountAccessGrant CreateGrant(MountGrantTargetType targetType, params MountPermission[] permissions) =>
        MountAccessGrant.Create(targetType, TargetId, permissions).Value;
}
