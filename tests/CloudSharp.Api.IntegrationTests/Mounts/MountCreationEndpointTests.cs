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
public sealed class MountCreationEndpointTests : MountIntegrationTestBase
{
    private const string ValidBody = """{"name":"Design","visibility":"PRIVATE"}""";

    [Test]
    public async Task CreateMount_ShouldPersistMountAndBothInitialOverwrites()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);
        var role = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Member);
        var body = $$"""
            {"name":"  Design  ","visibility":"PRIVATE",
             "roleOverwrites":[{"rolePublicId":"{{role.PublicId:D}}","allow":["MOUNT_VIEW"],"deny":[]}],
             "userOverwrites":[{"userPublicId":"{{member.PublicId:D}}","allow":[],"deny":["FILES_DOWNLOAD"]}]}
            """;

        using var response = await PostMountAsync(owner, space.PublicId, body, Guid.NewGuid().ToString("N"));
        var created = await ReadMountAsync(response, HttpStatusCode.Created);
        var mountId = created.GetProperty("mountPublicId").GetGuid();
        AssertETag(response, "\"v1\"");
        Assert.That(TestApiClient.GetHeader(response, "Location"), Is.EqualTo(MountPath(space.PublicId, mountId)));

        var stored = (await ReadStoredMountsAsync(space.Id)).Single();
        var targets = await ReadOverwriteTargetsAsync(stored.Id);
        Assert.Multiple(() =>
        {
            Assert.That(created.GetProperty("name").GetString(), Is.EqualTo("Design"));
            Assert.That(created.GetProperty("visibility").GetString(), Is.EqualTo("PRIVATE"));
            Assert.That(created.GetProperty("status").GetString(), Is.EqualTo("ACTIVE"));
            Assert.That(created.GetProperty("archivedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(created.GetProperty("myAccess").GetProperty("canManage").GetBoolean(), Is.True);
            Assert.That(stored.PublicId, Is.EqualTo(mountId));
            Assert.That(stored.Visibility, Is.EqualTo(MountVisibility.Private));
            Assert.That(stored.Version, Is.EqualTo(1));
            Assert.That(targets.Roles, Is.EqualTo(new[] { role.Id }));
            Assert.That(targets.Users, Is.EqualTo(new[] { member.Id }));
        });

        using var detailResponse = await Api.GetAsync(MountPath(space.PublicId, mountId), member.AccessToken);
        var detail = await ReadMountAsync(detailResponse);
        Assert.That(ReadCodes(detail.GetProperty("myAccess"), "permissions"),
            Is.EquivalentTo(new[] { "MOUNT_VIEW", "FILES_READ", "FILES_WRITE" }));
    }

    [Test]
    public async Task CreateMount_ShouldReplayOriginalResponseAndScopeKeyBySpace()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        var key = Guid.NewGuid().ToString("N");
        using var initial = await PostMountAsync(owner, space.PublicId, ValidBody, key);
        var created = await ReadMountAsync(initial, HttpStatusCode.Created);
        var mountId = created.GetProperty("mountPublicId").GetGuid();
        var originalETag = AssertETag(initial, "\"v1\"");
        var path = MountPath(space.PublicId, mountId);

        using var update = await Api.SendJsonAsync(
            HttpMethod.Patch, path, new { name = "Renamed" }, owner.AccessToken, originalETag);
        await ReadMountAsync(update);
        AssertETag(update, "\"v2\"");

        using var retry = await PostMountAsync(owner, space.PublicId,
            """{"userOverwrites":[],"visibility":"PRIVATE","name":"  Design  ","roleOverwrites":[]}""", key);
        var replay = await ReadMountAsync(retry, HttpStatusCode.Created);
        Assert.That(replay.GetRawText(), Is.EqualTo(created.GetRawText()));
        AssertETag(retry, originalETag);
        Assert.That(TestApiClient.GetHeader(retry, "Location"), Is.EqualTo(path));

        using var changed = await PostMountAsync(
            owner, space.PublicId, """{"name":"Different","visibility":"PRIVATE"}""", key);
        await IntegrationAssert.AssertErrorAsync(changed, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_REUSED");

        using var sameKeyInOtherSpace = await PostMountAsync(owner, otherSpace.PublicId, ValidBody, key);
        var other = await ReadMountAsync(sameKeyInOtherSpace, HttpStatusCode.Created);
        Assert.That(other.GetProperty("mountPublicId").GetGuid(), Is.Not.EqualTo(mountId));
        Assert.That((await ReadStoredMountsAsync(space.Id)).Select(mount => (mount.PublicId, mount.Name)),
            Is.EqualTo(new[] { (mountId, "Renamed") }));
    }

    [Test]
    public async Task CreateMount_ShouldRecheckAuthorizationBeforeReplaying()
    {
        var owner = await RegisterSpaceUserAsync();
        var admin = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, admin, owner, roleKeys: [SpaceRoleSystemKey.Admin]);
        var key = Guid.NewGuid().ToString("N");
        using var created = await PostMountAsync(admin, space.PublicId, ValidBody, key);
        await ReadMountAsync(created, HttpStatusCode.Created);

        var roles = await GetSpaceRolesAsync(space.Id);
        var memberRole = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Member);
        using var changedRoles = await Api.SendJsonAsync(HttpMethod.Put,
            $"{SpacePath(space.PublicId)}/memberships/{membership.PublicId:D}/roles",
            new { rolePublicIds = new[] { memberRole.PublicId } }, owner.AccessToken, "\"v1\"");
        Assert.That(changedRoles.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var retry = await PostMountAsync(admin, space.PublicId, ValidBody, key);
        await IntegrationAssert.AssertErrorAsync(retry, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
        Assert.That(await ReadStoredMountsAsync(space.Id), Has.Count.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("short")]
    public async Task CreateMount_ShouldRequireValidIdempotencyKey(string? key)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await PostMountAsync(owner, space.PublicId, ValidBody, key);
        await AssertValidationAsync(response, "idempotencyKey", "INVALID_IDEMPOTENCY_KEY");
        Assert.That(await ReadStoredMountsAsync(space.Id), Is.Empty);
    }

    [TestCase("{")]
    [TestCase("""{"name":"Design"}""")]
    [TestCase("""{"name":"Design","visibility":0}""")]
    [TestCase("""{"name":"Design","visibility":"PRIVATE","unknown":true}""")]
    public async Task CreateMount_ShouldRejectInvalidJson(string json)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await PostMountAsync(owner, space.PublicId, json, Guid.NewGuid().ToString("N"));
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        Assert.That(await ReadStoredMountsAsync(space.Id), Is.Empty);
    }

    [Test]
    public async Task CreateMount_ShouldRejectAnInvalidInitialOverwriteWithoutPersistingMount()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var body = $$"""
            {"name":"Design","visibility":"PRIVATE","userOverwrites":[
                {"userPublicId":"{{owner.PublicId:D}}","allow":["MOUNT_VIEW"],"deny":["MOUNT_VIEW"]}]}
            """;
        var key = Guid.NewGuid().ToString("N");

        using var response = await PostMountAsync(owner, space.PublicId, body, key);
        await AssertValidationAsync(response, "permissions", "MOUNT_PERMISSIONS_INVALID");
        Assert.That(await ReadStoredMountsAsync(space.Id), Is.Empty);

        using var corrected = await PostMountAsync(owner, space.PublicId, ValidBody, key);
        await ReadMountAsync(corrected, HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateMount_ShouldRejectAnArchivedSpace()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);

        using var response = await PostMountAsync(owner, space.PublicId, ValidBody, Guid.NewGuid().ToString("N"));
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "SPACE_ARCHIVED");
        Assert.That(await ReadStoredMountsAsync(space.Id), Is.Empty);
    }
}
