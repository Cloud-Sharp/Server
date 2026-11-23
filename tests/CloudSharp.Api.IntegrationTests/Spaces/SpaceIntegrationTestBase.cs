using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

public abstract class SpaceIntegrationTestBase : ApiIntegrationTestBase
{
    internal const string SpacesPath = "/api/v2/spaces";

    internal async Task<SpaceTestUser> RegisterSpaceUserAsync()
    {
        var session = await RegisterAsync(TestData.NewRegistration());
        var userId = await GetUserIdAsync(session.User.UserPublicId);
        return new SpaceTestUser(session.User.UserPublicId, userId, session.AccessToken);
    }

    internal async Task<SpaceEntity> SeedSpaceAsync(
        SpaceTestUser owner,
        string name = "Space",
        string? description = null,
        SpaceStatus status = SpaceStatus.Active,
        long? allowedBytes = null,
        long usedBytes = 0,
        long reservedBytes = 0,
        long version = 1,
        DateTimeOffset? createdAt = null)
    {
        var now = ToPersistencePrecision(createdAt ?? Clock.UtcNow);

        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var space = new SpaceEntity
        {
            PublicId = Guid.CreateVersion7(),
            OwnerUserId = owner.Id,
            Name = name,
            Description = description,
            Status = status,
            AllowedBytes = allowedBytes,
            UsedBytes = usedBytes,
            ReservedBytes = reservedBytes,
            Version = version,
            CreatedAt = now,
            UpdatedAt = now,
            ArchivedAt = status == SpaceStatus.Archived ? now : null,
            DeletingAt = status is SpaceStatus.Deleting or SpaceStatus.Deleted ? now : null,
            DeletedAt = status == SpaceStatus.Deleted ? now : null,
        };

        context.Spaces.Add(space);
        await context.SaveChangesAsync();

        foreach (var systemKey in Enum.GetValues<SpaceRoleSystemKey>())
        {
            var role = SpaceRole.CreateManaged(space.Id, systemKey, now);
            Assert.That(role.IsSuccess, Is.True, "The managed Role seed must be valid.");
            context.SpaceRoles.Add(SpaceRoleEntity.FromDomain(role.Value));
        }

        await context.SaveChangesAsync();
        return space;
    }

    internal async Task<SpaceMembershipEntity> SeedMembershipAsync(
        SpaceEntity space,
        SpaceTestUser user,
        SpaceTestUser assignedBy,
        SpaceMembershipStatus status = SpaceMembershipStatus.Active,
        params SpaceRoleSystemKey[] roleKeys)
    {
        roleKeys = roleKeys.Length > 0 ? roleKeys : [SpaceRoleSystemKey.Member];
        var now = ToPersistencePrecision(Clock.UtcNow);

        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roles = await context.SpaceRoles
            .Where(role => role.SpaceId == space.Id && role.DeletedAt == null)
            .ToListAsync();
        var selectedRoles = roles
            .Where(role => role.SystemKey.HasValue && roleKeys.Contains(role.SystemKey.Value))
            .ToArray();
        Assert.That(
            selectedRoles.Select(role => role.SystemKey).Distinct().Count(),
            Is.EqualTo(roleKeys.Distinct().Count()),
            "Every requested Role seed must exist.");

        var membership = new SpaceMembershipEntity
        {
            PublicId = Guid.CreateVersion7(),
            SpaceId = space.Id,
            UserId = user.Id,
            Status = status,
            Version = 1,
            JoinedAt = now,
            UpdatedAt = now,
            SuspendedAt = status == SpaceMembershipStatus.Suspended ? now : null,
            LeftAt = status == SpaceMembershipStatus.Left ? now : null,
        };
        context.SpaceMemberships.Add(membership);
        await context.SaveChangesAsync();

        foreach (var role in selectedRoles)
        {
            context.SpaceMembershipRoles.Add(new SpaceMembershipRoleEntity
            {
                MembershipId = membership.Id,
                RoleId = role.Id,
                SpaceId = space.Id,
                AssignedByUserId = assignedBy.Id,
                AssignedAt = now,
            });
        }

        await context.SaveChangesAsync();
        return membership;
    }

    internal async Task<SpaceEntity?> FindSpaceAsync(Guid publicId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Spaces.AsNoTracking()
            .SingleOrDefaultAsync(space => space.PublicId == publicId);
    }

