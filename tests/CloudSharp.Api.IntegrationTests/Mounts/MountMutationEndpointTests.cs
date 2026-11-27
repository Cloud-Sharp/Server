using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Mounts;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Mounts;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MountMutationEndpointTests : MountIntegrationTestBase
{
    [Test]
    public async Task UpdateMount_ShouldChangeOnlySuppliedSettingsAndKeepETagForNoOp()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space, MountVisibility.Private, MountStatus.Archived);
        var path = MountPath(space.PublicId, mount.PublicId);
        using var detail = await Api.GetAsync(path, owner.AccessToken);
        var before = await ReadMountAsync(detail);
        var initialETag = AssertETag(detail, "\"v1\"");

        using var update = await Api.SendJsonAsync(HttpMethod.Patch, path,
            new { name = "  Renamed  " }, owner.AccessToken, initialETag);
        var updated = await ReadMountAsync(update);
        var currentETag = AssertETag(update, "\"v2\"");

        using var noOp = await Api.SendJsonAsync(HttpMethod.Patch, path,
            new { name = "Renamed", visibility = "PRIVATE" }, owner.AccessToken, currentETag);
        Assert.That((await ReadMountAsync(noOp)).GetRawText(), Is.EqualTo(updated.GetRawText()));
        AssertETag(noOp, currentETag);

        var stored = (await ReadStoredMountsAsync(space.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(updated.GetProperty("name").GetString(), Is.EqualTo("Renamed"));
            Assert.That(updated.GetProperty("visibility").GetString(), Is.EqualTo("PRIVATE"));
            Assert.That(updated.GetProperty("archivedAt").GetDateTimeOffset(),
                Is.EqualTo(before.GetProperty("archivedAt").GetDateTimeOffset()));
            Assert.That(stored.Name, Is.EqualTo("Renamed"));
            Assert.That(stored.Version, Is.EqualTo(2));
            Assert.That(stored.Status, Is.EqualTo(MountStatus.Archived));
        });
    }

    [Test]
    public async Task UpdateMount_ShouldApplyVisibilityAndPreserveOverwrites()
    {
        var owner = await RegisterSpaceUserAsync();
        var allowed = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, allowed, owner);
        await SeedMembershipAsync(space, member, owner);
        var mount = await SeedMountAsync(space);
        var path = MountPath(space.PublicId, mount.PublicId);

        using var grant = await Api.SendJsonAsync(HttpMethod.Put, $"{path}/permissions/users/{allowed.PublicId:D}",
            new { allow = new[] { "MOUNT_VIEW" }, deny = Array.Empty<string>() }, owner.AccessToken, "\"v1\"");
        await ReadJsonAsync(grant);
        var etag = AssertETag(grant, "\"v2\"");
        using var updated = await Api.SendJsonAsync(
            HttpMethod.Patch, path, new { visibility = "PRIVATE" }, owner.AccessToken, etag);
        var result = await ReadMountAsync(updated);
        AssertETag(updated, "\"v3\"");
        Assert.That(result.GetProperty("name").GetString(), Is.EqualTo(mount.Name));
        Assert.That(result.GetProperty("visibility").GetString(), Is.EqualTo("PRIVATE"));

        using var stillVisible = await Api.GetAsync(path, allowed.AccessToken);
        await ReadMountAsync(stillVisible);
        using var hidden = await Api.GetAsync(path, member.AccessToken);
        await IntegrationAssert.AssertErrorAsync(hidden, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        Assert.That((await ReadOverwriteTargetsAsync(mount.Id)).Users, Is.EqualTo(new[] { allowed.Id }));
    }

    [TestCase(null, 428, "PRECONDITION_REQUIRED")]
    [TestCase("*", 400, "REQUEST_VALIDATION_FAILED")]
    [TestCase("\"v99\"", 412, "PRECONDITION_FAILED")]
    public async Task UpdateMount_ShouldRejectInvalidPrecondition(string? ifMatch, int status, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space);
        using var response = await Api.SendJsonAsync(HttpMethod.Patch, MountPath(space.PublicId, mount.PublicId),
            new { visibility = "PRIVATE" }, owner.AccessToken, ifMatch);

        if (ifMatch == "*")
        {
            await AssertValidationAsync(response, "ifMatch", "INVALID_ETAG");
        }
        else
        {
            await IntegrationAssert.AssertErrorAsync(response, (HttpStatusCode)status, code);
        }

        var stored = (await ReadStoredMountsAsync(space.Id)).Single();
        Assert.That((stored.Version, stored.Visibility), Is.EqualTo((1L, MountVisibility.Public)));
    }

    [TestCase("""{"name":null}""", "name", "MOUNT_NAME_INVALID")]
    [TestCase("""{"visibility":null}""", "visibility", "MOUNT_VISIBILITY_INVALID")]
    [TestCase("{}", "", "MOUNT_SETTINGS_REQUIRED")]
    public async Task UpdateMount_ShouldValidatePatchFields(string body, string field, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space);
        using var response = await Api.SendRawAsync(HttpMethod.Patch, MountPath(space.PublicId, mount.PublicId),
            body, "application/json", owner.AccessToken, "\"v1\"");

        await AssertValidationAsync(response, field, code);
        Assert.That((await ReadStoredMountsAsync(space.Id)).Single().Version, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangeMountState_ShouldPersistTransitionsAndUpdateExercisableAccess()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space);
        var path = MountPath(space.PublicId, mount.PublicId);

        using var archive = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/archive", owner, "\"v1\"");
        var archived = await ReadMountAsync(archive);
        var archivedETag = AssertETag(archive, "\"v2\"");
        var archivedAt = archived.GetProperty("archivedAt").GetDateTimeOffset();
        IntegrationAssert.AssertUtc(archivedAt, "archivedAt");
        Assert.That(archived.GetProperty("status").GetString(), Is.EqualTo("ARCHIVED"));
        var archivedAccess = archived.GetProperty("myAccess");
        Assert.That(ReadCodes(archivedAccess, "permissions"), Does.Not.Contain("FILES_WRITE"));
        Assert.That(archivedAccess.GetProperty("canManage").GetBoolean(), Is.True);
        Assert.That((await ReadStoredMountsAsync(space.Id)).Single().Status, Is.EqualTo(MountStatus.Archived));

        using var reactivate = await SendSpaceActionAsync(HttpMethod.Post, $"{path}/reactivate", owner, archivedETag);
        var active = await ReadMountAsync(reactivate);
        AssertETag(reactivate, "\"v3\"");
        Assert.That(active.GetProperty("status").GetString(), Is.EqualTo("ACTIVE"));
        Assert.That(active.GetProperty("archivedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(ReadCodes(active.GetProperty("myAccess"), "permissions"), Does.Contain("FILES_WRITE"));
        var stored = (await ReadStoredMountsAsync(space.Id)).Single();
        Assert.That((stored.Status, stored.Version, stored.ArchivedAt), Is.EqualTo((MountStatus.Active, 3L, (DateTimeOffset?)null)));
    }

    [TestCase("archive", MountStatus.Archived)]
    [TestCase("reactivate", MountStatus.Active)]
    public async Task ChangeMountState_ShouldRejectWrongStartingStatus(string action, MountStatus startingStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var mount = await SeedMountAsync(space, status: startingStatus);

        using var response = await SendSpaceActionAsync(
            HttpMethod.Post, $"{MountPath(space.PublicId, mount.PublicId)}/{action}", owner, "\"v1\"");
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Conflict, "REQUEST_CONFLICT");
        Assert.That((await ReadStoredMountsAsync(space.Id)).Single().Version, Is.EqualTo(1));
    }

    [TestCase(MountVisibility.Public, HttpStatusCode.Forbidden, "PERMISSION_DENIED")]
    [TestCase(MountVisibility.Private, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND")]
    public async Task UpdateMount_ShouldCheckAccessBeforeReadingETag(
        MountVisibility visibility, HttpStatusCode status, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);
        var mount = await SeedMountAsync(space, visibility);

        using var response = await Api.SendJsonAsync(HttpMethod.Patch, MountPath(space.PublicId, mount.PublicId),
            new { name = "Renamed" }, member.AccessToken);
        await IntegrationAssert.AssertErrorAsync(response, status, code);
    }
}
