using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InviteListEndpointTests : InviteIntegrationTestBase
{
    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ListInvites_ShouldReturnStoredActiveInvitesInDefaultOrder(SpaceStatus spaceStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: spaceStatus);
        var otherSpace = await SeedSpaceAsync(owner);
        var expiredByTime = await SeedInviteAsync(
            space,
            owner,
            expiresAt: Clock.UtcNow.AddDays(-1),
            createdAt: Clock.UtcNow.AddDays(-2));
        var available = await SeedInviteAsync(
            space, owner, maxUses: 3, useCount: 1, createdAt: space.CreatedAt);
        await SeedInviteAsync(space, owner, SpaceInviteStatus.Revoked);
        await SeedInviteAsync(space, owner, SpaceInviteStatus.Expired, maxUses: 1, useCount: 1);
        await SeedInviteAsync(otherSpace, owner);

        using var response = await Api.GetAsync(InviteListPath(space.PublicId), owner.AccessToken);
        var page = await ReadInvitePageAsync(response);

        Assert.That(page.Items, Has.Length.EqualTo(2));
        AssertInviteManagementItem(page.Items[0], available.Row);
        AssertInviteManagementItem(page.Items[1], expiredByTime.Row);
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListInvites_ShouldReturnAnEmptyPageWhenThereAreNoStoredActiveInvites()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        await SeedInviteAsync(space, owner, SpaceInviteStatus.Revoked);

        using var response = await Api.GetAsync(InviteListPath(space.PublicId), owner.AccessToken);
        var page = await ReadInvitePageAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Is.Empty);
            Assert.That(page.NextCursor, Is.Null);
        });
    }

    [TestCase("", 50)]
    [TestCase("?limit=100", 100)]
    public async Task ListInvites_ShouldApplyDefaultAndMaximumPageLimits(string query, int expectedLimit)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var now = space.CreatedAt;

        await using (var scope = Environment.Factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokenIssuer = scope.ServiceProvider.GetRequiredService<ITokenIssuer>();
            context.SpaceInvites.AddRange(Enumerable.Range(0, 101).Select(_ => new SpaceInviteEntity
            {
                PublicId = Guid.CreateVersion7(),
                SpaceId = space.Id,
                CreatedByUserId = owner.Id,
                TokenHash = tokenIssuer.Issue(TokenKind.SpaceInvite).HashedToken,
                Status = SpaceInviteStatus.Active,
                Version = 1,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = now.AddDays(1),
            }));
            await context.SaveChangesAsync();
        }

        using var response = await Api.GetAsync(InviteListPath(space.PublicId) + query, owner.AccessToken);
        var page = await ReadInvitePageAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Length.EqualTo(expectedLimit));
            Assert.That(page.Items.Select(item => item.GetProperty("invitePublicId").GetGuid()), Is.Unique);
            Assert.That(page.NextCursor, Does.Match("^[A-Za-z0-9_-]+$"));
        });
    }

    [TestCase("ASC")]
    [TestCase("DESC")]
    public async Task ListInvites_ShouldPaginateByCreatedAtAndBreakTiesByPublicId(string direction)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var older = space.CreatedAt.AddMinutes(-1);
        var greaterId = await SeedInviteAsync(
            space, owner, createdAt: older, publicId: Guid.Parse("018f2f6e-8a9a-7c3a-9d7a-3f4b5a6b7c8c"));
        var newer = await SeedInviteAsync(
            space, owner, createdAt: space.CreatedAt, publicId: Guid.Parse("018f2f6e-8a9a-7c3a-9d7a-3f4b5a6b7c8a"));
        var smallerId = await SeedInviteAsync(
            space, owner, createdAt: older, publicId: Guid.Parse("018f2f6e-8a9a-7c3a-9d7a-3f4b5a6b7c8b"));
        var expected = direction == "ASC"
            ? new[] { smallerId.Row, greaterId.Row, newer.Row }
            : new[] { newer.Row, greaterId.Row, smallerId.Row };
        var path = $"{InviteListPath(space.PublicId)}?limit=1&sort=CrEaTeDaT&direction={direction}";

        string? cursor = null;
        for (var index = 0; index < expected.Length; index++)
        {
            var cursorQuery = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await Api.GetAsync(path + cursorQuery, owner.AccessToken);
            var page = await ReadInvitePageAsync(response);

            Assert.That(page.Items, Has.Length.EqualTo(1));
            AssertInviteManagementItem(page.Items[0], expected[index]);
            cursor = page.NextCursor;
            if (index == expected.Length - 1)
            {
                Assert.That(cursor, Is.Null);
            }
            else
            {
                Assert.That(cursor, Does.Match("^[A-Za-z0-9_-]+$"));
            }
        }
    }

    [Test]
    public async Task ListInvites_ShouldRejectTamperedAndOutOfScopeCursors()
    {
        var owner = await RegisterSpaceUserAsync();
        var admin = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var otherSpace = await SeedSpaceAsync(owner);
        await SeedMembershipAsync(space, admin, owner, roleKeys: [SpaceRoleSystemKey.Admin]);
        await SeedInviteAsync(space, owner);
        await SeedInviteAsync(space, owner);

        var path = InviteListPath(space.PublicId);
        using var first = await Api.GetAsync($"{path}?limit=1", owner.AccessToken);
        var page = await ReadInvitePageAsync(first);
        Assert.That(page.NextCursor, Does.Match("^[A-Za-z0-9_-]+$"));
        var cursor = page.NextCursor!;
        var tamperIndex = cursor.Length / 2;
        var replacement = cursor[tamperIndex] == 'A' ? 'B' : 'A';
        var tamperedCursor = $"{cursor[..tamperIndex]}{replacement}{cursor[(tamperIndex + 1)..]}";
        var query = $"?limit=1&cursor={Uri.EscapeDataString(cursor)}";

        using var adminFirst = await Api.GetAsync($"{path}?limit=1", admin.AccessToken);
        await ReadInvitePageAsync(adminFirst);
        await AssertCursorRejectedAsync($"{path}?cursor={tamperedCursor}", owner);
        await AssertCursorRejectedAsync(path + query, admin);
        await AssertCursorRejectedAsync(InviteListPath(otherSpace.PublicId) + query, owner);
        await AssertCursorRejectedAsync($"{path}{query}&direction=ASC", owner);
    }

    [TestCase("cursor=", "CURSOR_INVALID", null)]
    [TestCase("limit=0", "REQUEST_VALIDATION_FAILED", "limit")]
    [TestCase("limit=101", "REQUEST_VALIDATION_FAILED", "limit")]
    [TestCase("limit=one", "REQUEST_VALIDATION_FAILED", null)]
    [TestCase("sort=expiresAt", "REQUEST_VALIDATION_FAILED", "sort")]
    [TestCase("direction=sideways", "REQUEST_VALIDATION_FAILED", "direction")]
    public async Task ListInvites_ShouldRejectInvalidQueries(string query, string code, string? field)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);

        using var response = await Api.GetAsync($"{InviteListPath(space.PublicId)}?{query}", owner.AccessToken);

        var error = await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
        if (field is not null)
        {
            Assert.That(error.Error.Details.Any(detail => detail.Field == field && !string.IsNullOrEmpty(detail.Code)), Is.True,
                $"Validation must identify {field} with a field error code.");
        }
    }

    [Test]
    public async Task ListInvites_ShouldRequireAUserSessionAndInviteManagementPermission()
    {
        await AssertInviteManagementAuthorizationAsync((actor, space, _) =>
            Api.GetAsync(InviteListPath(space.PublicId), actor?.AccessToken));
    }

    private async Task AssertCursorRejectedAsync(string path, SpaceTestUser actor)
    {
        using var response = await Api.GetAsync(path, actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(response, HttpStatusCode.BadRequest, "CURSOR_INVALID");
    }

    private static async Task<(JsonElement[] Items, string? NextCursor)> ReadInvitePageAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        var page = document.RootElement;
        IntegrationAssert.AssertPropertyNames(page, "items", "nextCursor");
        var items = page.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToArray();
        return (items, page.GetProperty("nextCursor").GetString());
    }
}
