using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Mounts;

[TestFixture]
public class MountTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 26, 3, 30, 0, TimeSpan.Zero);

    [Test]
    public void Create_WithInitialGrants_ShouldCreateActiveMountWithDefaults()
    {
        var result = Mount.Create(
            11,
            "  Design  ",
            MountVisibility.Private,
            Now,
            [
                Grant(MountGrantTargetType.Role, 7, MountPermission.FilesWrite, MountPermission.FilesRead),
                Grant(MountGrantTargetType.User, 7, MountPermission.FilesDownload),
            ]);

        Assert.That(result.IsSuccess, Is.True);
        var mount = result.Value;
        Assert.That(mount.Id, Is.Zero);
        Assert.That(mount.PublicId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(mount.PublicId.ToString("D")[14], Is.EqualTo('7'));
        Assert.That(mount.SpaceId, Is.EqualTo(11));
        Assert.That(mount.Name, Is.EqualTo("Design"));
        Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Private));
        Assert.That(mount.Status, Is.EqualTo(MountStatus.Active));
        Assert.That(mount.Version, Is.EqualTo(1));
        Assert.That(mount.CreatedAt, Is.EqualTo(Now));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now));
        Assert.That(mount.ArchivedAt, Is.Null);
        Assert.That(mount.Grants, Has.Count.EqualTo(2));
        Assert.That(FindGrant(mount, MountGrantTargetType.Role, 7).Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesWrite]), Is.True);
        Assert.That(FindGrant(mount, MountGrantTargetType.User, 7).Permissions.SetEquals(
            [MountPermission.FilesDownload]), Is.True);
    }

    [TestCase(1)]
    [TestCase(200)]
    public void Create_WithNameAtBoundary_ShouldTrimAndSucceed(int length)
    {
        var name = new string('n', length);

        var result = Mount.Create(7, $"  {name}  ", MountVisibility.Public, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Name, Is.EqualTo(name));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\n ")]
    [TestCase(201)]
    public void Create_WithInvalidName_ShouldReturnNameInvalid(object? input)
    {
        var name = input is int length ? new string('n', length) : (string?)input;

        var result = Mount.Create(7, name!, MountVisibility.Public, Now);

        AssertError(result, ErrorCodes.Mount.NameInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidSpaceId_ShouldReturnSpaceIdInvalid(long spaceId)
    {
        var result = Mount.Create(spaceId, "Design", MountVisibility.Public, Now);

        AssertError(result, ErrorCodes.Mount.SpaceIdInvalid);
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void Create_WithUndefinedVisibility_ShouldReturnVisibilityInvalid(int value)
    {
        var result = Mount.Create(7, "Design", (MountVisibility)value, Now);

        AssertError(result, ErrorCodes.Mount.VisibilityInvalid);
    }

    [Test]
    public void Create_WithNonUtcNow_ShouldReturnInvalidState()
    {
        var result = Mount.Create(
            7,
            "Design",
            MountVisibility.Public,
            Now.ToOffset(TimeSpan.FromHours(9)));

        AssertError(result, ErrorCodes.Mount.InvalidState);
    }

    [Test]
    public void Create_WithRepeatedName_ShouldSucceed()
    {
        var first = Mount.Create(7, "Design", MountVisibility.Private, Now);
        var second = Mount.Create(7, "Design", MountVisibility.Public, Now);

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.True);
        Assert.That(second.Value.PublicId, Is.Not.EqualTo(first.Value.PublicId));
    }

    [Test]
    public void Create_WithNullGrantEntry_ShouldReturnGrantsInvalid()
    {
        var result = Mount.Create(7, "Design", MountVisibility.Public, Now, [null!]);

        AssertError(result, ErrorCodes.Mount.GrantsInvalid);
    }

    [Test]
    public void Create_WithDuplicateTarget_ShouldReturnGrantsInvalid()
    {
        var result = Mount.Create(
            7,
            "Design",
            MountVisibility.Public,
            Now,
            [
                Grant(MountGrantTargetType.Role, 7, MountPermission.FilesRead),
                Grant(MountGrantTargetType.Role, 7, MountPermission.FilesWrite),
            ]);

        AssertError(result, ErrorCodes.Mount.GrantsInvalid);
    }

    [Test]
    public void Create_ShouldIsolateGrantsFromCallerCollectionMutation()
    {
        var source = new List<MountAccessGrant> { RoleGrant() };
        var mount = Mount.Create(7, "Design", MountVisibility.Public, Now, source).Value;

        source.Clear();

        Assert.That(mount.Grants, Has.Count.EqualTo(1));
        Assert.That(FindGrant(mount, MountGrantTargetType.Role, 7).Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesDownload]), Is.True);
    }

    [Test]
    public void Grants_ShouldRejectMutationAttempts()
    {
        var mount = Restore();
        var collection = (ICollection<MountAccessGrant>)mount.Grants;

        Assert.That(() => collection.Add(RoleGrant()), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Remove(collection.First()), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(mount.Grants, Has.Count.EqualTo(1));
    }

    [TestCase(MountStatus.Active)]
    [TestCase(MountStatus.Archived)]
    public void Reconstitute_WithValidSnapshot_ShouldRoundTripEveryField(MountStatus status)
    {
        var original = InState(status, version: 8);

        var restored = Mount.Reconstitute(
            original.Id,
            original.PublicId,
            original.SpaceId,
            original.Name,
            original.Visibility,
            original.Status,
            original.Version,
            original.CreatedAt,
            original.UpdatedAt,
            original.ArchivedAt,
            original.Grants);

        Assert.That(Snapshot(restored), Is.EqualTo(Snapshot(original)));
        Assert.That(restored, Is.Not.SameAs(original));
    }

    [Test]
    public void Reconstitute_ShouldPreserveStoredNameAndAllowUnsavedId()
    {
        var mount = Restore(id: 0, name: "  Stored name  ");

        Assert.That(mount.Id, Is.Zero);
        Assert.That(mount.Name, Is.EqualTo("  Stored name  "));
    }

    [Test]
    public void Reconstitute_ShouldIsolateGrantsFromCallerCollectionMutation()
    {
        var source = new List<MountAccessGrant> { RoleGrant() };
        var mount = Restore(grants: source);

        source.Clear();

        Assert.That(mount.Grants, Has.Count.EqualTo(1));
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<Mount> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithNullGrants_ShouldThrowArgumentNullException()
    {
        Assert.That(() => Mount.Reconstitute(
            42,
            Guid.CreateVersion7(),
            7,
            "Engineering",
            MountVisibility.Private,
            MountStatus.Active,
            1,
            Now.AddDays(-1),
            Now,
            null,
            null!), Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void UpdateSettings_ShouldApplyBothFieldsAndPreserveGrants()
    {
        var mount = Restore();
        var grantsBefore = GrantsSnapshot(mount);
        var changedAt = Now.AddMinutes(1);

        var result = mount.UpdateSettings("  Design  ", MountVisibility.Public, changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Name, Is.EqualTo("Design"));
        Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Public));
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(changedAt));
        Assert.That(mount.CreatedAt, Is.EqualTo(Now.AddDays(-1)));
        Assert.That(GrantsSnapshot(mount), Is.EqualTo(grantsBefore));
    }

    [Test]
    public void UpdateSettings_ShouldTreatNullFieldsAsOmitted()
    {
        var mount = Restore();

        Assert.That(mount.UpdateSettings("Design", null, Now).IsSuccess, Is.True);
        Assert.That(mount.Name, Is.EqualTo("Design"));
        Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Private));
        Assert.That(mount.Version, Is.EqualTo(2));

        Assert.That(mount.UpdateSettings(null, MountVisibility.Public, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(mount.Name, Is.EqualTo("Design"));
        Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Public));
        Assert.That(mount.Version, Is.EqualTo(3));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public void UpdateSettings_WithEquivalentValues_ShouldSucceedWithoutChangingSnapshot()
    {
        var mount = Restore();

        AssertSuccessUnchanged(
            mount,
            () => mount.UpdateSettings("  Engineering  ", MountVisibility.Private, Now.AddMinutes(1)));
    }

    [TestCaseSource(nameof(InvalidSettingsChanges))]
    public void UpdateSettings_WithInvalidUpdate_ShouldFailWithoutChangingSnapshot(
        Func<Mount, Result> mutate,
        string expectedCode)
    {
        var mount = Restore();

        AssertFailureUnchanged(mount, () => mutate(mount), expectedCode);
    }

    [Test]
    public void ArchiveThenReactivate_ShouldMaintainLifecycleTimestampsAndGrants()
    {
        var mount = Restore();
        var grantsBefore = GrantsSnapshot(mount);
        var archivedAt = Now.AddMinutes(1);
        var reactivatedAt = Now.AddMinutes(2);

        Assert.That(mount.Archive(archivedAt).IsSuccess, Is.True);
        Assert.That(mount.Status, Is.EqualTo(MountStatus.Archived));
        Assert.That(mount.ArchivedAt, Is.EqualTo(archivedAt));
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(archivedAt));

        Assert.That(mount.Reactivate(reactivatedAt).IsSuccess, Is.True);
        Assert.That(mount.Status, Is.EqualTo(MountStatus.Active));
        Assert.That(mount.ArchivedAt, Is.Null);
        Assert.That(mount.Version, Is.EqualTo(3));
        Assert.That(mount.UpdatedAt, Is.EqualTo(reactivatedAt));
        Assert.That(GrantsSnapshot(mount), Is.EqualTo(grantsBefore));
    }

    [TestCase("archive", MountStatus.Archived)]
    [TestCase("reactivate", MountStatus.Active)]
    public void LifecycleOperation_InInvalidState_ShouldReturnRequestConflictWithoutChangingSnapshot(
        string operation,
        MountStatus status)
    {
        var mount = InState(status);

        AssertFailureUnchanged(
            mount,
            () => operation == "archive" ? mount.Archive(Now) : mount.Reactivate(Now),
            ErrorCodes.Common.RequestConflict);
    }

    [Test]
    public void ArchivedMount_ShouldAcceptSettingsAndGrantChanges()
    {
        var mount = InState(MountStatus.Archived);
        var archivedAt = mount.ArchivedAt;

        Assert.That(mount.UpdateSettings("Archived design", null, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(mount.SetGrant(
            MountGrantTargetType.User,
            9,
            [MountPermission.FilesWrite],
            Now.AddMinutes(2)).IsSuccess, Is.True);
        Assert.That(mount.RevokePermissions(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesRead, MountPermission.FilesDownload],
            Now.AddMinutes(3)).IsSuccess, Is.True);

        Assert.That(mount.Status, Is.EqualTo(MountStatus.Archived));
        Assert.That(mount.ArchivedAt, Is.EqualTo(archivedAt));
        Assert.That(mount.Name, Is.EqualTo("Archived design"));
        Assert.That(mount.Grants, Has.Count.EqualTo(1));
        Assert.That(FindGrant(mount, MountGrantTargetType.User, 9).Permissions.SetEquals(
            [MountPermission.FilesWrite]), Is.True);
        Assert.That(mount.Version, Is.EqualTo(4));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(3)));
    }

    [Test]
    public void SetGrant_ShouldReplaceEntireSetAndPreserveOtherTargets()
    {
        var mount = Restore(grants:
        [
            RoleGrant(),
            Grant(MountGrantTargetType.User, 8, MountPermission.FilesRead),
        ]);
        var changedAt = Now.AddMinutes(1);

        var result = mount.SetGrant(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesWrite, MountPermission.FilesWrite],
            changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Grants, Has.Count.EqualTo(2));
        Assert.That(FindGrant(mount, MountGrantTargetType.Role, 7).Permissions.SetEquals(
            [MountPermission.FilesWrite]), Is.True);
        Assert.That(FindGrant(mount, MountGrantTargetType.User, 8).Permissions.SetEquals(
            [MountPermission.FilesRead]), Is.True);
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(changedAt));
    }

    [Test]
    public void SetGrant_WithEmptySet_ShouldRemoveExistingGrant()
    {
        var mount = Restore();

        var result = mount.SetGrant(MountGrantTargetType.Role, 7, [], Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Grants, Is.Empty);
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public void SetGrant_ShouldIsolatePermissionsFromCallerCollectionMutation()
    {
        var mount = Restore(grants: []);
        var source = new List<MountPermission> { MountPermission.FilesRead };

        Assert.That(mount.SetGrant(MountGrantTargetType.User, 9, source, Now).IsSuccess, Is.True);
        source.Clear();
        source.Add(MountPermission.FilesWrite);

        Assert.That(FindGrant(mount, MountGrantTargetType.User, 9).Permissions.SetEquals(
            [MountPermission.FilesRead]), Is.True);
    }

    [Test]
    public void RevokePermissions_ShouldSubtractRequestedSet()
    {
        var mount = Restore(grants:
        [
            Grant(
                MountGrantTargetType.Role,
                7,
                MountPermission.FilesRead,
                MountPermission.FilesDownload,
                MountPermission.FilesWrite),
        ]);

        var result = mount.RevokePermissions(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesDownload, MountPermission.FilesDownload],
            Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(FindGrant(mount, MountGrantTargetType.Role, 7).Permissions.SetEquals(
            [MountPermission.FilesRead, MountPermission.FilesWrite]), Is.True);
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public void RevokePermissions_WithAllGrantedPermissions_ShouldRemoveGrant()
    {
        var mount = Restore(grants:
        [
            Grant(
                MountGrantTargetType.Role,
                7,
                MountPermission.FilesRead,
                MountPermission.FilesDownload,
                MountPermission.FilesWrite),
        ]);

        var result = mount.RevokePermissions(
            MountGrantTargetType.Role,
            7,
            [
                MountPermission.FilesWrite,
                MountPermission.FilesRead,
                MountPermission.FilesDownload,
                MountPermission.FilesRead,
            ],
            Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Grants, Is.Empty);
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public void RevokePermissions_ShouldKeepSeparateRoleAndUserTargets()
    {
        var mount = Restore(grants:
        [
            Grant(MountGrantTargetType.Role, 7, MountPermission.FilesRead),
            Grant(MountGrantTargetType.User, 7, MountPermission.FilesDownload),
        ]);

        var result = mount.RevokePermissions(
            MountGrantTargetType.Role,
            7,
            [MountPermission.FilesRead],
            Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Grants, Has.Count.EqualTo(1));
        Assert.That(FindGrant(mount, MountGrantTargetType.User, 7).Permissions.SetEquals(
            [MountPermission.FilesDownload]), Is.True);
    }

    [Test]
    public void NoOpMutations_ShouldSucceedWithoutChangingSnapshot()
    {
        foreach (var mutation in NoOpMutations())
        {
            var mount = ForNoOpMutation();
            AssertSuccessUnchanged(mount, () => mutation.Apply(mount, Now.AddMinutes(1)));
        }
    }

    [TestCaseSource(nameof(InvalidGrantChanges))]
    public void GrantMutation_WithInvalidInput_ShouldFailWithoutChangingSnapshot(
        Func<Mount, Result> mutate,
        string expectedCode)
    {
        var mount = Restore();

        AssertFailureUnchanged(mount, () => mutate(mount), expectedCode);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Mutations_WithInvalidTime_ShouldFailWithoutChangingSnapshot(bool nonUtc)
    {
        var invalidTime = nonUtc ? Now.ToOffset(TimeSpan.FromHours(9)) : Now.AddTicks(-1);

        foreach (var mutation in RealChanges())
        {
            var mount = ForMutation(mutation);
            AssertFailureUnchanged(
                mount,
                () => mutation.Apply(mount, invalidTime),
                ErrorCodes.Mount.InvalidState);
        }

        foreach (var mutation in NoOpMutations())
        {
            var mount = ForNoOpMutation();
            AssertFailureUnchanged(
                mount,
                () => mutation.Apply(mount, invalidTime),
                ErrorCodes.Mount.InvalidState);
        }
    }

    [Test]
    public void NoOpMutations_AtMaximumVersion_ShouldSucceedWithoutChangingSnapshot()
    {
        foreach (var mutation in NoOpMutations())
        {
            var mount = ForNoOpMutation(long.MaxValue);
            AssertSuccessUnchanged(mount, () => mutation.Apply(mount, Now));
        }
    }

    [Test]
    public void RealChanges_AtMaximumVersion_ShouldFailWithoutChangingSnapshot()
    {
        foreach (var mutation in RealChanges())
        {
            var mount = ForMutation(mutation, long.MaxValue);
            AssertFailureUnchanged(
                mount,
                () => mutation.Apply(mount, Now),
                ErrorCodes.Mount.InvalidState);
        }
    }

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return InvalidSnapshot("negative id", () => Restore(id: -1));
        yield return InvalidSnapshot("empty public id", () => Restore(publicId: Guid.Empty));
        yield return InvalidSnapshot("zero space id", () => Restore(spaceId: 0));
        yield return InvalidSnapshot("null name", () => Restore(name: null!));
        yield return InvalidSnapshot("blank name", () => Restore(name: "   "));
        yield return InvalidSnapshot("long name", () => Restore(name: new string('n', 201)));
        yield return InvalidSnapshot("undefined visibility", () => Restore(visibility: (MountVisibility)99));
        yield return InvalidSnapshot("undefined status", () => Restore(status: (MountStatus)99));
        yield return InvalidSnapshot("zero version", () => Restore(version: 0));
        yield return InvalidSnapshot("negative version", () => Restore(version: -1));
        yield return InvalidSnapshot("non UTC created time", () =>
            Restore(createdAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("non UTC updated time", () =>
            Restore(updatedAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("updated before created", () => Restore(updatedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("active with archived timestamp", () => Restore(archivedAt: Now));
        yield return InvalidSnapshot("archived without timestamp", () => Restore(status: MountStatus.Archived));
        yield return InvalidSnapshot("non UTC archived time", () =>
            Restore(status: MountStatus.Archived, archivedAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("archived before created", () =>
            Restore(status: MountStatus.Archived, archivedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("archived after updated", () =>
            Restore(status: MountStatus.Archived, archivedAt: Now.AddTicks(1)));
        yield return InvalidSnapshot("null grant entry", () => Restore(grants: [null!]));
        yield return InvalidSnapshot("duplicate grant target", () => Restore(grants:
        [
            Grant(MountGrantTargetType.Role, 7, MountPermission.FilesRead),
            Grant(MountGrantTargetType.Role, 7, MountPermission.FilesWrite),
        ]));
    }

    private static IEnumerable<TestCaseData> InvalidSettingsChanges()
    {
        yield return InvalidChange(
            "no settings",
            mount => mount.UpdateSettings(null, null, Now),
            ErrorCodes.Mount.SettingsRequired);
        yield return InvalidChange(
            "blank name and visibility change",
            mount => mount.UpdateSettings(" ", MountVisibility.Public, Now),
            ErrorCodes.Mount.NameInvalid);
        yield return InvalidChange(
            "long name and visibility change",
            mount => mount.UpdateSettings(new string('n', 201), MountVisibility.Public, Now),
            ErrorCodes.Mount.NameInvalid);
        yield return InvalidChange(
            "name change and undefined visibility",
            mount => mount.UpdateSettings("Changed", (MountVisibility)99, Now),
            ErrorCodes.Mount.VisibilityInvalid);
    }

    private static IEnumerable<TestCaseData> InvalidGrantChanges()
    {
        yield return InvalidChange(
            "set null permissions",
            mount => mount.SetGrant(MountGrantTargetType.Role, 7, null!, Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "set undefined target type",
            mount => mount.SetGrant((MountGrantTargetType)99, 7, [MountPermission.FilesRead], Now),
            ErrorCodes.Mount.TargetTypeInvalid);
        yield return InvalidChange(
            "set invalid target id",
            mount => mount.SetGrant(MountGrantTargetType.Role, 0, [MountPermission.FilesRead], Now),
            ErrorCodes.Mount.TargetIdInvalid);
        yield return InvalidChange(
            "set undefined permission",
            mount => mount.SetGrant(MountGrantTargetType.Role, 7, [(MountPermission)99], Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "revoke null permissions",
            mount => mount.RevokePermissions(MountGrantTargetType.Role, 7, null!, Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "revoke undefined target type",
            mount => mount.RevokePermissions((MountGrantTargetType)99, 7, [MountPermission.FilesRead], Now),
            ErrorCodes.Mount.TargetTypeInvalid);
        yield return InvalidChange(
            "revoke invalid target id",
            mount => mount.RevokePermissions(MountGrantTargetType.Role, 0, [MountPermission.FilesRead], Now),
            ErrorCodes.Mount.TargetIdInvalid);
        yield return InvalidChange(
            "revoke undefined permission",
            mount => mount.RevokePermissions(MountGrantTargetType.Role, 7, [(MountPermission)99], Now),
            ErrorCodes.Mount.PermissionsInvalid);
    }

    private static IEnumerable<Mutation> RealChanges()
    {
        yield return new Mutation(
            "settings",
            MountStatus.Active,
            false,
            (mount, now) => mount.UpdateSettings("Changed", MountVisibility.Public, now));
        yield return new Mutation(
            "archive",
            MountStatus.Active,
            false,
            (mount, now) => mount.Archive(now));
        yield return new Mutation(
            "reactivate",
            MountStatus.Archived,
            false,
            (mount, now) => mount.Reactivate(now));
        yield return new Mutation(
            "add grant",
            MountStatus.Active,
            false,
            (mount, now) => mount.SetGrant(
                MountGrantTargetType.User,
                9,
                [MountPermission.FilesRead],
                now));
        yield return new Mutation(
            "replace grant",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetGrant(
                MountGrantTargetType.Role,
                7,
                [MountPermission.FilesWrite],
                now));
        yield return new Mutation(
            "remove grant",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetGrant(MountGrantTargetType.Role, 7, [], now));
        yield return new Mutation(
            "revoke permission",
            MountStatus.Active,
            true,
            (mount, now) => mount.RevokePermissions(
                MountGrantTargetType.Role,
                7,
                [MountPermission.FilesRead],
                now));
    }

    private static IEnumerable<Mutation> NoOpMutations()
    {
        yield return new Mutation(
            "equivalent settings",
            MountStatus.Active,
            true,
            (mount, now) => mount.UpdateSettings(" Engineering ", MountVisibility.Private, now));
        yield return new Mutation(
            "equivalent grant",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetGrant(
                MountGrantTargetType.Role,
                7,
                [MountPermission.FilesDownload, MountPermission.FilesRead, MountPermission.FilesRead],
                now));
        yield return new Mutation(
            "empty set for missing grant",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetGrant(MountGrantTargetType.User, 99, [], now));
        yield return new Mutation(
            "empty revocation",
            MountStatus.Active,
            true,
            (mount, now) => mount.RevokePermissions(MountGrantTargetType.Role, 7, [], now));
        yield return new Mutation(
            "revoke missing grant",
            MountStatus.Active,
            true,
            (mount, now) => mount.RevokePermissions(
                MountGrantTargetType.User,
                99,
                [MountPermission.FilesRead],
                now));
        yield return new Mutation(
            "revoke permission not granted",
            MountStatus.Active,
            true,
            (mount, now) => mount.RevokePermissions(
                MountGrantTargetType.Role,
                7,
                [MountPermission.FilesWrite],
                now));
    }

    private static TestCaseData InvalidSnapshot(string name, Func<Mount> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static TestCaseData InvalidChange(string name, Func<Mount, Result> mutate, string code) =>
        new TestCaseData(mutate, code).SetArgDisplayNames(name, code);

    private static Mount ForMutation(Mutation mutation, long version = 1) =>
        Restore(
            status: mutation.Status,
            version: version,
            archivedAt: mutation.Status == MountStatus.Archived ? Now.AddHours(-1) : null,
            grants: mutation.RequiresGrant
                ? new[] { RoleGrant() }
                : Array.Empty<MountAccessGrant>());

    private static Mount ForNoOpMutation(long version = 1) => Restore(version: version);

    private static Mount InState(MountStatus status, long version = 1) =>
        Restore(
            status: status,
            version: version,
            archivedAt: status == MountStatus.Archived ? Now.AddHours(-1) : null);

    private static Mount Restore(
        long id = 42,
        Guid? publicId = null,
        long spaceId = 7,
        string name = "Engineering",
        MountVisibility visibility = MountVisibility.Private,
        MountStatus status = MountStatus.Active,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? archivedAt = null,
        IEnumerable<MountAccessGrant>? grants = null) =>
        Mount.Reconstitute(
            id,
            publicId ?? Guid.CreateVersion7(),
            spaceId,
            name,
            visibility,
            status,
            version,
            createdAt ?? Now.AddDays(-1),
            updatedAt ?? Now,
            archivedAt,
            grants ?? [RoleGrant()]);

    private static MountAccessGrant RoleGrant() =>
        Grant(
            MountGrantTargetType.Role,
            7,
            MountPermission.FilesRead,
            MountPermission.FilesDownload);

    private static MountAccessGrant Grant(
        MountGrantTargetType targetType,
        long targetId,
        params MountPermission[] permissions) =>
        MountAccessGrant.Create(targetType, targetId, permissions).Value;

    private static MountAccessGrant FindGrant(
        Mount mount,
        MountGrantTargetType targetType,
        long targetId) =>
        mount.Grants.Single(grant => grant.TargetType == targetType && grant.TargetId == targetId);

    private static object?[] Snapshot(Mount mount) =>
    [
        mount.Id,
        mount.PublicId,
        mount.SpaceId,
        mount.Name,
        mount.Visibility,
        mount.Status,
        mount.Version,
        mount.CreatedAt,
        mount.UpdatedAt,
        mount.ArchivedAt,
        GrantsSnapshot(mount),
    ];

    private static string[] GrantsSnapshot(Mount mount) =>
        mount.Grants
            .Select(grant =>
                $"{(int)grant.TargetType}:{grant.TargetId}:{string.Join(",", grant.Permissions.Select(permission => (int)permission).Order())}")
            .Order()
            .ToArray();

    private static void AssertSuccessUnchanged(Mount mount, Func<Result> mutate)
    {
        var before = Snapshot(mount);
        var result = mutate();

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Snapshot(mount), Is.EqualTo(before), "No-op mutation must preserve every field.");
    }

    private static void AssertFailureUnchanged(Mount mount, Func<Result> mutate, string expectedCode)
    {
        var before = Snapshot(mount);
        var result = mutate();

        AssertError(result, expectedCode);
        Assert.That(Snapshot(mount), Is.EqualTo(before), "Failed mutation must preserve every field.");
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
        string Name,
        MountStatus Status,
        bool RequiresGrant,
        Func<Mount, DateTimeOffset, Result> Apply);
}
