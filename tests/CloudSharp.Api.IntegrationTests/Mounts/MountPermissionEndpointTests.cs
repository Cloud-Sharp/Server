using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Mounts;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MountPermissionEndpointTests : MountIntegrationTestBase
{
    [TestCase("ROLE", "roles")]
    [TestCase("USER", "users")]
    public async Task ReplaceMountPermissions_ShouldPersistReplaceAndRemoveWithMountETag(string targetType, string segment)
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);
        var role = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Member);
        var target = targetType == "ROLE" ? (role.PublicId, role.Id) : (member.PublicId, member.Id);
        var mount = await SeedMountAsync(space, status: MountStatus.Archived);
        var permissions = $"{MountPath(space.PublicId, mount.PublicId)}/permissions";
        var path = $"{permissions}/{segment}/{target.PublicId:D}";
        var allow = new[] { "MOUNT_VIEW", "FILES_READ" };
        var deny = new[] { "FILES_WRITE" };

        using var first = await Api.SendJsonAsync(
            HttpMethod.Put, path, new { allow, deny }, owner.AccessToken, "\"v1\"");
        var overwrite = await ReadJsonAsync(first);
        var etag = AssertETag(first, "\"v2\"");
        IntegrationAssert.AssertPropertyNames(overwrite, "targetType", "targetPublicId", "allow", "deny");
        Assert.Multiple(() =>
        {
            Assert.That(overwrite.GetProperty("targetType").GetString(), Is.EqualTo(targetType));
            Assert.That(overwrite.GetProperty("targetPublicId").GetGuid(), Is.EqualTo(target.PublicId));
            Assert.That(ReadCodes(overwrite, "allow"), Is.EquivalentTo(allow));
            Assert.That(ReadCodes(overwrite, "deny"), Is.EqualTo(deny));
        });

        using var listing = await Api.GetAsync($"{permissions}?targetType={targetType}", owner.AccessToken);
        var listed = await ReadJsonAsync(listing);
        IntegrationAssert.AssertPropertyNames(listed, "items", "nextCursor");
        Assert.That(listed.GetProperty("items").EnumerateArray().Single().GetRawText(), Is.EqualTo(overwrite.GetRawText()));
        var targets = await ReadOverwriteTargetsAsync(mount.Id);
        Assert.That(targetType == "ROLE" ? targets.Roles : targets.Users, Is.EqualTo(new[] { target.Id }));

        using var noOp = await Api.SendJsonAsync(HttpMethod.Put, path,
            new { allow = allow.Reverse().ToArray(), deny }, owner.AccessToken, etag);
        Assert.That((await ReadJsonAsync(noOp)).GetRawText(), Is.EqualTo(overwrite.GetRawText()));
        AssertETag(noOp, etag);

        var empty = Array.Empty<string>();
        using var removed = await Api.SendJsonAsync(HttpMethod.Put, path,
            new { allow = empty, deny = empty }, owner.AccessToken, etag);
        var removedOverwrite = await ReadJsonAsync(removed);
        AssertETag(removed, "\"v3\"");
        Assert.That(ReadCodes(removedOverwrite, "allow"), Is.Empty);
        Assert.That(ReadCodes(removedOverwrite, "deny"), Is.Empty);
        var finalTargets = await ReadOverwriteTargetsAsync(mount.Id);
        Assert.That(finalTargets.Roles, Is.Empty);
        Assert.That(finalTargets.Users, Is.Empty);
        Assert.That((await ReadStoredMountsAsync(space.Id)).Single().Version, Is.EqualTo(3));
    }

    [Test]
    public async Task ListMountPermissions_ShouldBindCursorToTargetType()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var roles = (await GetSpaceRolesAsync(space.Id)).OrderBy(role => role.PublicId).Take(2).ToArray();
        var body = $$"""
            {"name":"Design","visibility":"PUBLIC","roleOverwrites":[
                {"rolePublicId":"{{roles[1].PublicId:D}}","allow":["MOUNT_VIEW"],"deny":[]},
                {"rolePublicId":"{{roles[0].PublicId:D}}","allow":["MOUNT_VIEW"],"deny":[]}]}
            """;
        using var created = await PostMountAsync(owner, space.PublicId, body, Guid.NewGuid().ToString("N"));
        var mount = await ReadMountAsync(created, HttpStatusCode.Created);
        var permissions = $"{MountPath(space.PublicId, mount.GetProperty("mountPublicId").GetGuid())}/permissions";

        using var first = await Api.GetAsync($"{permissions}?targetType=ROLE&limit=1", owner.AccessToken);
        var page = await ReadJsonAsync(first);
        Assert.That(page.GetProperty("items").EnumerateArray().Single().GetProperty("targetPublicId").GetGuid(),
            Is.EqualTo(roles[0].PublicId));
        var cursor = page.GetProperty("nextCursor").GetString();
        Assert.That(cursor, Is.Not.Null.And.Not.Empty);

        using var second = await Api.GetAsync($"{permissions}?targetType=ROLE&cursor={cursor}", owner.AccessToken);
        var finalPage = await ReadJsonAsync(second);
        Assert.That(finalPage.GetProperty("items").EnumerateArray().Single().GetProperty("targetPublicId").GetGuid(),
            Is.EqualTo(roles[1].PublicId));
        Assert.That(finalPage.GetProperty("nextCursor").ValueKind, Is.EqualTo(JsonValueKind.Null));

        using var wrongType = await Api.GetAsync($"{permissions}?targetType=USER&cursor={cursor}", owner.AccessToken);
        await IntegrationAssert.AssertErrorAsync(wrongType, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    [Test]
    public async Task ReplaceMountPermissions_ShouldRejectEscalationByMountManager()
    {
        var owner = await RegisterSpaceUserAsync();
        var viewer = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, viewer, owner, roleKeys: [SpaceRoleSystemKey.Viewer]);
        var role = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var mount = await SeedMountAsync(space);
        var permissions = $"{MountPath(space.PublicId, mount.PublicId)}/permissions";
        using var grant = await Api.SendJsonAsync(HttpMethod.Put, $"{permissions}/users/{viewer.PublicId:D}",
            new { allow = new[] { "MOUNT_MANAGE" }, deny = Array.Empty<string>() }, owner.AccessToken, "\"v1\"");
        await ReadJsonAsync(grant);
        var etag = AssertETag(grant, "\"v2\"");

        using var list = await Api.GetAsync($"{permissions}?targetType=ROLE", viewer.AccessToken);
        await ReadJsonAsync(list);
        using var escalation = await Api.SendJsonAsync(HttpMethod.Put, $"{permissions}/roles/{role.PublicId:D}",
            new { allow = new[] { "FILES_WRITE" }, deny = Array.Empty<string>() }, viewer.AccessToken, etag);
        await IntegrationAssert.AssertErrorAsync(escalation, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        Assert.That((await ReadOverwriteTargetsAsync(mount.Id)).Roles, Is.Empty);
        Assert.That((await ReadStoredMountsAsync(space.Id)).Single().Version, Is.EqualTo(2));
    }

    [Test]
    public async Task PreviewMountPermissions_ShouldExplainProposedVisibilityInArchivedSpace()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);
        await SeedMembershipAsync(space, member, owner);
        var mount = await SeedMountAsync(space);
        var permissions = $"{MountPath(space.PublicId, mount.PublicId)}/permissions";

        using var response = await Api.GetAsync(
            $"{permissions}/preview?userPublicId={member.PublicId:D}&visibility=PRIVATE", owner.AccessToken);
        var result = await ReadJsonAsync(response);
        var item = result.GetProperty("items").EnumerateArray().Single();
        IntegrationAssert.AssertPropertyNames(item,
            "userPublicId", "currentPermissions", "previewPermissions", "currentSources", "previewSources");
        var sources = item.GetProperty("previewSources").EnumerateArray().ToArray();
        foreach (var source in sources)
        {
            IntegrationAssert.AssertPropertyNames(source, "sourceType", "allow", "deny", "rolePublicId");
        }

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("nextCursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(item.GetProperty("userPublicId").GetGuid(), Is.EqualTo(member.PublicId));
            Assert.That(ReadCodes(item, "currentPermissions"),
                Is.EquivalentTo(new[] { "MOUNT_VIEW", "FILES_READ", "FILES_DOWNLOAD" }));
            Assert.That(ReadCodes(item, "previewPermissions"), Is.Empty);
            Assert.That(sources.Select(source => source.GetProperty("sourceType").GetString()),
                Is.EqualTo(new[] { "BASE", "VISIBILITY" }));
            Assert.That(ReadCodes(sources[1], "deny"), Is.EqualTo(new[] { "MOUNT_VIEW" }));
            Assert.That(sources[1].GetProperty("rolePublicId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });

        using var listResponse = await Api.GetAsync($"{permissions}?targetType=ROLE", owner.AccessToken);
        await ReadJsonAsync(listResponse);
        using var write = await Api.SendJsonAsync(HttpMethod.Put, $"{permissions}/users/{member.PublicId:D}",
            new { allow = new[] { "MOUNT_VIEW" }, deny = Array.Empty<string>() }, owner.AccessToken, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(write, HttpStatusCode.Conflict, "SPACE_ARCHIVED");
    }

    [Test]
    public async Task PreviewMountPermissions_ShouldPageOnlyUsersWhosePermissionsChange()
    {
        var owner = await RegisterSpaceUserAsync();
        var firstMember = await RegisterSpaceUserAsync();
        var secondMember = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, firstMember, owner);
        await SeedMembershipAsync(space, secondMember, owner);
        var mount = await SeedMountAsync(space);
        var path = $"{MountPath(space.PublicId, mount.PublicId)}/permissions/preview";

        using var first = await Api.GetAsync($"{path}?visibility=PRIVATE&limit=1", owner.AccessToken);
        var page = await ReadJsonAsync(first);
        var cursor = page.GetProperty("nextCursor").GetString();
        Assert.That(cursor, Is.Not.Null.And.Not.Empty);

        using var second = await Api.GetAsync($"{path}?visibility=PRIVATE&cursor={cursor}", owner.AccessToken);
        var finalPage = await ReadJsonAsync(second);
        var actualIds = new[] { page, finalPage }.Select(current => current.GetProperty("items")
            .EnumerateArray().Single().GetProperty("userPublicId").GetGuid());
        Assert.That(actualIds, Is.EqualTo(new[] { firstMember.PublicId, secondMember.PublicId }.Order()));
        Assert.That(finalPage.GetProperty("nextCursor").ValueKind, Is.EqualTo(JsonValueKind.Null));

        using var changed = await Api.GetAsync($"{path}?visibility=PUBLIC&cursor={cursor}", owner.AccessToken);
        await IntegrationAssert.AssertErrorAsync(changed, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    [TestCase("", "targetType", "MOUNT_GRANT_TARGET_TYPE_INVALID")]
    [TestCase("?targetType=0", "targetType", "MOUNT_GRANT_TARGET_TYPE_INVALID")]
    [TestCase("/preview", "userPublicId", "MOUNT_PREVIEW_INPUT_REQUIRED")]
    [TestCase("/preview?visibility=0", "visibility", "MOUNT_VISIBILITY_INVALID")]
    [TestCase("/preview?userPublicId=invalid", "userPublicId", "MOUNT_GRANT_TARGET_ID_INVALID")]
    public async Task ReadMountPermissions_ShouldValidateQuery(string suffix, string field, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space);

        using var response = await Api.GetAsync(
            $"{MountPath(space.PublicId, mount.PublicId)}/permissions{suffix}", owner.AccessToken);
        await AssertValidationAsync(response, field, code);
    }
}
