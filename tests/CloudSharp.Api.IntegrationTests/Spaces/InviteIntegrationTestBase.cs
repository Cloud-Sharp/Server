using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

public abstract class InviteIntegrationTestBase : SpaceIntegrationTestBase
{
    internal static string InviteListPath(Guid spacePublicId) => $"{SpacePath(spacePublicId)}/invites";

    internal static string InviteDetailPath(Guid spacePublicId, Guid invitePublicId) =>
        $"{InviteListPath(spacePublicId)}/{invitePublicId:D}";

    internal static string InvitePublicPath(string token) => $"/public/v2/invites/{token}";

    internal static string InviteAcceptancePath(string token) => $"/api/v2/invites/{token}/accept";

    internal static string NewIdempotencyKey() => Guid.NewGuid().ToString("N");

    internal static Action<HttpRequestMessage> WithIdempotencyKey(string? key) => request =>
    {
        if (key is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        }
    };

    internal async Task<string> ReauthenticateAfterRedisResetAsync(RegistrationWireRequest registration)
    {
        // The shared suite owns this Redis database and resets it between tests.
        // Clear only Redis here so the committed PostgreSQL result can be recovered.
        await Environment.TestRedis.GetDatabase().ExecuteAsync("FLUSHDB");
        var session = await LoginAsync(new LoginWireRequest(registration.Email, registration.Password));
        return session.AccessToken;
    }

    internal async Task<SeededInvite> SeedInviteAsync(
        SpaceEntity space,
        SpaceTestUser creator,
        SpaceInviteStatus status = SpaceInviteStatus.Active,
        DateTimeOffset? expiresAt = null,
        int? maxUses = null,
        int useCount = 0,
        long version = 1,
        DateTimeOffset? createdAt = null,
        Guid? publicId = null)
    {
        var now = ToPersistencePrecision(Clock.UtcNow);
        var created = ToPersistencePrecision(createdAt ?? now.AddDays(-2));
        var issued = IssueInviteToken();
        var invite = new SpaceInviteEntity
        {
            PublicId = publicId ?? Guid.CreateVersion7(),
            SpaceId = space.Id,
            CreatedByUserId = creator.Id,
            TokenHash = issued.HashedToken,
            Status = status,
            UseCount = useCount,
            MaxUses = maxUses,
            ExpiresAt = ToPersistencePrecision(expiresAt ?? now.AddDays(1)),
            Version = version,
            CreatedAt = created,
            UpdatedAt = status == SpaceInviteStatus.Active ? created : now,
            RevokedAt = status == SpaceInviteStatus.Revoked ? now : null,
        };

        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.SpaceInvites.Add(invite);
        await context.SaveChangesAsync();
        return new SeededInvite(invite, issued.PlainToken);
    }

    internal string NewInviteToken() => IssueInviteToken().PlainToken;

