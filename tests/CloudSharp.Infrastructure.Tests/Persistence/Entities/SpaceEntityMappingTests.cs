using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.Entities;
using NUnit.Framework;

namespace CloudSharp.Infrastructure.Tests.Persistence.Entities;

[TestFixture]
public sealed class SpaceEntityMappingTests
{
    private static readonly Guid PublicId = new("019a9808-68b2-7000-8000-000000000001");
    private static readonly DateTimeOffset CreatedAt = new(2026, 11, 17, 1, 2, 3, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = CreatedAt.AddHours(2);

    [TestCase(0L, SpaceStatus.Active)]
    [TestCase(21L, SpaceStatus.Archived)]
    [TestCase(21L, SpaceStatus.Deleting)]
    [TestCase(21L, SpaceStatus.Deleted)]
    public void SpaceEntity_ShouldRoundTripStoredValues(long id, SpaceStatus status)
    {
        var space = Space.Reconstitute(
            id: id,
            publicId: PublicId,
            ownerUserId: 17,
            name: " Project files ",
            description: "  Description as stored  ",
            status: status,
            allowedBytes: status == SpaceStatus.Active ? null : 1_000,
            usedBytes: status == SpaceStatus.Deleted ? 0 : 250,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : 125,
            version: 4,
            createdAt: CreatedAt,
            updatedAt: UpdatedAt,
            archivedAt: status == SpaceStatus.Archived ? UpdatedAt : null,
            deletingAt: status is SpaceStatus.Deleting or SpaceStatus.Deleted ? CreatedAt.AddHours(1) : null,
            deletedAt: status == SpaceStatus.Deleted ? UpdatedAt : null);

        var entity = SpaceEntity.FromDomain(space);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(entity.ToDomain(), Is.EqualTo(space).UsingPropertiesComparer());
        });
    }

    [Test]
    public void SpaceEntity_UpdateFrom_ShouldPreserveIdAndClearOptionalValues()
    {
        var space = Space.Create(17, "Project files", null, null, CreatedAt).Value;
        var entity = new SpaceEntity
        {
            Id = 21,
            Description = "Former description",
            AllowedBytes = 1_000,
            ArchivedAt = UpdatedAt,
        };

        entity.UpdateFrom(space);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(21));
            Assert.That(entity.Description, Is.Null);
            Assert.That(entity.AllowedBytes, Is.Null);
            Assert.That(entity.ArchivedAt, Is.Null);
        });
    }

    [TestCase(0L)]
    [TestCase(31L)]
    public void SpaceRoleEntity_ShouldRoundTripStoredFieldsAndPermissions(long id)
    {
        SpacePermission[] permissions = [SpacePermission.FilesRead, SpacePermission.FilesDownload];
        var role = SpaceRole.Reconstitute(
            id, PublicId, 21, " Reading Lead ", "READING LEAD", null, "#aBc123",
            5, false, 7, CreatedAt, UpdatedAt, UpdatedAt, permissions);

        var entity = SpaceRoleEntity.FromDomain(role);
        var restored = entity.ToDomain(permissions);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(restored, Is.EqualTo(role).UsingPropertiesComparer());
            Assert.That(restored.Permissions, Is.EquivalentTo(permissions));
        });
    }

    [Test]
    public void SpaceRoleEntity_ToDomain_ShouldUseCoreAdminPermissionsWhenStoredPermissionsAreEmpty()
    {
        var role = SpaceRole.CreateManaged(21, SpaceRoleSystemKey.Admin, CreatedAt).Value;
        var entity = SpaceRoleEntity.FromDomain(role);

        var restored = entity.ToDomain([]);

        Assert.That(restored.Permissions, Is.EquivalentTo(Enum.GetValues<SpacePermission>()));
    }

    [Test]
    public void SpaceRoleEntity_UpdateFrom_ShouldPreserveIdAndClearOptionalFields()
    {
        var entity = new SpaceRoleEntity
        {
            Id = 31,
            SystemKey = SpaceRoleSystemKey.Member,
            Color = "#ABC123",
            DeletedAt = UpdatedAt,
        };
        var role = SpaceRole.Reconstitute(
            0, PublicId, 21, "Reader", "READER", null, null,
            0, false, 1, CreatedAt, CreatedAt, null, []);

        entity.UpdateFrom(role);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(31));
            Assert.That(entity.SystemKey, Is.Null);
            Assert.That(entity.Color, Is.Null);
            Assert.That(entity.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void PermissionEntity_ToDomain_ShouldResolveEveryStableCode([Values] SpacePermission permission)
    {
        var entity = new PermissionEntity { Code = SpacePermissionCodes.Get(permission) };

        Assert.That(entity.ToDomain(), Is.EqualTo(permission));
    }

    [Test]
    public void PermissionEntity_ToDomain_ShouldRejectUnknownStoredCode()
    {
        var entity = new PermissionEntity { Code = "UNKNOWN_PERMISSION" };

        Assert.That(() => entity.ToDomain(), Throws.InvalidOperationException);
    }

    [TestCase(0L, SpaceMembershipStatus.Active)]
    [TestCase(41L, SpaceMembershipStatus.Suspended)]
    [TestCase(41L, SpaceMembershipStatus.Left)]
    public void SpaceMembershipEntity_ShouldRoundTripAssignmentsAndStoredFields(
        long id,
        SpaceMembershipStatus status)
    {
        var assignment = MembershipRoleAssignment.Create(31, 17, CreatedAt.AddMinutes(30));
        var membership = SpaceMembership.Reconstitute(
            id, PublicId, 21, 18, status, 5, CreatedAt, UpdatedAt,
            status == SpaceMembershipStatus.Suspended ? UpdatedAt : null,
            status == SpaceMembershipStatus.Left ? UpdatedAt : null,
            [assignment]);

        var entity = SpaceMembershipEntity.FromDomain(membership);
        var link = SpaceMembershipRoleEntity.FromDomain(entity, assignment);
        var restored = entity.ToDomain([link]);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(link.MembershipId, Is.EqualTo(id));
            Assert.That(link.SpaceId, Is.EqualTo(21));
            Assert.That(link.Membership, Is.SameAs(entity));
            Assert.That(restored, Is.EqualTo(membership).UsingPropertiesComparer());
        });
    }

    [Test]
    public void SpaceMembershipEntity_UpdateFrom_ShouldPreserveIdAndClearSuspension()
    {
        var role = SpaceRole.Reconstitute(
            31, PublicId, 21, "Reader", "READER", null, null,
            0, false, 1, CreatedAt, CreatedAt, null, []);
        var membership = SpaceMembership.Create(21, 18, [role], 17, CreatedAt).Value;
        var entity = new SpaceMembershipEntity { Id = 41, SuspendedAt = UpdatedAt };

        entity.UpdateFrom(membership);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(41));
            Assert.That(entity.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(entity.SuspendedAt, Is.Null);
        });
    }

    [TestCase(0L, SpaceInviteStatus.Active)]
    [TestCase(51L, SpaceInviteStatus.Revoked)]
    [TestCase(51L, SpaceInviteStatus.Expired)]
    public void SpaceInviteEntity_ShouldRoundTripStoredHashAndVersion(long id, SpaceInviteStatus status)
    {
        var hash = TokenHash.Reconstitute("invite_hash_value");
        var invite = SpaceInvite.Reconstitute(
            id, PublicId, 21, 17, hash, status, 2,
            status == SpaceInviteStatus.Expired ? 2 : null, CreatedAt.AddDays(1),
            9, CreatedAt, UpdatedAt, status == SpaceInviteStatus.Revoked ? UpdatedAt : null);

        var entity = SpaceInviteEntity.FromDomain(invite);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(id));
            Assert.That(entity.TokenHash, Is.EqualTo(hash.Value));
            Assert.That(entity.ToDomain(), Is.EqualTo(invite).UsingPropertiesComparer());
        });
    }

    [Test]
    public void SpaceInviteEntity_UpdateFrom_ShouldPreserveIdAndClearOptionalValues()
    {
        var invite = SpaceInvite.Create(
            21, 17, TokenHash.Reconstitute("invite_hash_value"), CreatedAt.AddDays(1), null, CreatedAt).Value;
        var entity = new SpaceInviteEntity { Id = 51, MaxUses = 3, RevokedAt = UpdatedAt };

        entity.UpdateFrom(invite);

        Assert.Multiple(() =>
        {
            Assert.That(entity.Id, Is.EqualTo(51));
            Assert.That(entity.MaxUses, Is.Null);
            Assert.That(entity.RevokedAt, Is.Null);
        });
    }
}
