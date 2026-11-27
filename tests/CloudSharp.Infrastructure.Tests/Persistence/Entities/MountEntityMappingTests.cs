using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.Entities;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Entities;

[TestFixture]
public sealed class MountEntityMappingTests
{
    private const long SpaceId = 21;
    private const long TargetId = 31;
    private const long FilesReadBit = 1L << 2;
    private const long FilesWriteBit = 1L << 3;
    private const long FilesDownloadBit = 1L << 4;
    private const long MountManageBit = 1L << 13;
    private const long MountViewBit = 1L << 14;
    private static readonly Guid PublicId = new("019a9808-68b2-7000-8000-000000000002");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 26, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [TestCase(0L, MountVisibility.Public, MountStatus.Active)]
    [TestCase(41L, MountVisibility.Private, MountStatus.Archived)]
    public void MountEntity_ShouldRoundTripStoredFieldsAndBothOverwriteTargets(
        long id,
        MountVisibility visibility,
        MountStatus status)
    {
        var roleOverwrite = CreateOverwrite(
            MountOverwriteTargetType.Role,
            allow: [SpacePermission.MountView, SpacePermission.MountManage],
            deny: [SpacePermission.FilesWrite]);
        var userOverwrite = CreateOverwrite(MountOverwriteTargetType.User, deny: [SpacePermission.MountView]);
        var mount = Mount.Reconstitute(
            id, PublicId, SpaceId, " Project files ", visibility, status, 4,
            CreatedAt, UpdatedAt, status == MountStatus.Archived ? UpdatedAt : null, [roleOverwrite, userOverwrite]);

        var entity = MountEntity.FromDomain(mount);
        var roleEntity = MountRoleGrantEntity.FromDomain(entity, roleOverwrite);
        var userEntity = MountUserGrantEntity.FromDomain(entity, userOverwrite);
        var restored = entity.ToDomain([roleEntity], [userEntity]);
        var restoredRole = restored.FindOverwrite(MountOverwriteTargetType.Role, TargetId)!;
        var restoredUser = restored.FindOverwrite(MountOverwriteTargetType.User, TargetId)!;

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(roleEntity.MountId, Is.EqualTo(id));
            Assert.That(roleEntity.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(roleEntity.RoleId, Is.EqualTo(TargetId));
            Assert.That(roleEntity.Mount, Is.SameAs(entity));
            Assert.That(roleEntity.AllowMask, Is.EqualTo(MountViewBit | MountManageBit));
            Assert.That(roleEntity.DenyMask, Is.EqualTo(FilesWriteBit));
            Assert.That(userEntity.MountId, Is.EqualTo(id));
            Assert.That(userEntity.UserId, Is.EqualTo(TargetId));
            Assert.That(userEntity.Mount, Is.SameAs(entity));
            Assert.That(userEntity.AllowMask, Is.Zero);
            Assert.That(userEntity.DenyMask, Is.EqualTo(MountViewBit));
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
            Assert.That(restored.Overwrites, Has.Count.EqualTo(2));
            Assert.That(restoredRole.Allow, Is.EquivalentTo(roleOverwrite.Allow));
            Assert.That(restoredRole.Deny, Is.EquivalentTo(roleOverwrite.Deny));
            Assert.That(restoredUser.Allow, Is.Empty);
            Assert.That(restoredUser.Deny, Is.EquivalentTo(userOverwrite.Deny));
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

    [TestCase(SpacePermission.MountView, MountViewBit)]
    [TestCase(SpacePermission.MountManage, MountManageBit)]
    [TestCase(SpacePermission.FilesRead, FilesReadBit)]
    [TestCase(SpacePermission.FilesWrite, FilesWriteBit)]
    [TestCase(SpacePermission.FilesDownload, FilesDownloadBit)]
    public void OverwriteEntities_ShouldUseSpacePermissionBitsForAllowAndDeny(
        SpacePermission permission,
        long expectedMask)
    {
        var mount = new MountEntity { Id = 41, SpaceId = SpaceId };
        var roleEntity = MountRoleGrantEntity.FromDomain(
            mount, CreateOverwrite(MountOverwriteTargetType.Role, allow: [permission]));
        var userEntity = MountUserGrantEntity.FromDomain(
            mount, CreateOverwrite(MountOverwriteTargetType.User, deny: [permission]));

        Assert.Multiple(() =>
        {
            Assert.That(roleEntity.AllowMask, Is.EqualTo(expectedMask));
            Assert.That(roleEntity.DenyMask, Is.Zero);
            Assert.That(userEntity.AllowMask, Is.Zero);
            Assert.That(userEntity.DenyMask, Is.EqualTo(expectedMask));
            Assert.That(roleEntity.ToDomain().Allow, Is.EquivalentTo(new[] { permission }));
            Assert.That(userEntity.ToDomain().Deny, Is.EquivalentTo(new[] { permission }));
        });
    }

    [TestCase(0L, 0L)]
    [TestCase(1L, 0L)]
    [TestCase(0L, 1L << 9)]
    [TestCase(MountViewBit, MountViewBit)]
    [TestCase(-1L, 0L)]
    [TestCase(1L << 20, 0L)]
    public void OverwriteEntities_ToDomain_ShouldRejectInvalidStoredMasks(long allowMask, long denyMask)
    {
        var roleEntity = new MountRoleGrantEntity
        {
            MountId = 41,
            SpaceId = SpaceId,
            RoleId = TargetId,
            AllowMask = allowMask,
            DenyMask = denyMask,
        };
        var userEntity = new MountUserGrantEntity
        {
            MountId = 41,
            UserId = TargetId,
            AllowMask = allowMask,
            DenyMask = denyMask,
        };

        Assert.Multiple(() =>
        {
            Assert.That(() => roleEntity.ToDomain(), Throws.InstanceOf<Exception>());
            Assert.That(() => userEntity.ToDomain(), Throws.InstanceOf<Exception>());
        });
    }

    [Test]
    public void OverwriteEntities_UpdateFrom_ShouldReplaceMasksAndPreserveKeys()
    {
        var mount = new MountEntity { Id = 41, SpaceId = SpaceId };
        var roleEntity = MountRoleGrantEntity.FromDomain(
            mount, CreateOverwrite(MountOverwriteTargetType.Role, allow: [SpacePermission.MountView]));
        var userEntity = MountUserGrantEntity.FromDomain(
            mount, CreateOverwrite(MountOverwriteTargetType.User, allow: [SpacePermission.MountManage]));

        roleEntity.UpdateFrom(CreateOverwrite(MountOverwriteTargetType.Role, deny: [SpacePermission.MountManage]));
        userEntity.UpdateFrom(CreateOverwrite(
            MountOverwriteTargetType.User,
            allow: [SpacePermission.MountView],
            deny: [SpacePermission.FilesDownload]));

        Assert.Multiple(() =>
        {
            Assert.That(roleEntity.MountId, Is.EqualTo(mount.Id));
            Assert.That(roleEntity.RoleId, Is.EqualTo(TargetId));
            Assert.That(roleEntity.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(roleEntity.AllowMask, Is.Zero);
            Assert.That(roleEntity.DenyMask, Is.EqualTo(MountManageBit));
            Assert.That(userEntity.MountId, Is.EqualTo(mount.Id));
            Assert.That(userEntity.UserId, Is.EqualTo(TargetId));
            Assert.That(userEntity.AllowMask, Is.EqualTo(MountViewBit));
            Assert.That(userEntity.DenyMask, Is.EqualTo(FilesDownloadBit));
        });
    }

    [Test]
    public void OverwriteEntities_UpdateFrom_ShouldRejectAChangedTargetIdOrType()
    {
        var roleEntity = new MountRoleGrantEntity { RoleId = TargetId, AllowMask = MountViewBit };
        var userEntity = new MountUserGrantEntity { UserId = TargetId, AllowMask = MountViewBit };
        var otherRoleTarget = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role, TargetId + 1, [SpacePermission.MountManage], []).Value;
        var otherUserTarget = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.User, TargetId + 1, [SpacePermission.MountManage], []).Value;
        var roleOverwrite = CreateOverwrite(MountOverwriteTargetType.Role, allow: [SpacePermission.MountManage]);
        var userOverwrite = CreateOverwrite(MountOverwriteTargetType.User, allow: [SpacePermission.MountManage]);

        Assert.Multiple(() =>
        {
            Assert.That(() => roleEntity.UpdateFrom(otherRoleTarget), Throws.ArgumentException);
            Assert.That(() => roleEntity.UpdateFrom(userOverwrite), Throws.ArgumentException);
            Assert.That(() => userEntity.UpdateFrom(otherUserTarget), Throws.ArgumentException);
            Assert.That(() => userEntity.UpdateFrom(roleOverwrite), Throws.ArgumentException);
            Assert.That(roleEntity.AllowMask, Is.EqualTo(MountViewBit));
            Assert.That(userEntity.AllowMask, Is.EqualTo(MountViewBit));
        });
    }

    private static MountPermissionOverwrite CreateOverwrite(
        MountOverwriteTargetType targetType,
        SpacePermission[]? allow = null,
        SpacePermission[]? deny = null) =>
        MountPermissionOverwrite.Create(targetType, TargetId, allow ?? [], deny ?? []).Value;
}