    internal async Task<SpaceInviteEntity[]> GetInvitesAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SpaceInvites.AsNoTracking()
            .Where(invite => invite.SpaceId == spaceId)
            .ToArrayAsync();
    }

    internal async Task RemoveViewerRoleAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deleted = await context.SpaceRoles
            .Where(role => role.SpaceId == spaceId && role.SystemKey == SpaceRoleSystemKey.Viewer)
            .ExecuteDeleteAsync();
        Assert.That(deleted, Is.EqualTo(1));
    }

    internal async Task AssertInviteManagementAuthorizationAsync(
        Func<SpaceTestUser?, SpaceEntity, Guid, Task<HttpResponseMessage>> send)
    {
        var owner = await RegisterSpaceUserAsync();
        var viewer = await RegisterSpaceUserAsync();
        var outsider = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner);
        await SeedMembershipAsync(space, viewer, owner, SpaceMembershipStatus.Active, SpaceRoleSystemKey.Viewer);

        using var anonymous = await send(null, space, invite.Row.PublicId);
        await IntegrationAssert.AssertErrorAsync(anonymous, HttpStatusCode.Unauthorized, "AUTH_TOKEN_REQUIRED");

        using var forbidden = await send(viewer, space, invite.Row.PublicId);
        await IntegrationAssert.AssertErrorAsync(forbidden, HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        using var hidden = await send(outsider, space, invite.Row.PublicId);
        var hiddenError = await IntegrationAssert.AssertErrorAsync(hidden, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");

        await ChangeSystemRoleAsync(viewer.PublicId, SystemRole.SystemAdmin);
        using var forbiddenAdministrator = await send(viewer, space, invite.Row.PublicId);
        await IntegrationAssert.AssertErrorAsync(forbiddenAdministrator, HttpStatusCode.Forbidden, "PERMISSION_DENIED");

        await ChangeSystemRoleAsync(outsider.PublicId, SystemRole.SystemAdmin);
        using var hiddenAdministrator = await send(outsider, space, invite.Row.PublicId);
        var hiddenAdministratorError = await IntegrationAssert.AssertErrorAsync(
            hiddenAdministrator, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND");
        AssertSamePublicError(hiddenError, hiddenAdministratorError);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    internal static void AssertCapabilityHeaders(HttpResponseMessage response)
    {
        IntegrationAssert.AssertNoStore(response);
        Assert.That(TestApiClient.GetHeader(response, "Referrer-Policy"), Is.EqualTo("no-referrer"));
    }

    internal static void AssertInviteManagementItem(JsonElement item, SpaceInviteEntity expected)
    {
        IntegrationAssert.AssertPropertyNames(
            item, "invitePublicId", "status", "expiresAt", "maxUses", "useCount", "createdAt", "updatedAt", "revokedAt");
        var publicId = item.GetProperty("invitePublicId").GetGuid();
        IntegrationAssert.AssertUuidV7(publicId, "invitePublicId");
        Assert.That(publicId.Variant, Is.InRange(8, 11));

        var maxUses = item.GetProperty("maxUses");
        var revokedAt = item.GetProperty("revokedAt");
        Assert.Multiple(() =>
        {
            Assert.That(publicId, Is.EqualTo(expected.PublicId));
            Assert.That(item.GetProperty("status").GetString(), Is.EqualTo(expected.Status.ToString().ToUpperInvariant()));
            Assert.That(item.GetProperty("useCount").GetInt32(), Is.EqualTo(expected.UseCount));
            Assert.That(maxUses.ValueKind is JsonValueKind.Null ? (int?)null : maxUses.GetInt32(), Is.EqualTo(expected.MaxUses));
            AssertTimestamp(item.GetProperty("expiresAt"), expected.ExpiresAt, "expiresAt");
            AssertTimestamp(item.GetProperty("createdAt"), expected.CreatedAt, "createdAt");
            AssertTimestamp(item.GetProperty("updatedAt"), expected.UpdatedAt, "updatedAt");
            if (expected.RevokedAt is { } timestamp)
            {
                AssertTimestamp(revokedAt, timestamp, "revokedAt");
            }
            else
            {
                Assert.That(revokedAt.ValueKind, Is.EqualTo(JsonValueKind.Null));
            }
        });
    }

    internal static void AssertInviteUnchanged(SpaceInviteEntity actual, SpaceInviteEntity expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Id, Is.EqualTo(expected.Id));
            Assert.That(actual.PublicId, Is.EqualTo(expected.PublicId));
            Assert.That(actual.SpaceId, Is.EqualTo(expected.SpaceId));
            Assert.That(actual.CreatedByUserId, Is.EqualTo(expected.CreatedByUserId));
            Assert.That(actual.TokenHash == expected.TokenHash, Is.True, "The invite token hash must be preserved.");
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            Assert.That(actual.UseCount, Is.EqualTo(expected.UseCount));
            Assert.That(actual.MaxUses, Is.EqualTo(expected.MaxUses));
            Assert.That(actual.ExpiresAt, Is.EqualTo(expected.ExpiresAt));
            Assert.That(actual.Version, Is.EqualTo(expected.Version));
            Assert.That(actual.CreatedAt, Is.EqualTo(expected.CreatedAt));
            Assert.That(actual.UpdatedAt, Is.EqualTo(expected.UpdatedAt));
            Assert.That(actual.RevokedAt, Is.EqualTo(expected.RevokedAt));
        });
    }

    private IssuedToken IssueInviteToken() =>
        Environment.Factory.Services.GetRequiredService<ITokenIssuer>().Issue(TokenKind.SpaceInvite);

    private static void AssertTimestamp(JsonElement value, DateTimeOffset expected, string field)
    {
        var timestamp = value.GetDateTimeOffset();
        IntegrationAssert.AssertUtc(timestamp, field);
        Assert.That(timestamp, Is.EqualTo(expected), $"{field} must match the saved invite.");
    }
}

internal sealed class SeededInvite(SpaceInviteEntity row, string token)
{
    internal SpaceInviteEntity Row { get; } = row;

    internal string Token { get; } = token;
}
