using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class RoleReadEndpointTests : MembershipIntegrationTestBase
{
    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ListSpaceRoles_ShouldReturnActiveManagedRolesWithEffectivePermissions(
        SpaceStatus status)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: status);
        await SeedSpaceAsync(owner, "Other space");
        var expected = await PrepareRoleCatalogAsync(space);

        using var response = await Api.GetAsync(RolesPath(space.PublicId), owner.AccessToken);
        var roles = await ReadRolesAsync(response);

        Assert.That(roles.Select(role => role.Code), Is.EquivalentTo(new[] { "ADMIN", "MEMBER", "VIEWER" }));
        var byCode = roles.ToDictionary(role => role.Code, StringComparer.Ordinal);
        AssertRole(
            byCode["ADMIN"],
            expected[SpaceRoleSystemKey.Admin],
            "SPACE_VIEW", "SPACE_UPDATE", "FILES_READ", "FILES_WRITE", "FILES_DOWNLOAD",
            "SHARES_VIEW", "SHARES_MANAGE", "TAGS_MANAGE", "INVITES_MANAGE", "MEMBERS_VIEW",
            "MEMBERS_MANAGE", "UPLOADS_MANAGE", "OPERATIONS_MANAGE");
        AssertRole(
            byCode["MEMBER"],
            expected[SpaceRoleSystemKey.Member],
            "SPACE_VIEW", "FILES_READ", "FILES_DOWNLOAD");
        AssertRole(byCode["VIEWER"], expected[SpaceRoleSystemKey.Viewer]);
    }

    [Test]
    public async Task ListSpaceRoles_WithMembersViewPermission_ShouldReturnRoles()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, member, owner);

        using var response = await Api.GetAsync(RolesPath(space.PublicId), member.AccessToken);
        var roles = await ReadRolesAsync(response);

        Assert.That(roles.Select(role => role.Code), Is.EquivalentTo(new[] { "ADMIN", "MEMBER", "VIEWER" }));
    }

    [Test]
    public async Task ListSpaceRoles_WithoutActiveManagedRoles_ShouldReturnEmptyArray()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await context.SpaceRoles.Where(role => role.SpaceId == space.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(role => role.DeletedAt, space.UpdatedAt));
        }

        using var response = await Api.GetAsync(RolesPath(space.PublicId), owner.AccessToken);
        var roles = await ReadRolesAsync(response);

        Assert.That(roles, Is.Empty);
    }

    private async Task<Dictionary<SpaceRoleSystemKey, SpaceRoleEntity>> PrepareRoleCatalogAsync(
        SpaceEntity space)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await context.SpaceRoles.Where(role => role.SpaceId == space.Id)
            .ToDictionaryAsync(role => role.SystemKey!.Value);
        var admin = roles[SpaceRoleSystemKey.Admin];
        var member = roles[SpaceRoleSystemKey.Member];
        var viewer = roles[SpaceRoleSystemKey.Viewer];
        // Make the stored masks differ from the built-in role templates.
        admin.PermissionMask = 0;
        member.PermissionMask = viewer.PermissionMask;
        member.Name = "Workspace Member";
        member.NormalizedName = "WORKSPACE MEMBER";
        viewer.PermissionMask = 0;

        context.SpaceRoles.AddRange(
            new SpaceRoleEntity
            {
                PublicId = Guid.CreateVersion7(),
                SpaceId = space.Id,
                Name = "Custom role",
                NormalizedName = "CUSTOM ROLE",
                Version = 1,
                CreatedAt = space.CreatedAt,
                UpdatedAt = space.UpdatedAt,
            },
            new SpaceRoleEntity
            {
                PublicId = Guid.CreateVersion7(),
                SpaceId = space.Id,
                Name = "Retired Viewer",
                NormalizedName = "RETIRED VIEWER",
                SystemKey = SpaceRoleSystemKey.Viewer,
                IsManaged = true,
                Version = 1,
                CreatedAt = space.CreatedAt,
                UpdatedAt = space.UpdatedAt,
                DeletedAt = space.UpdatedAt,
            });
        await context.SaveChangesAsync();
        return roles;
    }

    private static void AssertRole(
        RoleWireResponse actual,
        SpaceRoleEntity expected,
        params string[] permissions)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.RolePublicId, Is.EqualTo(expected.PublicId));
            Assert.That(actual.Name, Is.EqualTo(expected.Name));
            Assert.That(actual.IsManaged, Is.True);
            Assert.That(actual.Permissions, Is.EquivalentTo(permissions));
        });
    }
}
