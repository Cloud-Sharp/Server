using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class MembershipAuthorizationEndpointTests : MembershipIntegrationTestBase
{
    private static readonly string[] InvalidPublicIds =
    [
        "not-a-uuid",
        "018f2f6e-8a9a-4c3a-9d7a-3f4b5a6b7c8d",
        "018f2f6e-8a9a-7c3a-1d7a-3f4b5a6b7c8d",
    ];

    public enum Endpoint
    {
        ListMembers,
        GetMember,
        ReplaceRoles,
        SuspendMember,
        ActivateMember,
        RemoveMember,
        Leave,
        ListRoles,
    }

    private static IEnumerable<Endpoint> PermissionEndpoints =>
        Enum.GetValues<Endpoint>().Where(endpoint => endpoint != Endpoint.Leave);

    private static IEnumerable<Endpoint> MemberEndpoints =>
        PermissionEndpoints.Where(endpoint => endpoint is not (Endpoint.ListMembers or Endpoint.ListRoles));

    [Test]
    public async Task Request_WithoutValidSession_ShouldReturn401([Values] Endpoint endpoint)
    {
        var spaceId = Guid.CreateVersion7().ToString("D");
        var membershipId = Guid.CreateVersion7().ToString("D");

        foreach (var (token, code) in new (string?, string)[]
                 {
                     (null, ErrorCodes.Auth.TokenRequired),
                     ("cs_sess_forged", ErrorCodes.Auth.TokenInvalid),
                     ("cs_mcp_forged", ErrorCodes.Auth.TokenTypeNotAllowed),
                 })
        {
            using var response = await SendRequestAsync(endpoint, spaceId, membershipId, token);
            await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Unauthorized, code);
        }
    }

    [Test]
    public async Task Request_WithInvalidSpacePublicId_ShouldReturn400([Values] Endpoint endpoint)
    {
        var owner = await RegisterSpaceUserAsync();
        var membershipId = Guid.CreateVersion7().ToString("D");

        foreach (var invalidId in InvalidPublicIds)
        {
            using var response = await SendRequestAsync(endpoint, invalidId, membershipId, owner.AccessToken);
            await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        }
    }

    [TestCaseSource(nameof(MemberEndpoints))]
    public async Task Request_WithInvalidMembershipPublicId_ShouldReturn400(Endpoint endpoint)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        foreach (var invalidId in InvalidPublicIds)
        {
            using var response = await SendRequestAsync(endpoint, space.PublicId.ToString("D"), invalidId, owner.AccessToken);
            await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        }

        using var missingSpace = await SendRequestAsync(
            endpoint, Guid.CreateVersion7().ToString("D"), InvalidPublicIds[0], owner.AccessToken);
        await IntegrationAssert.AssertErrorAsync(missingSpace, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
    }

    [TestCaseSource(nameof(PermissionEndpoints))]
    public async Task Request_WithBaseAccessButWithoutOperationPermission_ShouldReturn403(Endpoint endpoint)
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var actorRole = endpoint is Endpoint.ListMembers or Endpoint.GetMember or Endpoint.ListRoles
            ? SpaceRoleSystemKey.Viewer
            : SpaceRoleSystemKey.Member;
        await SeedMembershipAsync(space, actor, owner, SpaceMembershipStatus.Active, actorRole);
        var member = await SeedMembershipAsync(space, target, owner, TargetStatus(endpoint));
        var roles = await GetSpaceRolesAsync(space.Id);
        var viewer = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);

        using var response = await SendRequestAsync(
            endpoint, space.PublicId.ToString("D"), member.PublicId.ToString("D"), actor.AccessToken, viewer.PublicId);

        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
    }

    [TestCaseSource(nameof(PermissionEndpoints))]
    public async Task Request_WithoutAVisibleSpace_ShouldReturnSame404(Endpoint endpoint)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var outsider = await RegisterSpaceUserAsync();
        var administrator = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var active = await SeedSpaceAsync(owner);
        var deleting = await SeedSpaceAsync(owner, status: SpaceStatus.Deleting);
        var deleted = await SeedSpaceAsync(owner, status: SpaceStatus.Deleted);
        var member = await SeedMembershipAsync(active, target, owner, TargetStatus(endpoint));
        var deletingMember = await SeedMembershipAsync(deleting, target, owner, TargetStatus(endpoint));
        var deletedMember = await SeedMembershipAsync(deleted, target, owner, TargetStatus(endpoint));
        var viewer = (await GetSpaceRolesAsync(active.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var deletingViewer = (await GetSpaceRolesAsync(deleting.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var deletedViewer = (await GetSpaceRolesAsync(deleted.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var missingSpaceId = Guid.CreateVersion7();

        var errors = new List<ErrorEnvelope>();
        foreach (var (spaceId, membershipId, roleId, caller) in new[]
                 {
                     (missingSpaceId, member.PublicId, viewer.PublicId, owner),
                     (active.PublicId, member.PublicId, viewer.PublicId, outsider),
                     (deleting.PublicId, deletingMember.PublicId, deletingViewer.PublicId, owner),
                     (deleted.PublicId, deletedMember.PublicId, deletedViewer.PublicId, owner),
                     (missingSpaceId, member.PublicId, viewer.PublicId, administrator),
                     (active.PublicId, member.PublicId, viewer.PublicId, administrator),
                     (deleting.PublicId, deletingMember.PublicId, deletingViewer.PublicId, administrator),
                     (deleted.PublicId, deletedMember.PublicId, deletedViewer.PublicId, administrator),
                 })
        {
            using var response = await SendRequestAsync(
                endpoint, spaceId.ToString("D"), membershipId.ToString("D"), caller.AccessToken, roleId);
            errors.Add(await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
    }

    [TestCaseSource(nameof(PermissionEndpoints))]
    public async Task Request_AsSystemAdminWithoutOperationPermission_ShouldReturn403(Endpoint endpoint)
    {
        var owner = await RegisterSpaceUserAsync();
        var administrator = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var space = await SeedSpaceAsync(owner);
        var actorRole = endpoint is Endpoint.ListMembers or Endpoint.GetMember or Endpoint.ListRoles
            ? SpaceRoleSystemKey.Viewer
            : SpaceRoleSystemKey.Member;
        await SeedMembershipAsync(space, administrator, owner, SpaceMembershipStatus.Active, actorRole);
        var member = await SeedMembershipAsync(space, target, owner, TargetStatus(endpoint));
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);

        using var response = await SendRequestAsync(
            endpoint, space.PublicId.ToString("D"), member.PublicId.ToString("D"), administrator.AccessToken, viewer.PublicId);

        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.Forbidden, "PERMISSION_DENIED");
    }

    private static SpaceMembershipStatus TargetStatus(Endpoint endpoint) => endpoint == Endpoint.ActivateMember
        ? SpaceMembershipStatus.Suspended
        : SpaceMembershipStatus.Active;

    private Task<HttpResponseMessage> SendRequestAsync(
        Endpoint endpoint,
        string spaceId,
        string membershipId,
        string? token,
        Guid? roleId = null)
    {
        var spacePath = $"{SpacesPath}/{spaceId}";
        var memberPath = $"{spacePath}/memberships/{membershipId}";
        return endpoint switch
        {
            Endpoint.ListMembers => Api.GetAsync($"{spacePath}/memberships", token),
            Endpoint.GetMember => Api.GetAsync(memberPath, token),
            Endpoint.ReplaceRoles => Api.SendJsonAsync(
                HttpMethod.Put, $"{memberPath}/roles", new { rolePublicIds = new[] { roleId ?? Guid.CreateVersion7() } }, token, "\"v1\""),
            Endpoint.SuspendMember => SendMutationAsync(HttpMethod.Post, $"{memberPath}/suspend", token),
            Endpoint.ActivateMember => SendMutationAsync(HttpMethod.Post, $"{memberPath}/activate", token),
            Endpoint.RemoveMember => SendMutationAsync(HttpMethod.Delete, memberPath, token),
            Endpoint.Leave => Api.SendAsync(HttpMethod.Delete, $"{spacePath}/membership", token),
            Endpoint.ListRoles => Api.GetAsync($"{spacePath}/roles", token),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint)),
        };
    }

    private Task<HttpResponseMessage> SendMutationAsync(HttpMethod method, string path, string? token) =>
        Api.SendAsync(method, path, token, configure: request =>
            request.Headers.TryAddWithoutValidation(TestApiClient.IfMatchHeaderName, "\"v1\""));
}
