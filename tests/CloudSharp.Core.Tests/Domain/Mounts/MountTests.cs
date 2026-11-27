using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
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
    public void Create_WithInitialOverwrites_ShouldCreateActiveMountWithDefaults()
    {
        var result = Mount.Create(
            11,
            "  Design  ",
            MountVisibility.Private,
            Now,
            [
                Overwrite(MountOverwriteTargetType.Role, 7, allow: [SpacePermission.MountManage, SpacePermission.MountView]),
                Overwrite(MountOverwriteTargetType.User, 7, deny: [SpacePermission.FilesWrite]),
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
        Assert.That(mount.Overwrites, Has.Count.EqualTo(2));
        var role = FindOverwrite(mount, MountOverwriteTargetType.Role, 7);
        Assert.That(role.Allow, Is.EquivalentTo(new[] { SpacePermission.MountView, SpacePermission.MountManage }));
        Assert.That(role.Deny, Is.Empty);
        var user = FindOverwrite(mount, MountOverwriteTargetType.User, 7);
        Assert.That(user.Allow, Is.Empty);
        Assert.That(user.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
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
    public void Create_WithNullOverwriteEntry_ShouldReturnGrantsInvalid()
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
                Overwrite(MountOverwriteTargetType.Role, 7, allow: [SpacePermission.MountView]),
                Overwrite(MountOverwriteTargetType.Role, 7, deny: [SpacePermission.MountManage]),
            ]);

        AssertError(result, ErrorCodes.Mount.GrantsInvalid);
    }

    [Test]
    public void Create_ShouldIsolateOverwritesFromCallerCollectionMutation()
    {
        var source = new List<MountPermissionOverwrite> { RoleOverwrite() };
        var mount = Mount.Create(7, "Design", MountVisibility.Public, Now, source).Value;

        source.Clear();

        Assert.That(mount.Overwrites, Has.Count.EqualTo(1));
        Assert.That(FindOverwrite(mount, MountOverwriteTargetType.Role, 7).Allow,
            Is.EquivalentTo(new[] { SpacePermission.MountView }));
    }

    [Test]
    public void Overwrites_ShouldRejectMutationAttempts()
    {
        var mount = Restore();
        var collection = (ICollection<MountPermissionOverwrite>)mount.Overwrites;

        Assert.That(() => collection.Add(RoleOverwrite()), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Remove(collection.First()), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(mount.Overwrites, Has.Count.EqualTo(1));
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
            original.Overwrites);

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
    public void Reconstitute_ShouldIsolateOverwritesFromCallerCollectionMutation()
    {
        var source = new List<MountPermissionOverwrite> { RoleOverwrite() };
        var mount = Restore(overwrites: source);

        source.Clear();

        Assert.That(mount.Overwrites, Has.Count.EqualTo(1));
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<Mount> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithNullOverwrites_ShouldThrowArgumentNullException()
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
    public void UpdateSettings_ShouldApplyBothFieldsAndPreserveOverwrites()
    {
        var mount = Restore();
        var overwritesBefore = OverwritesSnapshot(mount);
        var changedAt = Now.AddMinutes(1);

        var result = mount.UpdateSettings("  Design  ", MountVisibility.Public, changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Name, Is.EqualTo("Design"));
        Assert.That(mount.Visibility, Is.EqualTo(MountVisibility.Public));
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(changedAt));
        Assert.That(mount.CreatedAt, Is.EqualTo(Now.AddDays(-1)));
        Assert.That(OverwritesSnapshot(mount), Is.EqualTo(overwritesBefore));
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
    public void ArchiveThenReactivate_ShouldMaintainLifecycleTimestampsAndOverwrites()
    {
        var mount = Restore();
        var overwritesBefore = OverwritesSnapshot(mount);
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
        Assert.That(OverwritesSnapshot(mount), Is.EqualTo(overwritesBefore));
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
    public void ArchivedMount_ShouldAcceptSettingsAndOverwriteChanges()
    {
        var mount = InState(MountStatus.Archived);
        var archivedAt = mount.ArchivedAt;

        Assert.That(mount.UpdateSettings("Archived design", null, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(mount.SetOverwrite(
            MountOverwriteTargetType.User,
            9,
            [SpacePermission.MountManage],
            [],
            Now.AddMinutes(2)).IsSuccess, Is.True);
        Assert.That(mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], [], Now.AddMinutes(3)).IsSuccess, Is.True);

        Assert.That(mount.Status, Is.EqualTo(MountStatus.Archived));
        Assert.That(mount.ArchivedAt, Is.EqualTo(archivedAt));
        Assert.That(mount.Name, Is.EqualTo("Archived design"));
        Assert.That(mount.Overwrites, Has.Count.EqualTo(1));
        Assert.That(FindOverwrite(mount, MountOverwriteTargetType.User, 9).Allow,
            Is.EquivalentTo(new[] { SpacePermission.MountManage }));
        Assert.That(mount.Version, Is.EqualTo(4));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(3)));
    }

    [Test]
    public void SetOverwrite_ShouldReplaceBothSetsAndPreserveOtherTargets()
    {
        var mount = Restore(overwrites:
        [
            RoleOverwrite(),
            Overwrite(MountOverwriteTargetType.User, 8, allow: [SpacePermission.MountView]),
        ]);
        var changedAt = Now.AddMinutes(1);

        var result = mount.SetOverwrite(
            MountOverwriteTargetType.Role,
            7,
            [SpacePermission.FilesWrite, SpacePermission.FilesWrite],
            [SpacePermission.MountView],
            changedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Overwrites, Has.Count.EqualTo(2));
        var role = FindOverwrite(mount, MountOverwriteTargetType.Role, 7);
        Assert.That(role.Allow, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
        Assert.That(role.Deny, Is.EquivalentTo(new[] { SpacePermission.MountView }));
        Assert.That(FindOverwrite(mount, MountOverwriteTargetType.User, 8).Allow,
            Is.EquivalentTo(new[] { SpacePermission.MountView }));
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(changedAt));
    }

    [Test]
    public void SetOverwrite_WithEmptySets_ShouldRemoveExistingOverwrite()
    {
        var mount = Restore();

        var result = mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], [], Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Overwrites, Is.Empty);
        Assert.That(mount.FindOverwrite(MountOverwriteTargetType.Role, 7), Is.Null);
        Assert.That(mount.Version, Is.EqualTo(2));
        Assert.That(mount.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    [Test]
    public void SetOverwrite_ShouldIsolatePermissionsFromCallerCollectionMutation()
    {
        var mount = Restore(overwrites: []);
        var allow = new List<SpacePermission> { SpacePermission.MountView };
        var deny = new List<SpacePermission> { SpacePermission.FilesWrite };

        Assert.That(mount.SetOverwrite(MountOverwriteTargetType.User, 9, allow, deny, Now).IsSuccess, Is.True);
        allow.Clear();
        deny.Add(SpacePermission.FilesDownload);

        var stored = FindOverwrite(mount, MountOverwriteTargetType.User, 9);
        Assert.That(stored.Allow, Is.EquivalentTo(new[] { SpacePermission.MountView }));
        Assert.That(stored.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
    }

    [Test]
    public void SetOverwrite_ShouldKeepSeparateRoleAndUserTargets()
    {
        var mount = Restore(overwrites:
        [
            Overwrite(MountOverwriteTargetType.Role, 7, allow: [SpacePermission.MountView]),
            Overwrite(MountOverwriteTargetType.User, 7, allow: [SpacePermission.MountView]),
        ]);

        var result = mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], [], Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(mount.Overwrites, Has.Count.EqualTo(1));
        Assert.That(mount.FindOverwrite(MountOverwriteTargetType.User, 7), Is.Not.Null);
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

    [TestCaseSource(nameof(InvalidOverwriteChanges))]
    public void OverwriteMutation_WithInvalidInput_ShouldFailWithoutChangingSnapshot(
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
        yield return InvalidSnapshot("null overwrite entry", () => Restore(overwrites: [null!]));
        yield return InvalidSnapshot("duplicate overwrite target", () => Restore(overwrites:
        [
            Overwrite(MountOverwriteTargetType.Role, 7, allow: [SpacePermission.MountView]),
            Overwrite(MountOverwriteTargetType.Role, 7, deny: [SpacePermission.MountManage]),
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

    private static IEnumerable<TestCaseData> InvalidOverwriteChanges()
    {
        yield return InvalidChange(
            "null allow",
            mount => mount.SetOverwrite(MountOverwriteTargetType.Role, 7, null!, [], Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "null deny",
            mount => mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], null!, Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "undefined target type",
            mount => mount.SetOverwrite((MountOverwriteTargetType)99, 7, [SpacePermission.MountView], [], Now),
            ErrorCodes.Mount.TargetTypeInvalid);
        yield return InvalidChange(
            "invalid target id",
            mount => mount.SetOverwrite(MountOverwriteTargetType.Role, 0, [SpacePermission.MountView], [], Now),
            ErrorCodes.Mount.TargetIdInvalid);
        yield return InvalidChange(
            "permission outside mount scope",
            mount => mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [SpacePermission.InvitesManage], [], Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "undefined permission",
            mount => mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], [(SpacePermission)99], Now),
            ErrorCodes.Mount.PermissionsInvalid);
        yield return InvalidChange(
            "allow and deny overlap",
            mount => mount.SetOverwrite(
                MountOverwriteTargetType.Role,
                7,
                [SpacePermission.FilesRead],
                [SpacePermission.FilesRead],
                Now),
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
            "add overwrite",
            MountStatus.Active,
            false,
            (mount, now) => mount.SetOverwrite(
                MountOverwriteTargetType.User,
                9,
                [SpacePermission.MountView],
                [],
                now));
        yield return new Mutation(
            "replace overwrite",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetOverwrite(
                MountOverwriteTargetType.Role,
                7,
                [],
                [SpacePermission.MountView],
                now));
        yield return new Mutation(
            "remove overwrite",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetOverwrite(MountOverwriteTargetType.Role, 7, [], [], now));
    }

    private static IEnumerable<Mutation> NoOpMutations()
    {
        yield return new Mutation(
            "equivalent settings",
            MountStatus.Active,
            true,
            (mount, now) => mount.UpdateSettings(" Engineering ", MountVisibility.Private, now));
        yield return new Mutation(
            "equivalent overwrite",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetOverwrite(
                MountOverwriteTargetType.Role,
                7,
                [SpacePermission.MountView, SpacePermission.MountView],
                [],
                now));
        yield return new Mutation(
            "empty sets for missing overwrite",
            MountStatus.Active,
            true,
            (mount, now) => mount.SetOverwrite(MountOverwriteTargetType.User, 99, [], [], now));
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
            overwrites: mutation.RequiresOverwrite
                ? new[] { RoleOverwrite() }
                : Array.Empty<MountPermissionOverwrite>());

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
        IEnumerable<MountPermissionOverwrite>? overwrites = null) =>
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
            overwrites ?? [RoleOverwrite()]);

    private static MountPermissionOverwrite RoleOverwrite() =>
        Overwrite(MountOverwriteTargetType.Role, 7, allow: [SpacePermission.MountView]);

    private static MountPermissionOverwrite Overwrite(
        MountOverwriteTargetType targetType,
        long targetId,
        SpacePermission[]? allow = null,
        SpacePermission[]? deny = null) =>
        MountPermissionOverwrite.Create(targetType, targetId, allow ?? [], deny ?? []).Value;

    private static MountPermissionOverwrite FindOverwrite(
        Mount mount,
        MountOverwriteTargetType targetType,
        long targetId) =>
        mount.FindOverwrite(targetType, targetId)
        ?? throw new AssertionException($"Missing {targetType} overwrite {targetId}.");

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
        OverwritesSnapshot(mount),
    ];

    private static string[] OverwritesSnapshot(Mount mount) =>
        mount.Overwrites
            .Select(overwrite =>
                $"{(int)overwrite.TargetType}:{overwrite.TargetId}"
                + $":{string.Join(",", overwrite.Allow.Select(permission => (int)permission).Order())}"
                + $":{string.Join(",", overwrite.Deny.Select(permission => (int)permission).Order())}")
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
        bool RequiresOverwrite,
        Func<Mount, DateTimeOffset, Result> Apply);
}
