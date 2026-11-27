using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.Spaces;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Mounts;

public abstract class MountIntegrationTestBase : SpaceIntegrationTestBase
{
    internal static string MountsPath(Guid spacePublicId) => $"{SpacePath(spacePublicId)}/mounts";

    internal static string MountPath(Guid spacePublicId, Guid mountPublicId) =>
        $"{MountsPath(spacePublicId)}/{mountPublicId:D}";

    internal Task<HttpResponseMessage> PostMountAsync(
        SpaceTestUser actor, Guid spacePublicId, string json, string? idempotencyKey) =>
        Api.SendRawAsync(HttpMethod.Post, MountsPath(spacePublicId), json, "application/json", actor.AccessToken,
            configure: request =>
            {
                if (idempotencyKey is not null)
                {
                    request.Headers.TryAddWithoutValidation(TestApiClient.IdempotencyKeyHeaderName, idempotencyKey);
                }
            });

    internal async Task<MountEntity> SeedMountAsync(
        SpaceEntity space,
        MountVisibility visibility = MountVisibility.Public,
        MountStatus status = MountStatus.Active,
        DateTimeOffset? createdAt = null)
    {
        var now = ToPersistencePrecision(createdAt ?? Clock.UtcNow);
        var mount = new MountEntity
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = space.Id,
            Name = "Design",
            Visibility = visibility,
            Status = status,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
            ArchivedAt = status == MountStatus.Archived ? now : null,
        };

        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Mounts.Add(mount);
        await db.SaveChangesAsync();
        return mount;
    }

    internal async Task<IReadOnlyList<MountEntity>> ReadStoredMountsAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Mounts.AsNoTracking().Where(mount => mount.SpaceId == spaceId).ToListAsync();
    }

    internal async Task<(long[] Roles, long[] Users)> ReadOverwriteTargetsAsync(long mountId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await db.MountRoleGrants.Where(grant => grant.MountId == mountId)
            .Select(grant => grant.RoleId).ToArrayAsync();
        var users = await db.MountUserGrants.Where(grant => grant.MountId == mountId)
            .Select(grant => grant.UserId).ToArrayAsync();
        return (roles, users);
    }

    internal static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await ReadSuccessBodyAsync(response, status);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    internal static async Task<JsonElement> ReadMountAsync(
        HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var mount = await ReadJsonAsync(response, status);
        IntegrationAssert.AssertPropertyNames(mount, "spacePublicId", "mountPublicId", "name", "visibility",
            "status", "myAccess", "createdAt", "updatedAt", "archivedAt");
        IntegrationAssert.AssertPropertyNames(mount.GetProperty("myAccess"), "permissions", "canManage");
        IntegrationAssert.AssertUuidV7(mount.GetProperty("mountPublicId").GetGuid(), "mountPublicId");
        IntegrationAssert.AssertUtc(mount.GetProperty("createdAt").GetDateTimeOffset(), "createdAt");
        IntegrationAssert.AssertUtc(mount.GetProperty("updatedAt").GetDateTimeOffset(), "updatedAt");
        return mount;
    }

    internal static async Task AssertValidationAsync(HttpResponseMessage response, string field, string code)
    {
        var error = await IntegrationAssert.AssertErrorAsync(
            response, HttpStatusCode.BadRequest, "REQUEST_VALIDATION_FAILED");
        Assert.That(error.Error.Details.Select(detail => (detail.Field, detail.Code)), Does.Contain((field, code)));
    }

    internal static string[] ReadCodes(JsonElement item, string property) =>
        item.GetProperty(property).EnumerateArray().Select(code => code.GetString()!).ToArray();
}
