using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

public abstract class MembershipIntegrationTestBase : SpaceIntegrationTestBase
{
    internal static string MembershipsPath(Guid spaceId) => $"{SpacePath(spaceId)}/memberships";

    internal static string MembershipPath(Guid spaceId, Guid membershipId) =>
        $"{MembershipsPath(spaceId)}/{membershipId:D}";

    internal static string LeavePath(Guid spaceId) => $"{SpacePath(spaceId)}/membership";

    internal static string RolesPath(Guid spaceId) => $"{SpacePath(spaceId)}/roles";

    internal static async Task<MembershipWireResponse> ReadMembershipAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        AssertMembershipProperties(document.RootElement);
        return Deserialize<MembershipWireResponse>(body);
    }

    internal static async Task<MembershipPageWireResponse> ReadMembershipPageAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        var page = document.RootElement;
        IntegrationAssert.AssertPropertyNames(page, "items", "nextCursor");
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            AssertMembershipProperties(item);
        }

        return Deserialize<MembershipPageWireResponse>(body);
    }

    internal static async Task<IReadOnlyList<RoleWireResponse>> ReadRolesAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        foreach (var role in document.RootElement.EnumerateArray())
        {
            IntegrationAssert.AssertPropertyNames(role, "rolePublicId", "code", "name", "isManaged", "permissions");
            AssertRoleSummary(role);
        }

        return Deserialize<IReadOnlyList<RoleWireResponse>>(body);
    }

    internal static string AssertMembershipEtag(HttpResponseMessage response, long version) =>
        AssertETag(response, FormattableString.Invariant($"\"v{version}\""));

    internal static async Task AssertNoContentAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        IntegrationAssert.AssertNoStore(response);
        Assert.That(await TestApiClient.ReadBodyAsync(response), Is.Empty);
    }

    private static void AssertMembershipProperties(JsonElement item)
    {
        IntegrationAssert.AssertPropertyNames(item, "membershipPublicId", "user", "status", "roles", "joinedAt", "updatedAt");
        AssertPublicId(item, "membershipPublicId");
        Assert.That(item.GetProperty("status").GetString(), Is.AnyOf("ACTIVE", "SUSPENDED", "LEFT"));
        IntegrationAssert.AssertUtc(item.GetProperty("joinedAt").GetDateTimeOffset(), "joinedAt");
        IntegrationAssert.AssertUtc(item.GetProperty("updatedAt").GetDateTimeOffset(), "updatedAt");

        var user = item.GetProperty("user");
        IntegrationAssert.AssertPropertyNames(user, "userPublicId", "userName", "displayName");
        AssertPublicId(user, "userPublicId");
        foreach (var role in item.GetProperty("roles").EnumerateArray())
        {
            IntegrationAssert.AssertPropertyNames(role, "rolePublicId", "code", "name");
            AssertRoleSummary(role);
        }
    }

    private static void AssertRoleSummary(JsonElement role)
    {
        AssertPublicId(role, "rolePublicId");
        Assert.That(role.GetProperty("code").GetString(), Is.AnyOf("ADMIN", "MEMBER", "VIEWER"));
        Assert.That(role.GetProperty("name").GetString(), Is.Not.Null.And.Not.Empty);
    }

    private static void AssertPublicId(JsonElement item, string field)
    {
        var id = item.GetProperty(field).GetGuid();
        IntegrationAssert.AssertUuidV7(id, field);
        Assert.That(id.Variant, Is.InRange(8, 11), $"{field} must use the RFC UUID variant.");
    }
}
