using System.Net;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class SpaceReadEndpointTests : SpaceIntegrationTestBase
{
    [Test]
    public async Task ListSpaces_ShouldReturnOwnedAndAccessibleSpaces()
    {
        var actor = await RegisterSpaceUserAsync();
        var other = await RegisterSpaceUserAsync();
        var ownedActive = await SeedSpaceAsync(actor, "Owned active");
        var ownedArchived = await SeedSpaceAsync(
            actor,
            "Owned archived",
            status: SpaceStatus.Archived);
        var memberActive = await SeedSpaceAsync(other, "Member active");
        await SeedMembershipAsync(memberActive, actor, other);
        var memberArchived = await SeedSpaceAsync(
            other,
            "Member archived",
            status: SpaceStatus.Archived);
        await SeedMembershipAsync(memberArchived, actor, other);
        await SeedSpaceAsync(other, "Inaccessible");
        var deleting = await SeedSpaceAsync(
            actor,
            "Deleting",
            status: SpaceStatus.Deleting);

        using var response = await Api.GetAsync(
            $"{SpacesPath}?limit=100",
            actor.AccessToken);
        var page = await ReadSpacePageAsync(response);

        var expected = new[]
        {
            ownedActive.PublicId,
            ownedArchived.PublicId,
            memberActive.PublicId,
            memberArchived.PublicId,
        };
        Assert.Multiple(() =>
        {
            Assert.That(page.Items.Select(item => item.SpacePublicId), Is.EquivalentTo(expected));
            Assert.That(page.Items.Select(item => item.SpacePublicId), Does.Not.Contain(deleting.PublicId));
            Assert.That(page.NextCursor, Is.Null);
            Assert.That(
                page.Items.Where(item => item.SpacePublicId == ownedActive.PublicId
                        || item.SpacePublicId == ownedArchived.PublicId)
                    .Select(item => item.MyAccess.Source),
                Is.All.EqualTo("OWNER"));
            Assert.That(
                page.Items.Where(item => item.SpacePublicId == memberActive.PublicId
                        || item.SpacePublicId == memberArchived.PublicId)
                    .Select(item => item.MyAccess.Source),
                Is.All.EqualTo("MEMBERSHIP"));
        });
    }

    [Test]
    public async Task ListSpaces_SystemAdministrator_ShouldUseOwnedAndMembershipAccess()
    {
        var administrator = await RegisterSpaceUserAsync();
        var other = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var owned = await SeedSpaceAsync(administrator, "Owned");
        var member = await SeedSpaceAsync(other, "Member");
        await SeedMembershipAsync(
            member,
            administrator,
            other,
            roleKeys: [SpaceRoleSystemKey.Viewer]);
        await SeedSpaceAsync(other, "Outside");

        using var response = await Api.GetAsync(
            $"{SpacesPath}?limit=100",
            administrator.AccessToken);
        var page = await ReadSpacePageAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(
                page.Items.Select(item => item.SpacePublicId),
                Is.EquivalentTo(new[] { owned.PublicId, member.PublicId }));
            Assert.That(
                page.Items.Single(item => item.SpacePublicId == owned.PublicId).MyAccess.Source,
                Is.EqualTo("OWNER"));
            Assert.That(
                page.Items.Single(item => item.SpacePublicId == member.PublicId).MyAccess.Source,
                Is.EqualTo("MEMBERSHIP"));
        });
    }

    [TestCase("ASC")]
    [TestCase("DESC")]
    public async Task ListSpaces_ShouldPaginateEqualTimestampsByPublicId(string direction)
    {
        var actor = await RegisterSpaceUserAsync();
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var spaces = new[]
        {
            await SeedSpaceAsync(actor, "First", createdAt: createdAt),
            await SeedSpaceAsync(actor, "Second", createdAt: createdAt),
            await SeedSpaceAsync(actor, "Third", createdAt: createdAt),
        };
        var expected = spaces
            .Select(space => space.PublicId.ToString("D"))
            .OrderBy(publicId => publicId, StringComparer.Ordinal)
            .ToArray();
        if (direction == "DESC")
        {
            expected = expected.Reverse().ToArray();
        }

        string? cursor = null;
        for (var index = 0; index < expected.Length; index++)
        {
            var cursorQuery = cursor is null
                ? string.Empty
                : $"&cursor={Uri.EscapeDataString(cursor)}";
            using var response = await Api.GetAsync(
                $"{SpacesPath}?limit=1&sort=createdAt&direction={direction}{cursorQuery}",
                actor.AccessToken);
            var page = await ReadSpacePageAsync(response);

            Assert.Multiple(() =>
            {
                Assert.That(page.Items, Has.Count.EqualTo(1));
                Assert.That(
                    page.Items[0].SpacePublicId.ToString("D"),
                    Is.EqualTo(expected[index]));
                Assert.That(
                    page.NextCursor,
                    index == expected.Length - 1 ? Is.Null : Is.Not.Null.And.Not.Empty);
            });
            cursor = page.NextCursor;
        }
    }

    [TestCase("")]
    [TestCase("not-a-valid-cursor")]
    public async Task ListSpaces_ShouldRejectTamperedCursor(string cursor)
    {
        var actor = await RegisterSpaceUserAsync();

        using var response = await Api.GetAsync(
            $"{SpacesPath}?cursor={Uri.EscapeDataString(cursor)}",
            actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "CURSOR_INVALID");
    }

    [Test]
    public async Task ListSpaces_ShouldRejectCursorFromDifferentActor()
    {
        var firstActor = await RegisterSpaceUserAsync();
        var secondActor = await RegisterSpaceUserAsync();
        var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await SeedSpaceAsync(firstActor, "First", createdAt: createdAt);
        await SeedSpaceAsync(firstActor, "Second", createdAt: createdAt);
        await SeedSpaceAsync(secondActor, "Other");

        string? cursor;
        using (var response = await Api.GetAsync(
                   $"{SpacesPath}?limit=1",
                   firstActor.AccessToken))
        {
            cursor = (await ReadSpacePageAsync(response)).NextCursor;
            Assert.That(cursor, Is.Not.Null.And.Not.Empty);
        }

        using var rejected = await Api.GetAsync(
            $"{SpacesPath}?limit=1&cursor={Uri.EscapeDataString(cursor!)}",
            secondActor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            rejected,
            HttpStatusCode.BadRequest,
            "CURSOR_INVALID");
    }

    [Test]
    public async Task ListSpaces_ShouldRejectCursorForDifferentDirection()
    {
        var actor = await RegisterSpaceUserAsync();
        await SeedSpaceAsync(actor, "First");
        await SeedSpaceAsync(actor, "Second");

        string? cursor;
        using (var response = await Api.GetAsync(
                   $"{SpacesPath}?limit=1&direction=ASC",
                   actor.AccessToken))
        {
            cursor = (await ReadSpacePageAsync(response)).NextCursor;
            Assert.That(cursor, Is.Not.Null.And.Not.Empty);
        }

        using var rejected = await Api.GetAsync(
            $"{SpacesPath}?limit=1&direction=DESC&cursor={Uri.EscapeDataString(cursor!)}",
            actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            rejected,
            HttpStatusCode.BadRequest,
            "CURSOR_INVALID");
    }

    [TestCase("limit=0")]
    [TestCase("limit=101")]
    [TestCase("sort=name")]
    [TestCase("direction=sideways")]
    public async Task ListSpaces_ShouldRejectInvalidQuery(string query)
    {
        var actor = await RegisterSpaceUserAsync();

        using var response = await Api.GetAsync(
            $"{SpacesPath}?{query}",
            actor.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
    }

    [Test]
    public async Task GetSpace_ShouldReturnDetailAndETag()
    {
        var owner = await RegisterSpaceUserAsync();
        var createdAt = new DateTimeOffset(2026, 9, 1, 8, 30, 0, TimeSpan.Zero);
        var space = await SeedSpaceAsync(
            owner,
            "Product",
            "Product files",
            allowedBytes: 1024,
            version: 7,
            createdAt: createdAt);

        using var response = await Api.GetAsync(
            SpacePath(space.PublicId),
            owner.AccessToken);
        var detail = await ReadSpaceAsync(response);
        AssertETag(response, "\"v7\"");

        Assert.Multiple(() =>
        {
            Assert.That(detail.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(detail.OwnerUserPublicId, Is.EqualTo(owner.PublicId));
            Assert.That(detail.Name, Is.EqualTo("Product"));
            Assert.That(detail.Description, Is.EqualTo("Product files"));
            Assert.That(detail.Status, Is.EqualTo("ACTIVE"));
            Assert.That(detail.MyAccess.Source, Is.EqualTo("OWNER"));
            Assert.That(detail.MyAccess.Permissions, Does.Contain("SPACE_VIEW"));
            Assert.That(detail.MyAccess.Permissions, Does.Contain("SPACE_UPDATE"));
            Assert.That(detail.CreatedAt, Is.EqualTo(space.CreatedAt));
            Assert.That(detail.UpdatedAt, Is.EqualTo(space.UpdatedAt));
        });
        IntegrationAssert.AssertUuidV7(detail.SpacePublicId, "spacePublicId");
        IntegrationAssert.AssertUtc(detail.CreatedAt, "createdAt");
        IntegrationAssert.AssertUtc(detail.UpdatedAt, "updatedAt");
    }

    [Test]
    public async Task GetSpace_ShouldReturnArchivedMembershipAccess()
    {
        var owner = await RegisterSpaceUserAsync();
        var member = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(
            owner,
            status: SpaceStatus.Archived,
            version: 3);
        await SeedMembershipAsync(
            space,
            member,
            owner,
            roleKeys: [SpaceRoleSystemKey.Viewer]);

        using var response = await Api.GetAsync(
            SpacePath(space.PublicId),
            member.AccessToken);
        var detail = await ReadSpaceAsync(response);
        AssertETag(response, "\"v3\"");
        Assert.Multiple(() =>
        {
            Assert.That(detail.Status, Is.EqualTo("ARCHIVED"));
            Assert.That(detail.MyAccess.Source, Is.EqualTo("MEMBERSHIP"));
            Assert.That(
                detail.MyAccess.Permissions,
                Is.EquivalentTo(new[] { "SPACE_VIEW", "FILES_READ", "FILES_DOWNLOAD" }));
        });
    }

    [Test]
    public async Task GetSpace_ShouldMaskMissingInaccessibleAndDeletingSpaces()
    {
        var actor = await RegisterSpaceUserAsync();
        var other = await RegisterSpaceUserAsync();
        var inaccessible = await SeedSpaceAsync(other, "Inaccessible");
        var deleting = await SeedSpaceAsync(actor, "Deleting", status: SpaceStatus.Deleting);
        var targets = new[]
        {
            Guid.CreateVersion7(),
            inaccessible.PublicId,
            deleting.PublicId,
        };
        var errors = new List<ErrorEnvelope>();

        foreach (var target in targets)
        {
            using var response = await Api.GetAsync(
                SpacePath(target),
                actor.AccessToken,
                NewCorrelationId());
            errors.Add(await IntegrationAssert.AssertErrorAsync(
                response,
                HttpStatusCode.NotFound,
                "RESOURCE_NOT_FOUND"));
        }

        AssertSamePublicError(errors.ToArray());
    }

    [Test]
    public async Task GetSpace_SystemAdministrator_ShouldMaskInaccessibleSpace()
    {
        var administrator = await RegisterSpaceUserAsync();
        var other = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var space = await SeedSpaceAsync(other, "Outside");

        using var response = await Api.GetAsync(
            SpacePath(space.PublicId),
            administrator.AccessToken,
            NewCorrelationId());
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.NotFound,
            "RESOURCE_NOT_FOUND");
    }

    [Test]
    public async Task GetSpaces_ShouldRejectMissingSession()
    {
        var correlationId = NewCorrelationId();
        using var response = await Api.GetAsync(SpacesPath, correlationId: correlationId);

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            "AUTH_TOKEN_REQUIRED");
        Assert.That(
            TestApiClient.GetHeader(response, TestApiClient.CorrelationHeaderName),
            Is.EqualTo(correlationId));
    }

    [Test]
    public async Task GetSpaces_ShouldRejectInvalidSession()
    {
        using var response = await Api.GetAsync(
            SpacesPath,
            $"cs_sess_{new string('a', 43)}",
            NewCorrelationId());

        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Unauthorized,
            "AUTH_TOKEN_INVALID");
    }

    [TestCase("not-a-uuid")]
    [TestCase("018f2f6e-8a9a-4c3a-9d7a-3f4b5a6b7c8d")]
    public async Task GetSpace_ShouldRejectInvalidUuidV7(string publicId)
    {
        var actor = await RegisterSpaceUserAsync();

        using var response = await Api.GetAsync(
            $"{SpacesPath}/{publicId}",
            actor.AccessToken,
            NewCorrelationId());
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
    }
}
