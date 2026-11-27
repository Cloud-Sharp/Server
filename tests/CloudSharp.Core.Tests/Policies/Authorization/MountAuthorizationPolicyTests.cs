using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.Policies.Authorization;

[TestFixture]
public class MountAuthorizationPolicyTests
{
    [Test]
    public void Evaluate_ShouldReturnContextWithEffectiveMountPermissions()
    {
        var actor = Actor();
        var mount = Mount(1, overwrites: [Overwrite(MountOverwriteTargetType.User, actor.UserId, allow: [SpacePermission.FilesWrite])]);

        var result = MountAuthorizationPolicy.Evaluate(Query(mount, [SpacePermission.FilesWrite]), Member(actor), mount);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.MountId, Is.EqualTo(mount.Id));
            Assert.That(result.Value.MountPublicId, Is.EqualTo(mount.PublicId));
            Assert.That(result.Value.Space.SpaceId, Is.EqualTo(SpaceId));
            Assert.That(result.Value.Space.UserId, Is.EqualTo(actor.UserId));
            Assert.That(result.Value.EffectivePermissions, Is.EquivalentTo(new[]
            {
                SpacePermission.MountView,
                SpacePermission.FilesRead,
                SpacePermission.FilesWrite,
            }));
        });
    }

    [Test]
    public void Evaluate_ShouldHideMountWithoutMountViewEvenWhenOtherPermissionIsRequired()
    {
        var actor = Actor();
        var mount = Mount(
            2,
            visibility: MountVisibility.Private,
            overwrites: [Overwrite(MountOverwriteTargetType.User, actor.UserId, allow: [SpacePermission.MountManage])]);

        var result = MountAuthorizationPolicy.Evaluate(Query(mount, [SpacePermission.MountManage]), Member(actor), mount);

        AssertError(result, ErrorCodes.Common.ResourceNotFound);
    }

    [Test]
    public void Evaluate_ShouldReturnPermissionDeniedForVisibleMountWithoutRequiredPermission()
    {
        var actor = Actor();
        var mount = Mount(3);

        var result = MountAuthorizationPolicy.Evaluate(Query(mount, [SpacePermission.MountManage]), Member(actor), mount);

        AssertError(result, ErrorCodes.Auth.SpacePermissionDenied);
    }

    [TestCase("Missing")]
    [TestCase("OtherSpace")]
    [TestCase("OtherPublicId")]
    public void Evaluate_ShouldReturnResourceNotFoundForMissingOrForeignMount(string scenario)
    {
        var actor = Actor();
        var requested = Mount(4);
        var stored = scenario switch
        {
            "Missing" => null,
            "OtherSpace" => Mount(4, spaceId: SpaceId + 1),
            _ => Mount(5),
        };

        var result = MountAuthorizationPolicy.Evaluate(Query(requested, []), Owner(actor), stored);

        AssertError(result, ErrorCodes.Common.ResourceNotFound);
    }

    [Test]
    public void Evaluate_ShouldReturnRequestConflictForMountStateNotAllowed()
    {
        var actor = Actor();
        var mount = Mount(6, status: MountStatus.Archived);

        var result = MountAuthorizationPolicy.Evaluate(
            Query(mount, [], mountStatuses: [MountStatus.Active]),
            Owner(actor),
            mount);

        AssertError(result, ErrorCodes.Common.RequestConflict);
    }

    [Test]
    public void Evaluate_ShouldMaskMountStateBehindMountView()
    {
        var actor = Actor();
        var mount = Mount(7, visibility: MountVisibility.Private, status: MountStatus.Archived);

        var result = MountAuthorizationPolicy.Evaluate(
            Query(mount, [], mountStatuses: [MountStatus.Active]),
            Member(actor),
            mount);

        AssertError(result, ErrorCodes.Common.ResourceNotFound);
    }

    [TestCase(null, ErrorCodes.Auth.SpaceNotFound)]
    [TestCase(SpaceMembershipStatus.Suspended, ErrorCodes.Auth.SpaceAccessDenied)]
    public void Evaluate_ShouldReturnSpaceErrorsBeforeReadingMountAccess(
        SpaceMembershipStatus? membershipStatus,
        string expectedError)
    {
        var actor = Actor();
        var mount = Mount(8);
        var snapshot = membershipStatus is { } status ? Member(actor, membershipStatus: status) : null;

        var result = MountAuthorizationPolicy.Evaluate(Query(mount, []), snapshot, mount);

        AssertError(result, expectedError);
    }

    [Test]
    public void Evaluate_ShouldReturnSpaceStateErrorForWriteOnArchivedSpace()
    {
        var actor = Actor();
        var mount = Mount(9);

        var result = MountAuthorizationPolicy.Evaluate(
            Query(mount, [SpacePermission.MountManage], spaceStatuses: [SpaceStatus.Active]),
            Owner(actor, SpaceStatus.Archived),
            mount);

        AssertError(result, ErrorCodes.Auth.SpaceStateNotAllowed);
    }

    [Test]
    public void Evaluate_ShouldAllowManagementReadOnArchivedSpace()
    {
        var actor = Actor();
        var role = Role(11, permissions: [SpacePermission.MountView, SpacePermission.MountManage]);
        var mount = Mount(11);

        var result = MountAuthorizationPolicy.Evaluate(
            Query(mount, [SpacePermission.MountManage]),
            Member(actor, [role], spaceStatus: SpaceStatus.Archived),
            mount);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.SpaceStatus, Is.EqualTo(SpaceStatus.Archived));
            Assert.That(result.Value.EffectivePermissions, Does.Contain(SpacePermission.MountManage));
        });
    }

    [Test]
    public void Query_ShouldRejectRequiredPermissionOutsideMountScope()
    {
        var mount = Mount(10);

        Assert.That(
            () => Query(mount, [SpacePermission.MembersManage]),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    private static MountAuthorizationQuery Query(
        Mount mount,
        SpacePermission[] required,
        SpaceStatus[]? spaceStatuses = null,
        MountStatus[]? mountStatuses = null) =>
        new(
            Actor(),
            SpacePublicId,
            mount.PublicId,
            required,
            spaceStatuses ?? [SpaceStatus.Active, SpaceStatus.Archived],
            mountStatuses ?? [MountStatus.Active, MountStatus.Archived]);

    private static void AssertError(Result<AuthorizedMountContext> result, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(errorCode));
    }
}
