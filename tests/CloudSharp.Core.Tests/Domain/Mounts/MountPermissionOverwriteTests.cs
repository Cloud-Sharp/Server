using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Mounts;

[TestFixture]
public class MountPermissionOverwriteTests
{
    [Test]
    public void Create_ShouldDeduplicatePermissionsIndependentOfOrder()
    {
        var first = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role,
            7,
            [SpacePermission.FilesWrite, SpacePermission.MountView, SpacePermission.FilesWrite],
            [SpacePermission.FilesDownload, SpacePermission.FilesDownload]);
        var second = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role,
            7,
            [SpacePermission.MountView, SpacePermission.FilesWrite],
            [SpacePermission.FilesDownload]);

        Assert.That(first.IsSuccess, Is.True);
        Assert.That(second.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(first.Value.Allow.SetEquals(second.Value.Allow), Is.True);
            Assert.That(first.Value.Deny.SetEquals(second.Value.Deny), Is.True);
            Assert.That(first.Value.Allow, Has.Count.EqualTo(2));
            Assert.That(first.Value.Deny, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Create_ShouldCopyTargetAndPermissions()
    {
        var result = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.User,
            42,
            [SpacePermission.MountView],
            [SpacePermission.FilesWrite]);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.TargetType, Is.EqualTo(MountOverwriteTargetType.User));
            Assert.That(result.Value.TargetId, Is.EqualTo(42));
            Assert.That(result.Value.Allow, Is.EquivalentTo(new[] { SpacePermission.MountView }));
            Assert.That(result.Value.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
            Assert.That(result.Value.IsEmpty, Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Create_WithOnlyOneSide_ShouldSucceed(bool allowOnly)
    {
        SpacePermission[] permissions = [SpacePermission.MountView];

        var result = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role,
            7,
            allowOnly ? permissions : [],
            allowOnly ? [] : permissions);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public void Create_ShouldIsolatePermissionsFromCallerCollectionMutation()
    {
        var allow = new List<SpacePermission> { SpacePermission.MountView };
        var deny = new List<SpacePermission> { SpacePermission.FilesWrite };
        var overwrite = MountPermissionOverwrite.Create(MountOverwriteTargetType.Role, 7, allow, deny).Value;

        allow.Clear();
        deny.Add(SpacePermission.FilesDownload);

        Assert.That(overwrite.Allow, Is.EquivalentTo(new[] { SpacePermission.MountView }));
        Assert.That(overwrite.Deny, Is.EquivalentTo(new[] { SpacePermission.FilesWrite }));
    }

    [Test]
    public void Permissions_ShouldRejectMutationAttempts()
    {
        var overwrite = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role,
            7,
            [SpacePermission.MountView],
            [SpacePermission.FilesWrite]).Value;
        var allow = (ICollection<SpacePermission>)overwrite.Allow;
        var deny = (ICollection<SpacePermission>)overwrite.Deny;

        Assert.That(() => allow.Add(SpacePermission.MountManage), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => deny.Clear(), Throws.TypeOf<NotSupportedException>());
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(99)]
    public void Create_WithUndefinedTargetType_ShouldReturnTargetTypeInvalid(int value)
    {
        var result = MountPermissionOverwrite.Create(
            (MountOverwriteTargetType)value,
            7,
            [SpacePermission.MountView],
            []);

        AssertError(result, ErrorCodes.Mount.TargetTypeInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidTargetId_ShouldReturnTargetIdInvalid(long targetId)
    {
        var result = MountPermissionOverwrite.Create(
            MountOverwriteTargetType.Role,
            targetId,
            [SpacePermission.MountView],
            []);

        AssertError(result, ErrorCodes.Mount.TargetIdInvalid);
    }

    [TestCaseSource(nameof(InvalidPermissionSets))]
    public void Create_WithInvalidPermissions_ShouldReturnPermissionsInvalid(
        SpacePermission[]? allow,
        SpacePermission[]? deny)
    {
        var result = MountPermissionOverwrite.Create(MountOverwriteTargetType.Role, 7, allow!, deny!);

        AssertError(result, ErrorCodes.Mount.PermissionsInvalid);
    }

    [Test]
    public void Reconstitute_WithValidSnapshot_ShouldPreserveEveryField()
    {
        var overwrite = MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.User,
            9,
            [SpacePermission.FilesRead],
            [SpacePermission.MountManage]);

        Assert.Multiple(() =>
        {
            Assert.That(overwrite.TargetType, Is.EqualTo(MountOverwriteTargetType.User));
            Assert.That(overwrite.TargetId, Is.EqualTo(9));
            Assert.That(overwrite.Allow, Is.EquivalentTo(new[] { SpacePermission.FilesRead }));
            Assert.That(overwrite.Deny, Is.EquivalentTo(new[] { SpacePermission.MountManage }));
        });
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<MountPermissionOverwrite> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ChangedPermissions_ShouldReturnPermissionsWhoseAllowOrDenyStateChanges()
    {
        var current = Overwrite(
            allow: [SpacePermission.MountView, SpacePermission.FilesRead],
            deny: [SpacePermission.FilesWrite]);
        var next = Overwrite(
            allow: [SpacePermission.MountView, SpacePermission.FilesWrite],
            deny: [SpacePermission.FilesDownload]);

        var changed = MountPermissionOverwrite.ChangedPermissions(current, next);

        Assert.That(changed, Is.EquivalentTo(new[]
        {
            SpacePermission.FilesRead,
            SpacePermission.FilesWrite,
            SpacePermission.FilesDownload,
        }));
    }

    [Test]
    public void ChangedPermissions_WithoutCurrentOverwrite_ShouldReturnEveryPermissionOfNext()
    {
        var next = Overwrite(allow: [SpacePermission.MountView], deny: [SpacePermission.FilesWrite]);

        var changed = MountPermissionOverwrite.ChangedPermissions(null, next);

        Assert.That(changed, Is.EquivalentTo(new[] { SpacePermission.MountView, SpacePermission.FilesWrite }));
    }

    private static IEnumerable<TestCaseData> InvalidPermissionSets()
    {
        yield return new TestCaseData(null, Array.Empty<SpacePermission>()).SetArgDisplayNames("null allow");
        yield return new TestCaseData(Array.Empty<SpacePermission>(), null).SetArgDisplayNames("null deny");
        yield return new TestCaseData(Array.Empty<SpacePermission>(), Array.Empty<SpacePermission>())
            .SetArgDisplayNames("empty");
        yield return new TestCaseData(new[] { SpacePermission.SpaceView }, Array.Empty<SpacePermission>())
            .SetArgDisplayNames("allow outside mount scope");
        yield return new TestCaseData(Array.Empty<SpacePermission>(), new[] { SpacePermission.MembersManage })
            .SetArgDisplayNames("deny outside mount scope");
        yield return new TestCaseData(new[] { (SpacePermission)99 }, Array.Empty<SpacePermission>())
            .SetArgDisplayNames("undefined permission");
        yield return new TestCaseData(
                new[] { SpacePermission.MountView, SpacePermission.FilesRead },
                new[] { SpacePermission.FilesRead })
            .SetArgDisplayNames("allow and deny overlap");
    }

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return Snapshot("undefined target type", () => MountPermissionOverwrite.Reconstitute(
            (MountOverwriteTargetType)99, 7, [SpacePermission.MountView], []));
        yield return Snapshot("invalid target id", () => MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.Role, 0, [SpacePermission.MountView], []));
        yield return Snapshot("empty", () => MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.Role, 7, [], []));
        yield return Snapshot("outside mount scope", () => MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.Role, 7, [SpacePermission.SpaceUpdate], []));
        yield return Snapshot("overlap", () => MountPermissionOverwrite.Reconstitute(
            MountOverwriteTargetType.Role, 7, [SpacePermission.MountView], [SpacePermission.MountView]));
    }

    private static TestCaseData Snapshot(string name, Func<MountPermissionOverwrite> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static MountPermissionOverwrite Overwrite(SpacePermission[] allow, SpacePermission[] deny) =>
        MountPermissionOverwrite.Create(MountOverwriteTargetType.Role, 7, allow, deny).Value;

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }
}
