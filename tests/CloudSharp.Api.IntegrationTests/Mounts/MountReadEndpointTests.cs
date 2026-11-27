using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Mounts;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Mounts;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MountReadEndpointTests : MountIntegrationTestBase
{
    [Test]
    public async Task ListMounts_ShouldPageVisibleMountsAndBindCursorToActorSpaceAndSort()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);
        await SeedMembershipAsync(otherSpace, member, owner);
        var oldest = await SeedMountAsync(space, status: MountStatus.Archived, createdAt: Clock.UtcNow.AddDays(-3));
        await SeedMountAsync(space, MountVisibility.Private, createdAt: Clock.UtcNow.AddDays(-2));
        var newest = await SeedMountAsync(space, createdAt: Clock.UtcNow.AddDays(-1));
        var path = MountsPath(space.PublicId);

        using var firstResponse = await Api.GetAsync($"{path}?limit=1", member.AccessToken);
        var first = await ReadJsonAsync(firstResponse);
        IntegrationAssert.AssertPropertyNames(first, "items", "nextCursor", "canCreateMount");
        Assert.That(first.GetProperty("canCreateMount").GetBoolean(), Is.False);
        var item = first.GetProperty("items").EnumerateArray().Single();
        IntegrationAssert.AssertPropertyNames(item, "mountPublicId", "name", "visibility", "status",
            "createdAt", "updatedAt", "myAccess");
        Assert.That(item.GetProperty("mountPublicId").GetGuid(), Is.EqualTo(newest.PublicId));
        var cursor = first.GetProperty("nextCursor").GetString();
        Assert.That(cursor, Is.Not.Null.And.Not.Empty);

        using var secondResponse = await Api.GetAsync($"{path}?limit=100&cursor={cursor}", member.AccessToken);
        var second = await ReadJsonAsync(secondResponse);
        var finalItem = second.GetProperty("items").EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(finalItem.GetProperty("mountPublicId").GetGuid(), Is.EqualTo(oldest.PublicId));
            Assert.That(finalItem.GetProperty("status").GetString(), Is.EqualTo("ARCHIVED"));
            Assert.That(second.GetProperty("nextCursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });

        using var otherActor = await Api.GetAsync($"{path}?cursor={cursor}", owner.AccessToken);
        await IntegrationAssert.AssertErrorAsync(otherActor, HttpStatusCode.BadRequest, "CURSOR_INVALID");
        using var otherSort = await Api.GetAsync($"{path}?cursor={cursor}&direction=ASC", member.AccessToken);
        await IntegrationAssert.AssertErrorAsync(otherSort, HttpStatusCode.BadRequest, "CURSOR_INVALID");
        using var otherScope = await Api.GetAsync(
            $"{MountsPath(otherSpace.PublicId)}?cursor={cursor}", member.AccessToken);
        await IntegrationAssert.AssertErrorAsync(otherScope, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    [Test]
    public async Task ListMounts_ShouldReportCreationCapabilityWithNoMounts()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        using var response = await Api.GetAsync(MountsPath(space.PublicId), owner.AccessToken);
        var page = await ReadJsonAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(page.GetProperty("items").GetArrayLength(), Is.Zero);
            Assert.That(page.GetProperty("nextCursor").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(page.GetProperty("canCreateMount").GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task GetMount_ShouldMaskHiddenMissingAndCrossSpaceMounts()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);
        await SeedMembershipAsync(otherSpace, member, owner);
        var hidden = await SeedMountAsync(space, MountVisibility.Private);
        var publicMount = await SeedMountAsync(space);

        using var hiddenResponse = await Api.GetAsync(MountPath(space.PublicId, hidden.PublicId), member.AccessToken);
        var hiddenError = await IntegrationAssert.AssertErrorAsync(
            hiddenResponse, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        using var missing = await Api.GetAsync(MountPath(space.PublicId, Guid.CreateVersion7()), member.AccessToken);
        var missingError = await IntegrationAssert.AssertErrorAsync(
            missing, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        using var crossSpace = await Api.GetAsync(MountPath(otherSpace.PublicId, publicMount.PublicId), member.AccessToken);
        var crossSpaceError = await IntegrationAssert.AssertErrorAsync(
            crossSpace, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertSamePublicError(hiddenError, missingError, crossSpaceError);
    }

    [TestCase("spacePublicId", "SPACE_PUBLIC_ID_INVALID")]
    [TestCase("mountPublicId", "MOUNT_PUBLIC_ID_INVALID")]
    public async Task GetMount_ShouldRejectNonVersion7PublicIds(string field, string code)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invalidId = Guid.NewGuid();
        var mount = await SeedMountAsync(space);
        var path = field == "spacePublicId"
            ? MountPath(invalidId, mount.PublicId)
            : MountPath(space.PublicId, invalidId);

        using var response = await Api.GetAsync(path, owner.AccessToken);
        await AssertValidationAsync(response, field, code);
    }

    [TestCase("GET", "")]
    [TestCase("POST", "")]
    [TestCase("GET", "/{mount}")]
    [TestCase("PATCH", "/{mount}")]
    [TestCase("POST", "/{mount}/archive")]
    [TestCase("POST", "/{mount}/reactivate")]
    [TestCase("GET", "/{mount}/permissions?targetType=ROLE")]
    [TestCase("PUT", "/{mount}/permissions/roles/{target}")]
    [TestCase("PUT", "/{mount}/permissions/users/{target}")]
    [TestCase("GET", "/{mount}/permissions/preview?visibility=PRIVATE")]
    public async Task MountEndpoints_ShouldRequireSession(string method, string suffix)
    {
        var path = MountsPath(Guid.CreateVersion7()) + suffix
            .Replace("{mount}", Guid.CreateVersion7().ToString("D"), StringComparison.Ordinal)
            .Replace("{target}", Guid.CreateVersion7().ToString("D"), StringComparison.Ordinal);
        using var response = await Api.SendAsync(new HttpMethod(method), path);
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Unauthorized, "AUTH_TOKEN_REQUIRED");
    }

    [TestCase("limit=0", "limit")]
    [TestCase("limit=101", "limit")]
    [TestCase("sort=NAME", "sort")]
    [TestCase("direction=UP", "direction")]
    public async Task ListMounts_ShouldValidatePageParameters(string query, string field)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await Api.GetAsync($"{MountsPath(space.PublicId)}?{query}", owner.AccessToken);
        await AssertValidationAsync(response, field, "INVALID");
    }
}
