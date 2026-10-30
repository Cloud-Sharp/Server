using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Memberships;

[TestFixture]
public class SpaceMembershipTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 3, 30, 0, TimeSpan.Zero);

    [Test]
    public void Create_WithValidInputs_ShouldCreateActiveMembershipWithAssignments()
    {
        var roles = new[] { RestoreRole(101), RestoreRole(102, systemKey: SpaceRoleSystemKey.Viewer) };

        var result = SpaceMembership.Create(7, 21, roles, 9, Now);

        Assert.That(result.IsSuccess, Is.True);
        var membership = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(membership.Id, Is.Zero);
            Assert.That(membership.PublicId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(membership.PublicId.ToString("D")[14], Is.EqualTo('7'));
            Assert.That(membership.SpaceId, Is.EqualTo(7));
            Assert.That(membership.UserId, Is.EqualTo(21));
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(membership.Version, Is.EqualTo(1));
            Assert.That(membership.JoinedAt, Is.EqualTo(Now));
            Assert.That(membership.UpdatedAt, Is.EqualTo(Now));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.LeftAt, Is.Null);
            Assert.That(membership.RoleAssignments, Is.EquivalentTo(new[]
            {
                Assignment(101, 9, Now),
                Assignment(102, 9, Now),
            }));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidSpaceId_ShouldFail(long spaceId)
    {
        AssertError(
            SpaceMembership.Create(spaceId, 21, [RestoreRole(101)], 9, Now),
            ErrorCodes.SpaceMembership.SpaceIdInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidUserId_ShouldFail(long userId)
    {
        AssertError(
            SpaceMembership.Create(7, userId, [RestoreRole(101)], 9, Now),
            ErrorCodes.SpaceMembership.UserIdInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidAssignedBy_ShouldFail(long assignedByUserId)
    {
        AssertError(
            SpaceMembership.Create(7, 21, [RestoreRole(101)], assignedByUserId, Now),
            ErrorCodes.SpaceMembership.AssignedByInvalid);
    }

    [Test]
    public void Create_WithNonUtcNow_ShouldFail()
    {
        AssertError(
            SpaceMembership.Create(7, 21, [RestoreRole(101)], 9, Now.ToOffset(TimeSpan.FromHours(9))),
            ErrorCodes.SpaceMembership.InvalidState);
    }

    [TestCaseSource(nameof(InvalidRoleSets))]
    public void Create_WithInvalidRoles_ShouldFail(IReadOnlyCollection<SpaceRole> roles)
    {
        AssertError(
            SpaceMembership.Create(7, 21, roles, 9, Now),
            ErrorCodes.SpaceMembership.RoleInvalid);
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceMembershipStatus.Left)]
    public void Reconstitute_WithValidSnapshot_ShouldRoundTripEveryField(SpaceMembershipStatus status)
    {
        var original = InState(status, version: 8, assignments:
        [
            Assignment(101, 8, Now.AddDays(-1)),
            Assignment(102, 6, Now.AddHours(-2)),
        ]);

        var restored = SpaceMembership.Reconstitute(
            original.Id, original.PublicId, original.SpaceId, original.UserId, original.Status,
            original.Version, original.JoinedAt, original.UpdatedAt, original.SuspendedAt, original.LeftAt,
            original.RoleAssignments);

        Assert.That(Snapshot(restored), Is.EqualTo(Snapshot(original)));
        Assert.That(restored, Is.Not.SameAs(original));
    }

    [Test]
    public void Reconstitute_ShouldPreserveStoredValuesAndAllowUnsavedId()
    {
        var membership = Restore(id: 0);

        Assert.That(membership.Id, Is.Zero);
        Assert.That(membership.JoinedAt, Is.EqualTo(Now.AddDays(-1)));
        Assert.That(membership.UpdatedAt, Is.EqualTo(Now));
    }

    [TestCase(SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceMembershipStatus.Left)]
    public void Reconstitute_WithEmptyAssignments_OnInactiveState_ShouldSucceed(SpaceMembershipStatus status)
    {
        var membership = InState(status, assignments: []);

        Assert.That(membership.RoleAssignments, Is.Empty);
    }

    [Test]
    public void Reconstitute_ShouldCopyAssignmentsFromSourceCollection()
    {
        var source = new List<MembershipRoleAssignment> { Assignment(101) };
        var membership = Restore(assignments: source);

        source.Add(Assignment(103));

        Assert.That(membership.RoleAssignments.Select(a => a.RoleId), Is.EquivalentTo(new[] { 101L }));
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<SpaceMembership> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithUndefinedStatus_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(status: (SpaceMembershipStatus)99));
    }

    [Test]
    public void Reconstitute_WithNullAssignments_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpaceMembership.Reconstitute(
            42, Guid.CreateVersion7(), 7, 21, SpaceMembershipStatus.Active, 1,
            Now.AddDays(-1), Now, null, null, null!));
    }

    [Test]
    public void ReplaceRoles_ShouldAssignNewRolesAndPreserveKeptAssignmentRecords()
    {
        var membership = Restore(assignments:
        [
            Assignment(101, assignedBy: 8, assignedAt: Now.AddDays(-1)),
            Assignment(102, assignedBy: 8, assignedAt: Now.AddDays(-1)),
        ]);
        var beforeAssignments = membership.RoleAssignments;
        var replacedAt = Now.AddHours(1);

        var result = membership.ReplaceRoles([RestoreRole(102), RestoreRole(103)], 9, replacedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.RoleAssignments, Is.EquivalentTo(new[]
            {
                Assignment(102, 8, Now.AddDays(-1)),
                Assignment(103, 9, replacedAt),
            }));
            Assert.That(membership.Version, Is.EqualTo(2));
            Assert.That(membership.UpdatedAt, Is.EqualTo(replacedAt));
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(membership.RoleAssignments, Is.Not.SameAs(beforeAssignments));
            Assert.That(beforeAssignments, Is.EquivalentTo(new[]
            {
                Assignment(101, 8, Now.AddDays(-1)),
                Assignment(102, 8, Now.AddDays(-1)),
            }));
        });
    }

    [Test]
    public void ReplaceRoles_WithSameRoleSet_ShouldSucceedWithoutChangingSnapshot()
    {
        var membership = Restore(assignments: [Assignment(101), Assignment(102)]);
        var before = Snapshot(membership);
        var beforeAssignments = membership.RoleAssignments;

        var result = membership.ReplaceRoles([RestoreRole(102), RestoreRole(101)], 11, Now.AddHours(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(Snapshot(membership), Is.EqualTo(before));
            Assert.That(membership.RoleAssignments, Is.SameAs(beforeAssignments));
        });
    }

    [Test]
    public void ReplaceRoles_WithSameRoleSet_AtMaximumVersion_ShouldSucceedWithoutChangingSnapshot()
    {
        var membership = Restore(version: long.MaxValue, assignments: [Assignment(101)]);
        var before = Snapshot(membership);

        var result = membership.ReplaceRoles([RestoreRole(101)], 11, Now.AddHours(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Snapshot(membership), Is.EqualTo(before));
    }

    [Test]
    public void ReplaceRoles_OnSuspendedMembership_ShouldSucceedWithoutChangingStatus()
    {
        var membership = InState(SpaceMembershipStatus.Suspended);

        var result = membership.ReplaceRoles([RestoreRole(103)], 9, Now.AddHours(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));
            Assert.That(membership.SuspendedAt, Is.EqualTo(Now.AddHours(-1)));
            Assert.That(membership.RoleAssignments.Select(a => a.RoleId), Is.EquivalentTo(new[] { 103L }));
            Assert.That(membership.Version, Is.EqualTo(2));
            Assert.That(membership.UpdatedAt, Is.EqualTo(Now.AddHours(1)));
        });
    }

    [TestCaseSource(nameof(InvalidRoleSets))]
    public void ReplaceRoles_WithInvalidRoles_ShouldFailWithoutChangingSnapshot(IReadOnlyCollection<SpaceRole> roles)
    {
        var membership = Restore();

        AssertFailureUnchanged(
            membership,
            () => membership.ReplaceRoles(roles, 9, Now),
            ErrorCodes.SpaceMembership.RoleInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ReplaceRoles_WithInvalidAssignedBy_ShouldFailWithoutChangingSnapshot(long assignedByUserId)
    {
        var membership = Restore();

        AssertFailureUnchanged(
            membership,
            () => membership.ReplaceRoles([RestoreRole(102)], assignedByUserId, Now),
            ErrorCodes.SpaceMembership.AssignedByInvalid);
    }

    [Test]
    public void SuspendThenActivate_ShouldToggleSuspendedTimestamp()
    {
        var membership = Restore();
        var suspendedAt = Now.AddMinutes(1);
        var activatedAt = Now.AddMinutes(2);

        Assert.That(membership.Suspend(suspendedAt).IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));
            Assert.That(membership.SuspendedAt, Is.EqualTo(suspendedAt));
            Assert.That(membership.LeftAt, Is.Null);
            Assert.That(membership.Version, Is.EqualTo(2));
            Assert.That(membership.UpdatedAt, Is.EqualTo(suspendedAt));
        });

        Assert.That(membership.Activate(activatedAt).IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.JoinedAt, Is.EqualTo(Now.AddDays(-1)));
            Assert.That(membership.Version, Is.EqualTo(3));
            Assert.That(membership.UpdatedAt, Is.EqualTo(activatedAt));
        });
    }

    [Test]
    public void Suspend_OnSuspendedMembership_ShouldFailWithoutChangingSnapshot()
    {
        var membership = InState(SpaceMembershipStatus.Suspended);

        AssertFailureUnchanged(membership, () => membership.Suspend(Now), ErrorCodes.SpaceMembership.InvalidState);
    }

    [Test]
    public void Activate_OnActiveMembership_ShouldFailWithoutChangingSnapshot()
    {
        var membership = InState(SpaceMembershipStatus.Active);

        AssertFailureUnchanged(membership, () => membership.Activate(Now), ErrorCodes.SpaceMembership.InvalidState);
    }

    [Test]
    public void SuspendedMembershipWithoutRoles_ShouldGainRolesBeforeActivation()
    {
        var membership = InState(SpaceMembershipStatus.Suspended, assignments: []);

        AssertFailureUnchanged(membership, () => membership.Activate(Now), ErrorCodes.SpaceMembership.RoleInvalid);

        Assert.That(membership.ReplaceRoles([RestoreRole(201)], 9, Now).IsSuccess, Is.True);
        Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Suspended));

        Assert.That(membership.Activate(Now).IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.RoleAssignments.Select(a => a.RoleId), Is.EquivalentTo(new[] { 201L }));
            Assert.That(membership.Version, Is.EqualTo(3));
        });
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public void Leave_ShouldMarkLeftAndKeepAssignmentRecords(SpaceMembershipStatus status)
    {
        var membership = InState(status);
        var assignmentsBefore = membership.RoleAssignments.ToArray();
        var leftAt = Now.AddMinutes(5);

        var result = membership.Leave(leftAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(membership.LeftAt, Is.EqualTo(leftAt));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.RoleAssignments, Is.EquivalentTo(assignmentsBefore));
            Assert.That(membership.Version, Is.EqualTo(2));
            Assert.That(membership.UpdatedAt, Is.EqualTo(leftAt));
        });
    }

    [TestCase(SpaceMembershipStatus.Active)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    public void Remove_ShouldMarkLeftAndKeepAssignmentRecords(SpaceMembershipStatus status)
    {
        var membership = InState(status);

        var result = membership.Remove(Now.AddMinutes(5));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(membership.Status, Is.EqualTo(SpaceMembershipStatus.Left));
            Assert.That(membership.LeftAt, Is.EqualTo(Now.AddMinutes(5)));
            Assert.That(membership.SuspendedAt, Is.Null);
            Assert.That(membership.RoleAssignments, Is.Not.Empty);
        });
    }

    [TestCaseSource(nameof(LeftMutations))]
    public void Mutation_OnLeftMembership_ShouldFailWithoutChangingSnapshot(Func<SpaceMembership, Result> mutate)
    {
        var membership = InState(SpaceMembershipStatus.Left);

        AssertFailureUnchanged(membership, () => mutate(membership), ErrorCodes.SpaceMembership.Inactive);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Mutations_WithInvalidTime_ShouldFailWithoutChangingSnapshot(bool nonUtc)
    {
        var invalidTime = nonUtc ? Now.ToOffset(TimeSpan.FromHours(9)) : Now.AddTicks(-1);
        foreach (var mutation in Mutations())
        {
            var membership = ForMutation(mutation);
            AssertFailureUnchanged(
                membership, () => mutation.Apply(membership, invalidTime), ErrorCodes.SpaceMembership.InvalidState);
        }
    }

    [Test]
    public void Mutations_AtMaximumVersion_ShouldFailWithoutChangingSnapshot()
    {
        foreach (var mutation in Mutations())
        {
            var membership = ForMutation(mutation, long.MaxValue);
            AssertFailureUnchanged(
                membership, () => mutation.Apply(membership, Now), ErrorCodes.SpaceMembership.InvalidState);
        }
    }

    [Test]
    public void RoleAssignments_ShouldRejectMutationAttempts()
    {
        var membership = Restore();
        var snapshot = membership.RoleAssignments.ToArray();

        var collection = (ICollection<MembershipRoleAssignment>)membership.RoleAssignments;

        Assert.That(() => collection.Add(Assignment(999)), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Remove(snapshot[0]), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(membership.RoleAssignments.ToArray(), Is.EquivalentTo(snapshot));
    }

    private static IEnumerable<TestCaseData> InvalidRoleSets()
    {
        yield return RoleSet("null roles", null!);
        yield return RoleSet("empty roles", []);
        yield return RoleSet("null role", [null!]);
        yield return RoleSet("unpersisted role",
            [SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Member, Now).Value]);
        yield return RoleSet("role from another space", [RestoreRole(id: 104, spaceId: 8)]);
        yield return RoleSet("deleted role", [RestoreRole(id: 105, deletedAt: Now.AddHours(-1))]);
        yield return RoleSet("duplicate roles", [RestoreRole(101), RestoreRole(101)]);
    }

    private static TestCaseData RoleSet(string name, IReadOnlyCollection<SpaceRole> roles) =>
        new TestCaseData(new object?[] { roles }).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return InvalidSnapshot("negative id", () => Restore(id: -1));
        yield return InvalidSnapshot("empty public id", () => Restore(publicId: Guid.Empty));
        yield return InvalidSnapshot("zero space id", () => Restore(spaceId: 0));
        yield return InvalidSnapshot("zero user id", () => Restore(userId: 0));
        yield return InvalidSnapshot("zero version", () => Restore(version: 0));
        yield return InvalidSnapshot("non UTC joined time",
            () => Restore(joinedAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("updated before joined", () => Restore(updatedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("non UTC suspended time", () => Restore(
            status: SpaceMembershipStatus.Suspended,
            suspendedAt: Now.AddHours(-1).ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("suspended before joined", () =>
            Restore(status: SpaceMembershipStatus.Suspended, suspendedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("suspended after updated", () =>
            Restore(status: SpaceMembershipStatus.Suspended, suspendedAt: Now.AddMinutes(1)));
        yield return InvalidSnapshot("non UTC left time", () =>
            Restore(status: SpaceMembershipStatus.Left, leftAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("active with suspended timestamp", () => Restore(suspendedAt: Now.AddHours(-1)));
        yield return InvalidSnapshot("active with left timestamp", () => Restore(leftAt: Now));
        yield return InvalidSnapshot("suspended without timestamp",
            () => Restore(status: SpaceMembershipStatus.Suspended));
        yield return InvalidSnapshot("left timestamp allowed only for left", () => Restore(
            status: SpaceMembershipStatus.Suspended, suspendedAt: Now.AddHours(-1), leftAt: Now));
        yield return InvalidSnapshot("left without timestamp", () => Restore(status: SpaceMembershipStatus.Left));
        yield return InvalidSnapshot("suspended timestamp allowed only for suspended", () => Restore(
            status: SpaceMembershipStatus.Left, leftAt: Now, suspendedAt: Now.AddHours(-1)));
        yield return InvalidSnapshot("null assignment", () => Restore(assignments: [null!]));
        yield return InvalidSnapshot("duplicate role ids",
            () => Restore(assignments: [Assignment(101), Assignment(101, assignedBy: 8)]));
        yield return InvalidSnapshot("assigned before joined",
            () => Restore(assignments: [Assignment(101, assignedAt: Now.AddDays(-2))]));
        yield return InvalidSnapshot("assigned after updated",
            () => Restore(assignments: [Assignment(101, assignedAt: Now.AddMinutes(1))]));
        yield return InvalidSnapshot("active without assignments", () => Restore(assignments: []));
    }

    private static TestCaseData InvalidSnapshot(string name, Func<SpaceMembership> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> LeftMutations()
    {
        yield return LeftMutation("replace roles", m => m.ReplaceRoles([RestoreRole(102)], 11, Now));
        yield return LeftMutation("suspend", m => m.Suspend(Now));
        yield return LeftMutation("activate", m => m.Activate(Now));
        yield return LeftMutation("leave again", m => m.Leave(Now));
        yield return LeftMutation("remove again", m => m.Remove(Now));
    }

    private static TestCaseData LeftMutation(string name, Func<SpaceMembership, Result> mutate) =>
        new TestCaseData(mutate).SetArgDisplayNames(name);

    private static IEnumerable<Mutation> Mutations()
    {
        yield return new Mutation("replaceRoles", SpaceMembershipStatus.Active,
            (m, now) => m.ReplaceRoles([RestoreRole(102)], 11, now));
        yield return new Mutation("suspend", SpaceMembershipStatus.Active, (m, now) => m.Suspend(now));
        yield return new Mutation("activate", SpaceMembershipStatus.Suspended, (m, now) => m.Activate(now));
        yield return new Mutation("leave", SpaceMembershipStatus.Active, (m, now) => m.Leave(now));
        yield return new Mutation("remove", SpaceMembershipStatus.Active, (m, now) => m.Remove(now));
    }

    private static SpaceMembership ForMutation(Mutation mutation, long version = 1) =>
        InState(mutation.Status, version);

    private static SpaceMembership InState(
        SpaceMembershipStatus status,
        long version = 1,
        IReadOnlyCollection<MembershipRoleAssignment>? assignments = null) =>
        Restore(
            status: status,
            version: version,
            suspendedAt: status == SpaceMembershipStatus.Suspended ? Now.AddHours(-1) : null,
            leftAt: status == SpaceMembershipStatus.Left ? Now : null,
            assignments: assignments);

    private static SpaceMembership Restore(
        long id = 42,
        Guid? publicId = null,
        long spaceId = 7,
        long userId = 21,
        SpaceMembershipStatus status = SpaceMembershipStatus.Active,
        long version = 1,
        DateTimeOffset? joinedAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? suspendedAt = null,
        DateTimeOffset? leftAt = null,
        IReadOnlyCollection<MembershipRoleAssignment>? assignments = null) =>
        SpaceMembership.Reconstitute(
            id, publicId ?? Guid.CreateVersion7(), spaceId, userId, status, version,
            joinedAt ?? Now.AddDays(-1), updatedAt ?? Now, suspendedAt, leftAt,
            assignments ?? [Assignment(101)]);

    private static SpaceRole RestoreRole(
        long id = 101,
        long spaceId = 7,
        SpaceRoleSystemKey? systemKey = SpaceRoleSystemKey.Member,
        DateTimeOffset? deletedAt = null) =>
        SpaceRole.Reconstitute(
            id, Guid.CreateVersion7(), spaceId, "Member", "MEMBER", systemKey, null, 1, true,
            1, Now.AddDays(-2), Now, deletedAt,
            [SpacePermission.SpaceView, SpacePermission.FilesRead]);

    private static MembershipRoleAssignment Assignment(
        long roleId, long assignedBy = 9, DateTimeOffset? assignedAt = null) =>
        MembershipRoleAssignment.Create(roleId, assignedBy, assignedAt ?? Now.AddDays(-1));

    private static object?[] Snapshot(SpaceMembership membership) =>
    [
        membership.Id, membership.PublicId, membership.SpaceId, membership.UserId,
        membership.Status, membership.Version, membership.JoinedAt, membership.UpdatedAt,
        membership.SuspendedAt, membership.LeftAt,
        membership.RoleAssignments.OrderBy(a => a.RoleId).ToArray()
    ];

    private static void AssertFailureUnchanged(SpaceMembership membership, Func<Result> mutate, string expectedCode)
    {
        var before = Snapshot(membership);
        var result = mutate();

        AssertError(result, expectedCode);
        Assert.That(Snapshot(membership), Is.EqualTo(before), "Failed mutation must preserve every field.");
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }

    private sealed record Mutation(
        string Name, SpaceMembershipStatus Status, Func<SpaceMembership, DateTimeOffset, Result> Apply);
}
