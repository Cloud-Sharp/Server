using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class SpaceCreationEndpointTests : SpaceIntegrationTestBase
{
    [Test]
    public async Task CreateSpace_ShouldPersistOwnerAndManagedRoles()
    {
        var owner = await RegisterSpaceUserAsync();
        const string body = """
            {"name":"  Product  ","description":"  Product files  ","allowedBytes":1024}
            """;

        using var response = await CreateSpaceAsync(Api, owner, body);
        var created = await ReadSpaceAsync(response, HttpStatusCode.Created);
        AssertETag(response, "\"v1\"");
        Assert.That(
            TestApiClient.GetHeader(response, "Location"),
            Is.EqualTo(SpacePath(created.SpacePublicId)));

        var persisted = await FindSpaceAsync(created.SpacePublicId);
        Assert.That(persisted, Is.Not.Null);
        var roles = await GetSpaceRolesAsync(persisted!.Id);
        var memberships = await GetSpaceMembershipsAsync(persisted.Id);
        Assert.Multiple(() =>
        {
            Assert.That(created.OwnerUserPublicId, Is.EqualTo(owner.PublicId));
            Assert.That(created.Name, Is.EqualTo("Product"));
            Assert.That(created.Description, Is.EqualTo("Product files"));
            Assert.That(created.Status, Is.EqualTo("ACTIVE"));
            Assert.That(created.MyAccess.Source, Is.EqualTo("OWNER"));
            Assert.That(created.MyAccess.Permissions, Does.Contain("SPACE_VIEW"));
            Assert.That(created.MyAccess.Permissions, Does.Contain("SPACE_UPDATE"));
            Assert.That(persisted.OwnerUserId, Is.EqualTo(owner.Id));
            Assert.That(persisted.Name, Is.EqualTo("Product"));
            Assert.That(persisted.Description, Is.EqualTo("Product files"));
            Assert.That(persisted.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(persisted.AllowedBytes, Is.EqualTo(1024));
            Assert.That(persisted.UsedBytes, Is.Zero);
            Assert.That(persisted.ReservedBytes, Is.Zero);
            Assert.That(persisted.Version, Is.EqualTo(1));
            Assert.That(roles, Has.Count.EqualTo(3));
            Assert.That(roles.Select(role => role.SystemKey), Is.EquivalentTo(
                new[]
                {
                    SpaceRoleSystemKey.Admin,
                    SpaceRoleSystemKey.Member,
                    SpaceRoleSystemKey.Viewer,
                }));
            Assert.That(roles.Select(role => role.IsManaged), Is.All.True);
            Assert.That(memberships, Is.Empty, "The OWNER must not have a Membership row.");
        });
    }

    [TestCase("""
        {"name":"Unlimited"}
        """)]
    [TestCase("""
        {"name":"Unlimited","description":null,"allowedBytes":null}
        """)]
    public async Task CreateSpace_ShouldSupportOmittedAndNullOptionalFields(string body)
    {
        var owner = await RegisterSpaceUserAsync();

        using var response = await CreateSpaceAsync(Api, owner, body);
        var created = await ReadSpaceAsync(response, HttpStatusCode.Created);

        var persisted = await FindSpaceAsync(created.SpacePublicId);
        Assert.That(persisted, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(created.Description, Is.Null);
            Assert.That(persisted!.Description, Is.Null);
            Assert.That(persisted.AllowedBytes, Is.Null);
        });
    }

    [TestCase("""
        {"description":"Missing name"}
        """)]
    [TestCase("""
        {"name":null}
        """)]
    [TestCase("""
        {"name":"   "}
        """)]
    [TestCase("""
        {"name":"Product","allowedBytes":-1}
        """)]
    public async Task CreateSpace_ShouldRejectInvalidInput(string body)
    {
        var owner = await RegisterSpaceUserAsync();

        using var response = await CreateSpaceAsync(Api, owner, body);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetSpacesAsync(), Is.Empty);
    }

    [Test]
    public async Task CreateSpace_ShouldRejectMalformedJson()
    {
        var owner = await RegisterSpaceUserAsync();

        using var response = await CreateSpaceAsync(Api, owner, """{"name":""");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetSpacesAsync(), Is.Empty);
    }

    [Test]
    public async Task CreateSpace_ShouldRejectUnknownField()
    {
        var owner = await RegisterSpaceUserAsync();

        using var response = await CreateSpaceAsync(
            Api,
            owner,
            """{"name":"Product","unexpected":true}""");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetSpacesAsync(), Is.Empty);
    }

    [Test]
    public async Task CreateSpace_ShouldRejectUnsupportedMediaType()
    {
        var owner = await RegisterSpaceUserAsync();

        using var response = await CreateSpaceAsync(
            Api,
            owner,
            """{"name":"Product"}""",
            "text/plain");
        await IntegrationAssert.AssertErrorAsync(
            response,
            (HttpStatusCode)415,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(await GetSpacesAsync(), Is.Empty);
    }
}