    internal async Task<IReadOnlyList<SpaceEntity>> GetSpacesAsync()
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.Spaces.AsNoTracking().ToListAsync();
    }

    internal async Task<IReadOnlyList<SpaceRoleEntity>> GetSpaceRolesAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SpaceRoles.AsNoTracking()
            .Where(role => role.SpaceId == spaceId)
            .ToListAsync();
    }

    internal async Task<IReadOnlyList<SpaceMembershipEntity>> GetSpaceMembershipsAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SpaceMemberships.AsNoTracking()
            .Where(membership => membership.SpaceId == spaceId)
            .ToListAsync();
    }

    internal async Task<IReadOnlyList<SpaceMembershipRoleEntity>> GetSpaceMembershipRoleLinksAsync(long spaceId)
    {
        await using var scope = Environment.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await context.SpaceMembershipRoles.AsNoTracking()
            .Where(link => link.SpaceId == spaceId)
            .ToListAsync();
    }

    internal static Task<HttpResponseMessage> CreateSpaceAsync(
        TestApiClient api,
        SpaceTestUser actor,
        string json,
        string mediaType = "application/json") =>
        api.SendRawAsync(
            HttpMethod.Post,
            SpacesPath,
            json,
            mediaType,
            actor.AccessToken);

    internal Task<HttpResponseMessage> SendSpaceActionAsync(
        HttpMethod method,
        string path,
        SpaceTestUser actor,
        string? ifMatch = null) =>
        Api.SendAsync(
            method,
            path,
            actor.AccessToken,
            configure: request =>
            {
                if (ifMatch is not null)
                {
                    request.Headers.TryAddWithoutValidation(TestApiClient.IfMatchHeaderName, ifMatch);
                }
            });

    internal static string SpacePath(Guid publicId) => $"{SpacesPath}/{publicId:D}";

    internal static string NewCorrelationId() => $"space-{Guid.NewGuid():N}";

    internal static async Task<SpaceWireResponse> ReadSpaceAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus = HttpStatusCode.OK)
    {
        var body = await ReadSuccessBodyAsync(response, expectedStatus);
        using var document = JsonDocument.Parse(body);
        AssertSpaceProperties(document.RootElement);
        return Deserialize<SpaceWireResponse>(body);
    }

    internal static async Task<SpacePageWireResponse> ReadSpacePageAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        IntegrationAssert.AssertPropertyNames(document.RootElement, "items", "nextCursor");
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            AssertSpaceProperties(item);
        }

        return Deserialize<SpacePageWireResponse>(body);
    }

    internal static async Task<SpaceQuotaWireResponse> ReadSpaceQuotaAsync(HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(body);
        IntegrationAssert.AssertPropertyNames(
            document.RootElement,
            "spacePublicId",
            "allowedBytes",
            "usedBytes",
            "reservedBytes",
            "availableBytes");
        return Deserialize<SpaceQuotaWireResponse>(body);
    }

    internal static async Task<SpaceDeletionAcceptedWireResponse> ReadSpaceDeletionAsync(
        HttpResponseMessage response)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.Accepted);
        using var document = JsonDocument.Parse(body);
        IntegrationAssert.AssertPropertyNames(
            document.RootElement,
            "spacePublicId",
            "status",
            "acceptedAt");
        return Deserialize<SpaceDeletionAcceptedWireResponse>(body);
    }

    internal static string AssertETag(HttpResponseMessage response, string? expected = null)
    {
        var etag = TestApiClient.GetHeader(response, "ETag");
        Assert.That(etag, Does.Match("^\\\"v[1-9][0-9]*\\\"$"));
        if (expected is not null)
        {
            Assert.That(etag, Is.EqualTo(expected));
        }

        return etag!;
    }

    internal static void AssertSamePublicError(params ErrorEnvelope[] errors)
    {
        Assert.That(errors, Is.Not.Empty);
        var expected = errors[0].Error;
        Assert.That(expected.Message, Is.Not.Null.And.Not.Empty);
        foreach (var actual in errors.Skip(1).Select(error => error.Error))
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Code, Is.EqualTo(expected.Code));
                Assert.That(actual.Message, Is.EqualTo(expected.Message));
                Assert.That(
                    actual.Details.Select(detail => (detail.Field, detail.Code)),
                    Is.EqualTo(expected.Details.Select(detail => (detail.Field, detail.Code))));
            });
        }
    }

    internal static async Task<string> ReadSuccessBodyAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus)
    {
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatus));
        IntegrationAssert.AssertJsonContentType(response);
        IntegrationAssert.AssertNoStore(response);
        return await TestApiClient.ReadBodyAsync(response);
    }

    internal static T Deserialize<T>(string body) =>
        JsonSerializer.Deserialize<T>(body, TestApiClient.JsonOptions)
        ?? throw new InvalidOperationException("The successful response body must contain JSON.");

    private static void AssertSpaceProperties(JsonElement space)
    {
        IntegrationAssert.AssertPropertyNames(
            space,
            "spacePublicId",
            "ownerUserPublicId",
            "name",
            "description",
            "status",
            "myAccess",
            "createdAt",
            "updatedAt");
        IntegrationAssert.AssertPropertyNames(
            space.GetProperty("myAccess"),
            "source",
            "permissions");
    }

    private static DateTimeOffset ToPersistencePrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}

internal sealed class SpaceTestUser(Guid publicId, long id, string accessToken)
{
    internal Guid PublicId { get; } = publicId;
    internal long Id { get; } = id;
    internal string AccessToken { get; } = accessToken;

    public override string ToString() => $"SpaceTestUser {{ PublicId = {PublicId} }}";
}
