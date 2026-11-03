using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Policies.Authorization;

[TestFixture]
public class SessionSpaceAuthorizationPolicyTests
{
    [Test]
    public void Evaluate_ShouldGrantOwnerAllPermissionsWithoutMembership()
    {
        var query = TestSpaceAuthorizationSnapshots.Query(actor: TestSpaceAuthorizationSnapshots.Session(21));
        var snapshot = new SpaceAuthorizationSnapshot(TestSpaceAuthorizationSnapshots.Space(), null, []);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot);

        Assert.That(result.IsSuccess, Is.True);
        var context = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(context.SpaceId, Is.EqualTo(7));
            Assert.That(context.SpacePublicId, Is.EqualTo(query.SpacePublicId));
            Assert.That(context.ActorType, Is.EqualTo(ActorType.Session));
            Assert.That(context.UserId, Is.EqualTo(21));
            Assert.That(context.MembershipId, Is.Null);
            Assert.That(context.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(context.EffectivePermissions.SetEquals(Enum.GetValues<SpacePermission>()), Is.True);
        });
    }

    [TestCase(21)]
    [TestCase(42)]
    public void Evaluate_ShouldPrioritizeSystemAdminBypass(long userId)
    {
        var query = TestSpaceAuthorizationSnapshots.Query(
            actor: TestSpaceAuthorizationSnapshots.Session(userId, SystemRole.SystemAdmin),
            permissions: [SpacePermission.UploadsManage, SpacePermission.OperationsManage]);
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(), TestSpaceAuthorizationSnapshots.Membership(), []);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
            Assert.That(result.Value.MembershipId, Is.Null);
            Assert.That(result.Value.EffectivePermissions.SetEquals(Enum.GetValues<SpacePermission>()), Is.True);
        });
    }

    [Test]
    public void Evaluate_ShouldUnionAssignedRolePermissions()
    {
        var query = TestSpaceAuthorizationSnapshots.Query(
            permissions: [SpacePermission.FilesRead, SpacePermission.TagsManage]);
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(),
            TestSpaceAuthorizationSnapshots.Membership(roleIds: [101, 102]),
            [
                TestSpaceAuthorizationSnapshots.Role(permissions: [SpacePermission.FilesRead]),
                TestSpaceAuthorizationSnapshots.Role(102, permissions: [SpacePermission.TagsManage]),
            ]);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.Membership));
            Assert.That(result.Value.MembershipId, Is.EqualTo(55));
            Assert.That(result.Value.EffectivePermissions.SetEquals(
                [SpacePermission.FilesRead, SpacePermission.TagsManage]), Is.True);
        });
    }

    [Test]
    public void Evaluate_ShouldRejectWhenAnyRequiredPermissionIsMissing()
    {
        var query = TestSpaceAuthorizationSnapshots.Query(
            permissions: [SpacePermission.FilesRead, SpacePermission.TagsManage]);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(query, TestSpaceAuthorizationSnapshots.MemberSnapshot());

        AssertCode(result, ErrorCodes.Auth.SpacePermissionDenied);
    }

    [TestCase(SystemRole.User)]
    [TestCase(SystemRole.SystemAdmin)]
    public void Evaluate_ShouldRejectMissingSpace(SystemRole systemRole)
    {
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(actor: TestSpaceAuthorizationSnapshots.Session(systemRole: systemRole)),
            null);

        AssertCode(result, ErrorCodes.Auth.SpaceNotFound);
    }

    [TestCaseSource(nameof(IneligibleMemberships))]
    public void Evaluate_ShouldRequireBasicAccessEvenWithoutRequiredPermissions(SpaceMembership? membership)
    {
        var snapshot = new SpaceAuthorizationSnapshot(TestSpaceAuthorizationSnapshots.Space(), membership,
            [TestSpaceAuthorizationSnapshots.Role(systemKey: SpaceRoleSystemKey.Admin)]);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(permissions: []), snapshot);

        AssertCode(result, ErrorCodes.Auth.SpaceAccessDenied);
    }

    [Test]
    public void Evaluate_ShouldCheckBasicAccessBeforeSpaceState()
    {
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(SpaceStatus.Archived), null, []);

        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(statuses: [SpaceStatus.Active]), snapshot);

        AssertCode(result, ErrorCodes.Auth.SpaceAccessDenied);
        Assert.That(((SpaceAuthorizationError)result.Errors.Single()).ActualSpaceStatus, Is.Null);
    }

    [Test]
    public void Evaluate_ShouldAllowBasicAccessWithEmptyEffectivePermissions()
    {
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(permissions: []),
            TestSpaceAuthorizationSnapshots.MemberSnapshot(permissions: []));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.EffectivePermissions, Is.Empty);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public void Evaluate_ShouldAllowExplicitlyAllowedSpaceStatus(SpaceStatus status)
    {
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(statuses: [status]),
            TestSpaceAuthorizationSnapshots.MemberSnapshot(status));

        Assert.That(result.IsSuccess, Is.True);
    }

    [TestCase(SpaceStatus.Archived, SpaceStatus.Active)]
    [TestCase(SpaceStatus.Active, SpaceStatus.Archived)]
    public void Evaluate_ShouldPreserveDeniedSpaceStatus(SpaceStatus actual, SpaceStatus allowed)
    {
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(statuses: [allowed]),
            TestSpaceAuthorizationSnapshots.MemberSnapshot(actual));

        AssertCode(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(((SpaceAuthorizationError)result.Errors.Single()).ActualSpaceStatus, Is.EqualTo(actual));
    }

    [TestCase(SpaceStatus.Deleting, SystemRole.User)]
    [TestCase(SpaceStatus.Deleted, SystemRole.User)]
    [TestCase(SpaceStatus.Deleting, SystemRole.SystemAdmin)]
    [TestCase(SpaceStatus.Deleted, SystemRole.SystemAdmin)]
    public void Evaluate_ShouldAlwaysRejectDeletingAndDeletedSpaces(SpaceStatus status, SystemRole systemRole)
    {
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(
                actor: TestSpaceAuthorizationSnapshots.Session(systemRole: systemRole), statuses: [status]),
            TestSpaceAuthorizationSnapshots.MemberSnapshot(status));

        AssertCode(result, ErrorCodes.Auth.SpaceStateNotAllowed);
        Assert.That(((SpaceAuthorizationError)result.Errors.Single()).ActualSpaceStatus, Is.EqualTo(status));
    }

    [TestCase(SystemRole.User)]
    [TestCase(SystemRole.SystemAdmin)]
    public void Evaluate_ShouldRejectMcpEvenForSpaceOwner(SystemRole systemRole)
    {
        var actor = CurrentActor.ForMcp(21, Guid.CreateVersion7(), systemRole, 5, Guid.CreateVersion7());
        var result = SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(actor),
            new SpaceAuthorizationSnapshot(TestSpaceAuthorizationSnapshots.Space(), null, []));

        AssertCode(result, ErrorCodes.Auth.TokenTypeNotAllowed);
    }

    [Test]
    public void Evaluate_ShouldNotAuthorizeDifferentSpaceReturnedByReader()
    {
        var snapshot = new SpaceAuthorizationSnapshot(
            TestSpaceAuthorizationSnapshots.Space(publicId: Guid.CreateVersion7()), null, []);

        Assert.Throws<InvalidOperationException>(() => SessionSpaceAuthorizationPolicy.Evaluate(
            TestSpaceAuthorizationSnapshots.Query(actor: TestSpaceAuthorizationSnapshots.Session(21)), snapshot));
    }

    [Test]
    public void Evaluate_ShouldReturnImmutableContextIndependentOfLaterMembershipChanges()
    {
        var snapshot = TestSpaceAuthorizationSnapshots.MemberSnapshot();
        var context = SessionSpaceAuthorizationPolicy.Evaluate(TestSpaceAuthorizationSnapshots.Query(), snapshot).Value;

        var changed = snapshot.Membership!.Suspend(snapshot.Membership.UpdatedAt.AddMinutes(1));
        Assert.That(changed.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(context.EffectivePermissions.Contains(SpacePermission.SpaceView), Is.True);
            Assert.That(() => ((ICollection<SpacePermission>)context.EffectivePermissions).Clear(),
                Throws.TypeOf<NotSupportedException>());
        });
        AssertCode(SessionSpaceAuthorizationPolicy.Evaluate(TestSpaceAuthorizationSnapshots.Query(), snapshot),
            ErrorCodes.Auth.SpaceAccessDenied);
    }

    private static IEnumerable<TestCaseData> IneligibleMemberships()
    {
        yield return new TestCaseData((SpaceMembership?)null).SetArgDisplayNames("missing");
        yield return new TestCaseData(TestSpaceAuthorizationSnapshots.Membership(SpaceMembershipStatus.Suspended))
            .SetArgDisplayNames("suspended");
        yield return new TestCaseData(TestSpaceAuthorizationSnapshots.Membership(SpaceMembershipStatus.Left))
            .SetArgDisplayNames("left");
        yield return new TestCaseData(TestSpaceAuthorizationSnapshots.Membership(userId: 43))
            .SetArgDisplayNames("different user");
        yield return new TestCaseData(TestSpaceAuthorizationSnapshots.Membership(spaceId: 8))
            .SetArgDisplayNames("different space");
        yield return new TestCaseData(TestSpaceAuthorizationSnapshots.Membership(id: 0))
            .SetArgDisplayNames("unpersisted");
    }

    private static void AssertCode(ResultBase result, string code)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(code));
    }
}
