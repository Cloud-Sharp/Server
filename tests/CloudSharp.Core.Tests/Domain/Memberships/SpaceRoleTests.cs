using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Memberships;

[TestFixture]
public class SpaceRoleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 22, 3, 30, 0, TimeSpan.Zero);

    [TestCaseSource(nameof(ManagedRoleCases))]
    public void CreateManaged_WithDefinedSystemKey_ShouldCreateRoleFromTemplate(
        SpaceRoleSystemKey systemKey,
        string expectedName,
        int expectedPosition)
    {
        var result = SpaceRole.CreateManaged(7, systemKey, Now);

        Assert.That(result.IsSuccess, Is.True);
        var role = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(role.Id, Is.Zero);
            Assert.That(role.PublicId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(role.PublicId.ToString("D")[14], Is.EqualTo('7'));
            Assert.That(role.SpaceId, Is.EqualTo(7));
            Assert.That(role.Name, Is.EqualTo(expectedName));
            Assert.That(role.NormalizedName, Is.EqualTo(expectedName.ToUpperInvariant()));
            Assert.That(role.SystemKey, Is.EqualTo(systemKey));
            Assert.That(role.Position, Is.EqualTo(expectedPosition));
            Assert.That(role.IsManaged, Is.True);
            Assert.That(role.Version, Is.EqualTo(1));
            Assert.That(role.CreatedAt, Is.EqualTo(Now));
            Assert.That(role.UpdatedAt, Is.EqualTo(Now));
            Assert.That(role.Color, Is.Null);
            Assert.That(role.DeletedAt, Is.Null);
        });
    }

    [Test]
    public void CreateManaged_WithAdmin_ShouldGrantEveryDefinedPermission()
    {
        var role = SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Admin, Now).Value;

        Assert.That(role.Permissions.SetEquals(Enum.GetValues<SpacePermission>()), Is.True);
    }

    [TestCaseSource(nameof(DefaultPermissionCases))]
    public void CreateManaged_WithNonAdminSystemKey_ShouldGrantTablePermissions(
        SpaceRoleSystemKey systemKey,
        SpacePermission[] expected)
    {
        var role = SpaceRole.CreateManaged(7, systemKey, Now).Value;

        Assert.That(role.Permissions.SetEquals(expected), Is.True);
    }

    [Test]
    public void CreateManaged_MemberPermissions_ShouldExtendViewerPermissions()
    {
        var viewer = SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Viewer, Now).Value;
        var member = SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Member, Now).Value;

        Assert.That(member.Permissions.IsSupersetOf(viewer.Permissions), Is.True);
        Assert.That(member.Permissions.Count, Is.EqualTo(viewer.Permissions.Count + 5));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void CreateManaged_WithInvalidSpaceId_ShouldFail(long spaceId)
    {
        AssertError(
            SpaceRole.CreateManaged(spaceId, SpaceRoleSystemKey.Admin, Now),
            ErrorCodes.SpaceRole.SpaceIdInvalid);
    }

    [TestCase(0)]
    [TestCase(4)]
    [TestCase(-1)]
    [TestCase(99)]
    public void CreateManaged_WithUndefinedSystemKey_ShouldFail(int undefinedValue)
    {
        AssertError(
            SpaceRole.CreateManaged(7, (SpaceRoleSystemKey)undefinedValue, Now),
            ErrorCodes.SpaceRole.SystemKeyInvalid);
    }

    [Test]
    public void CreateManaged_WithNonUtcNow_ShouldFail()
    {
        AssertError(
            SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Admin, Now.ToOffset(TimeSpan.FromHours(9))),
            ErrorCodes.SpaceRole.InvalidState);
    }

    [Test]
    public void Reconstitute_WithValidSnapshot_ShouldRoundTripEveryField()
    {
        var original = Restore(
            name: "Designer",
            normalizedName: "DESIGNER",
            systemKey: null,
            color: "#1a2B3c",
            position: 5,
            isManaged: false,
            version: 9,
            deletedAt: Now,
            permissions: [SpacePermission.SharesView, SpacePermission.TagsManage]);

        var restored = SpaceRole.Reconstitute(
            original.Id, original.PublicId, original.SpaceId, original.Name, original.NormalizedName,
            original.SystemKey, original.Color, original.Position, original.IsManaged, original.Version,
            original.CreatedAt, original.UpdatedAt, original.DeletedAt,
            [SpacePermission.SharesView, SpacePermission.TagsManage]);

        Assert.That(Snapshot(restored), Is.EqualTo(Snapshot(original)));
        Assert.That(restored, Is.Not.SameAs(original));
    }

    [TestCaseSource(nameof(AdminStoredPermissionCases))]
    public void Reconstitute_WithAdminSystemKey_ShouldExposeEveryDefinedPermission(
        SpacePermission[] storedPermissions)
    {
        var role = Restore(
            name: "Admin",
            normalizedName: "ADMIN",
            systemKey: SpaceRoleSystemKey.Admin,
            position: 2,
            version: 7,
            permissions: storedPermissions);

        Assert.Multiple(() =>
        {
            Assert.That(role.Permissions.SetEquals(Enum.GetValues<SpacePermission>()), Is.True);
            Assert.That(role.Version, Is.EqualTo(7));
            Assert.That(role.UpdatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void Reconstitute_WithViewerStoredPermissions_ShouldUseStoredSetAsIs()
    {
        var role = Restore(
            name: "Viewer",
            normalizedName: "VIEWER",
            systemKey: SpaceRoleSystemKey.Viewer,
            position: 0,
            permissions: [SpacePermission.MembersManage]);

        Assert.That(role.Permissions.SetEquals([SpacePermission.MembersManage]), Is.True);
    }

    [Test]
    public void Reconstitute_WithEmptyStoredPermissions_ShouldExposeEmptySetForNonAdmin()
    {
        var role = Restore(permissions: []);

        Assert.That(role.Permissions, Is.Empty);
    }

    [Test]
    public void Reconstitute_WithDuplicateStoredPermissions_ShouldKeepUniqueValues()
    {
        var role = Restore(
            permissions: [SpacePermission.SpaceView, SpacePermission.SpaceView, SpacePermission.FilesRead]);

        Assert.That(
            role.Permissions.SetEquals([SpacePermission.SpaceView, SpacePermission.FilesRead]), Is.True);
    }

    [Test]
    public void Reconstitute_ShouldCopyPermissionsFromSourceCollection()
    {
        var source = new List<SpacePermission> { SpacePermission.SpaceView, SpacePermission.FilesRead };
        var role = Restore(
            name: "Designer",
            normalizedName: "DESIGNER",
            systemKey: null,
            isManaged: false,
            permissions: source);

        source.Add(SpacePermission.TagsManage);
        source.Remove(SpacePermission.SpaceView);

        Assert.That(
            role.Permissions.SetEquals([SpacePermission.SpaceView, SpacePermission.FilesRead]), Is.True);
    }

    [Test]
    public void Permissions_ShouldRejectMutationAttempts()
    {
        var role = SpaceRole.CreateManaged(7, SpaceRoleSystemKey.Viewer, Now).Value;
        var snapshot = role.Permissions.ToArray();

        var collection = (ICollection<SpacePermission>)role.Permissions;

        Assert.That(() => collection.Add(SpacePermission.TagsManage), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Remove(SpacePermission.SpaceView), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => collection.Clear(), Throws.TypeOf<NotSupportedException>());
        Assert.That(role.Permissions.ToArray(), Is.EquivalentTo(snapshot));
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<SpaceRole> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [TestCase("red")]
    [TestCase("#FFF")]
    [TestCase("FFFFFF")]
    [TestCase("#GGGGGG")]
    [TestCase("#12345")]
    [TestCase("#1234567")]
    public void Reconstitute_WithInvalidColor_ShouldThrow(string color)
    {
        Assert.That(() => Restore(color: color), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithUndefinedSystemKey_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(systemKey: (SpaceRoleSystemKey)99));
    }

    [Test]
    public void Reconstitute_WithUndefinedStoredPermission_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(permissions: [(SpacePermission)99]));
    }

    [Test]
    public void Reconstitute_WithNullPermissions_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpaceRole.Reconstitute(
            42, Guid.CreateVersion7(), 7, "Member", "MEMBER", SpaceRoleSystemKey.Member, null, 1, true,
            1, Now.AddDays(-1), Now, null, null!));
    }

    private static IEnumerable<TestCaseData> ManagedRoleCases()
    {
        yield return new TestCaseData(SpaceRoleSystemKey.Admin, "Admin", 2).SetArgDisplayNames("admin");
        yield return new TestCaseData(SpaceRoleSystemKey.Member, "Member", 1).SetArgDisplayNames("member");
        yield return new TestCaseData(SpaceRoleSystemKey.Viewer, "Viewer", 0).SetArgDisplayNames("viewer");
    }

    private static IEnumerable<TestCaseData> DefaultPermissionCases()
    {
        yield return new TestCaseData(
                SpaceRoleSystemKey.Viewer,
                new[]
                {
                    SpacePermission.SpaceView,
                    SpacePermission.FilesRead,
                    SpacePermission.FilesDownload,
                })
            .SetArgDisplayNames("viewer");
        yield return new TestCaseData(
                SpaceRoleSystemKey.Member,
                new[]
                {
                    SpacePermission.SpaceView,
                    SpacePermission.FilesRead,
                    SpacePermission.FilesDownload,
                    SpacePermission.FilesWrite,
                    SpacePermission.SharesView,
                    SpacePermission.SharesManage,
                    SpacePermission.TagsManage,
                    SpacePermission.MembersView,
                })
            .SetArgDisplayNames("member");
    }

    private static IEnumerable<TestCaseData> AdminStoredPermissionCases()
    {
        yield return new TestCaseData(Array.Empty<SpacePermission>())
            .SetArgDisplayNames("empty stored set");
        yield return new TestCaseData(new[] { SpacePermission.SpaceView })
            .SetArgDisplayNames("partial stored set");
        yield return new TestCaseData(Enum.GetValues<SpacePermission>())
            .SetArgDisplayNames("complete stored set");
    }

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return InvalidSnapshot("negative id", () => Restore(id: -1));
        yield return InvalidSnapshot("empty public id", () => Restore(publicId: Guid.Empty));
        yield return InvalidSnapshot("zero space id", () => Restore(spaceId: 0));
        yield return InvalidSnapshot("blank name", () => Restore(name: "   "));
        yield return InvalidSnapshot("long name", () => Restore(name: new string('n', 101)));
        yield return InvalidSnapshot("blank normalized name", () => Restore(name: "Member", normalizedName: " "));
        yield return InvalidSnapshot("long normalized name", () =>
            Restore(name: new string('n', 100), normalizedName: new string('n', 101)));
        yield return InvalidSnapshot("normalized name mismatch", () =>
            Restore(name: "Team Lead", normalizedName: "TEAMLEAD"));
        yield return InvalidSnapshot("normalized name not upper-cased", () =>
            Restore(name: "Member", normalizedName: "member"));
        yield return InvalidSnapshot("normalized name not trimmed", () =>
            Restore(name: "Member", normalizedName: " MEMBER "));
        yield return InvalidSnapshot("negative position", () => Restore(position: -1));
        yield return InvalidSnapshot("zero version", () => Restore(version: 0));
        yield return InvalidSnapshot("non UTC created time", () =>
            Restore(createdAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("updated before created", () => Restore(updatedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("non UTC deleted time", () =>
            Restore(deletedAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("deleted before created", () => Restore(deletedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("deleted after updated", () => Restore(deletedAt: Now.AddMinutes(1)));
    }

    private static TestCaseData InvalidSnapshot(string name, Func<SpaceRole> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static SpaceRole Restore(
        long id = 42,
        Guid? publicId = null,
        long spaceId = 7,
        string name = "Member",
        string? normalizedName = null,
        SpaceRoleSystemKey? systemKey = SpaceRoleSystemKey.Member,
        string? color = null,
        int position = 1,
        bool isManaged = true,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? deletedAt = null,
        IEnumerable<SpacePermission>? permissions = null) =>
        SpaceRole.Reconstitute(
            id,
            publicId ?? Guid.CreateVersion7(),
            spaceId,
            name,
            normalizedName ?? name.Trim().ToUpperInvariant(),
            systemKey,
            color,
            position,
            isManaged,
            version,
            createdAt ?? Now.AddDays(-1),
            updatedAt ?? Now,
            deletedAt,
            permissions ?? [SpacePermission.SpaceView, SpacePermission.FilesRead]);

    private static object?[] Snapshot(SpaceRole role) =>
    [
        role.Id, role.PublicId, role.SpaceId, role.Name, role.NormalizedName, role.SystemKey,
        role.Color, role.Position, role.IsManaged, role.Version, role.CreatedAt, role.UpdatedAt,
        role.DeletedAt, role.Permissions.Order().ToArray()
    ];

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }
}