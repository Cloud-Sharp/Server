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
public sealed class SpaceMutationEndpointTests : SpaceIntegrationTestBase
{
    [Test]
    public async Task UpdateSpace_ShouldPreserveOmittedDescription()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original", "Existing description");

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"  Renamed  "}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v1\"");
        var updated = await ReadSpaceAsync(response);
        AssertETag(response, "\"v2\"");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(updated.Name, Is.EqualTo("Renamed"));
            Assert.That(updated.Description, Is.EqualTo("Existing description"));
            Assert.That(persisted!.Name, Is.EqualTo("Renamed"));
            Assert.That(persisted.Description, Is.EqualTo("Existing description"));
            Assert.That(persisted.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task UpdateSpace_ShouldClearExplicitNullDescription()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original", "Existing description", version: 4);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"description":null}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v4\"");
        var updated = await ReadSpaceAsync(response);
        AssertETag(response, "\"v5\"");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(updated.Name, Is.EqualTo("Original"));
            Assert.That(updated.Description, Is.Null);
            Assert.That(persisted!.Name, Is.EqualTo("Original"));
            Assert.That(persisted.Description, Is.Null);
            Assert.That(persisted.Version, Is.EqualTo(5));
        });
    }

    [TestCase("{}")]
    [TestCase("{\"name\":\"Original\"}")]
    [TestCase("{\"name\":\"  Original  \",\"description\":\"Existing description\"}")]
    public async Task UpdateSpace_ShouldReturnCurrentResourceForNoOp(string body)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original", "Existing description", version: 4);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            body,
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v4\"");
        var updated = await ReadSpaceAsync(response);
        AssertETag(response, "\"v4\"");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(updated.Name, Is.EqualTo("Original"));
            Assert.That(updated.Description, Is.EqualTo("Existing description"));
            Assert.That(updated.UpdatedAt, Is.EqualTo(space.UpdatedAt));
            Assert.That(persisted!.Version, Is.EqualTo(4));
            Assert.That(persisted.UpdatedAt, Is.EqualTo(space.UpdatedAt));
        });
    }

    [Test]
    public async Task UpdateSpace_ShouldRequireIfMatch()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original");

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"Changed"}""",
            "application/json",
            owner.AccessToken);
        await IntegrationAssert.AssertErrorAsync(
            response,
            (HttpStatusCode)428,
            "PRECONDITION_REQUIRED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo("Original"));
            Assert.That(persisted.Version, Is.EqualTo(1));
        });
    }

    [TestCase("W/\"v1\"")]
    [TestCase("*")]
    [TestCase("\"v1\", \"v2\"")]
    [TestCase("\"v0\"")]
    [TestCase("v1")]
    public async Task UpdateSpace_ShouldRejectMalformedIfMatch(string ifMatch)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original");

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"Changed"}""",
            "application/json",
            owner.AccessToken,
            ifMatch: ifMatch);
        var error = await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");
        Assert.That(
            error.Error.Details.Select(detail => (detail.Field, detail.Code)),
            Is.EqualTo(new[] { ("ifMatch", "INVALID_ETAG") }));
        Assert.That((await FindSpaceAsync(space.PublicId))!.Name, Is.EqualTo("Original"));
    }

    [Test]
    public async Task UpdateSpace_ShouldRejectStaleETag()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original", version: 4);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"Changed"}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v3\"");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.PreconditionFailed,
            "PRECONDITION_FAILED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo("Original"));
            Assert.That(persisted.Version, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task UpdateSpace_WithSameETag_ShouldCommitOnlyOneChange()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, "Original");
        using var firstClient = Environment.CreateClient();
        using var secondClient = Environment.CreateClient();

        var firstTask = firstClient.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"First"}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v1\"");
        var secondTask = secondClient.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"Second"}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v1\"");
        using var first = await firstTask;
        using var second = await secondTask;

        Assert.That(
            new[] { first.StatusCode, second.StatusCode },
            Is.EquivalentTo(new[] { HttpStatusCode.OK, HttpStatusCode.PreconditionFailed }));
        var successful = first.StatusCode == HttpStatusCode.OK ? first : second;
        var stale = ReferenceEquals(successful, first) ? second : first;
        var updated = await ReadSpaceAsync(successful);
        AssertETag(successful, "\"v2\"");
        await IntegrationAssert.AssertErrorAsync(
            stale,
            HttpStatusCode.PreconditionFailed,
            "PRECONDITION_FAILED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo(updated.Name));
            Assert.That(persisted.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ArchiveSpace_ShouldArchiveAndPreserveReadAccess()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, version: 2);

        using var response = await SendSpaceActionAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/archive",
            owner,
            "\"v2\"");
        var archived = await ReadSpaceAsync(response);
        AssertETag(response, "\"v3\"");
        Assert.That(archived.Status, Is.EqualTo("ARCHIVED"));

        using var detailResponse = await Api.GetAsync(
            SpacePath(space.PublicId),
            owner.AccessToken);
        var detail = await ReadSpaceAsync(detailResponse);
        Assert.Multiple(() =>
        {
            Assert.That(detail.Status, Is.EqualTo("ARCHIVED"));
            Assert.That(AssertETag(detailResponse), Is.EqualTo("\"v3\""));
        });

        using var repeated = await SendSpaceActionAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/archive",
            owner,
            "\"v3\"");
        await IntegrationAssert.AssertErrorAsync(
            repeated,
            HttpStatusCode.Conflict,
            "SPACE_ARCHIVED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(SpaceStatus.Archived));
            Assert.That(persisted.ArchivedAt, Is.Not.Null);
            Assert.That(persisted.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ReactivateSpace_ShouldRestoreActiveState()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived, version: 2);

        using var response = await SendSpaceActionAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/reactivate",
            owner,
            "\"v2\"");
        var reactivated = await ReadSpaceAsync(response);
        AssertETag(response, "\"v3\"");
        Assert.That(reactivated.Status, Is.EqualTo("ACTIVE"));

        using var repeated = await SendSpaceActionAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/reactivate",
            owner,
            "\"v3\"");
        await IntegrationAssert.AssertErrorAsync(
            repeated,
            HttpStatusCode.Conflict,
            "SPACE_INVALID_STATE");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(persisted.ArchivedAt, Is.Null);
            Assert.That(persisted.Version, Is.EqualTo(3));
        });
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task DeleteSpace_ShouldAcceptDeletionAndMaskSubsequentRequests(SpaceStatus initialStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: initialStatus, version: 2);

        using var response = await SendSpaceActionAsync(
            HttpMethod.Delete,
            SpacePath(space.PublicId),
            owner,
            "\"v2\"");
        var accepted = await ReadSpaceDeletionAsync(response);
        AssertETag(response, "\"v3\"");
        Assert.Multiple(() =>
        {
            Assert.That(accepted.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(accepted.Status, Is.EqualTo("DELETING"));
        });
        IntegrationAssert.AssertUtc(accepted.AcceptedAt, "acceptedAt");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Status, Is.EqualTo(SpaceStatus.Deleting));
            Assert.That(persisted.DeletingAt, Is.Not.Null);
            Assert.That(persisted.Version, Is.EqualTo(3));
        });

        using var detail = await Api.GetAsync(SpacePath(space.PublicId), owner.AccessToken);
        var detailError = await IntegrationAssert.AssertErrorAsync(
            detail,
            HttpStatusCode.NotFound,
            "RESOURCE_NOT_FOUND");
        using var repeated = await SendSpaceActionAsync(
            HttpMethod.Delete,
            SpacePath(space.PublicId),
            owner,
            "\"v3\"");
        var repeatedError = await IntegrationAssert.AssertErrorAsync(
            repeated,
            HttpStatusCode.NotFound,
            "RESOURCE_NOT_FOUND");
        AssertSamePublicError(detailError, repeatedError);
    }

    [Test]
    public async Task GetSpaceQuota_ShouldReturnFiniteValuesAndDetailETag()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(
            owner,
            allowedBytes: 1000,
            usedBytes: 250,
            reservedBytes: 100,
            version: 4);

        using var detailResponse = await Api.GetAsync(
            SpacePath(space.PublicId),
            owner.AccessToken);
        await ReadSpaceAsync(detailResponse);
        var detailETag = AssertETag(detailResponse, "\"v4\"");

        using var response = await Api.GetAsync(
            $"{SpacePath(space.PublicId)}/quota",
            owner.AccessToken);
        var quota = await ReadSpaceQuotaAsync(response);
        Assert.That(AssertETag(response), Is.EqualTo(detailETag));
        Assert.Multiple(() =>
        {
            Assert.That(quota.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(quota.AllowedBytes, Is.EqualTo(1000));
            Assert.That(quota.UsedBytes, Is.EqualTo(250));
            Assert.That(quota.ReservedBytes, Is.EqualTo(100));
            Assert.That(quota.AvailableBytes, Is.EqualTo(650));
        });
    }

    [Test]
    public async Task GetSpaceQuota_ShouldReturnUnlimitedValuesForArchivedSpace()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(
            owner,
            status: SpaceStatus.Archived,
            usedBytes: 20,
            reservedBytes: 5,
            version: 2);

        using var response = await Api.GetAsync(
            $"{SpacePath(space.PublicId)}/quota",
            owner.AccessToken);
        var quota = await ReadSpaceQuotaAsync(response);
        AssertETag(response, "\"v2\"");
        Assert.Multiple(() =>
        {
            Assert.That(quota.AllowedBytes, Is.Null);
            Assert.That(quota.UsedBytes, Is.EqualTo(20));
            Assert.That(quota.ReservedBytes, Is.EqualTo(5));
            Assert.That(quota.AvailableBytes, Is.Null);
        });
    }

    [Test]
    public async Task ChangeSpaceQuota_ShouldApplyFiniteAndUnlimitedValues()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(
            owner,
            allowedBytes: 1000,
            usedBytes: 100,
            reservedBytes: 50);

        using var finiteResponse = await Api.SendRawAsync(
            HttpMethod.Patch,
            $"{SpacePath(space.PublicId)}/quota",
            """{"allowedBytes":500}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v1\"");
        var finite = await ReadSpaceQuotaAsync(finiteResponse);
        AssertETag(finiteResponse, "\"v2\"");
        Assert.Multiple(() =>
        {
            Assert.That(finite.AllowedBytes, Is.EqualTo(500));
            Assert.That(finite.AvailableBytes, Is.EqualTo(350));
        });

        using var unlimitedResponse = await Api.SendRawAsync(
            HttpMethod.Patch,
            $"{SpacePath(space.PublicId)}/quota",
            """{"allowedBytes":null}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v2\"");
        var unlimited = await ReadSpaceQuotaAsync(unlimitedResponse);
        AssertETag(unlimitedResponse, "\"v3\"");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(unlimited.AllowedBytes, Is.Null);
            Assert.That(unlimited.AvailableBytes, Is.Null);
            Assert.That(unlimited.UsedBytes, Is.EqualTo(100));
            Assert.That(unlimited.ReservedBytes, Is.EqualTo(50));
            Assert.That(persisted!.AllowedBytes, Is.Null);
            Assert.That(persisted.UsedBytes, Is.EqualTo(100));
            Assert.That(persisted.ReservedBytes, Is.EqualTo(50));
            Assert.That(persisted.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ChangeSpaceQuota_ShouldRejectOmittedAllowedBytes()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, allowedBytes: 500, usedBytes: 100, version: 2);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            $"{SpacePath(space.PublicId)}/quota",
            "{}",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v2\"");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.BadRequest,
            "REQUEST_VALIDATION_FAILED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.AllowedBytes, Is.EqualTo(500));
            Assert.That(persisted.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ChangeSpaceQuota_ShouldRejectUndersizedValueWithoutPersistence()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(
            owner,
            allowedBytes: 1000,
            usedBytes: 250,
            reservedBytes: 100,
            version: 3);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            $"{SpacePath(space.PublicId)}/quota",
            """{"allowedBytes":349}""",
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v3\"");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            "SPACE_QUOTA_TOO_SMALL");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.AllowedBytes, Is.EqualTo(1000));
            Assert.That(persisted.UsedBytes, Is.EqualTo(250));
            Assert.That(persisted.ReservedBytes, Is.EqualTo(100));
            Assert.That(persisted.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task TransferSpaceOwnership_ShouldReplaceMembershipsAtomically()
    {
        var previousOwner = await RegisterSpaceUserAsync();
        var newOwner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(previousOwner, version: 5);
        var newOwnerMembership = await SeedMembershipAsync(
            space,
            newOwner,
            previousOwner,
            roleKeys: [SpaceRoleSystemKey.Member, SpaceRoleSystemKey.Viewer]);
        var body = $"{{\"newOwnerUserPublicId\":\"{newOwner.PublicId:D}\"}}";

        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/ownership-transfers",
            body,
            "application/json",
            previousOwner.AccessToken,
            ifMatch: "\"v5\"");
        var transferred = await ReadSpaceAsync(response);
        AssertETag(response, "\"v6\"");
        Assert.Multiple(() =>
        {
            Assert.That(transferred.OwnerUserPublicId, Is.EqualTo(newOwner.PublicId));
            Assert.That(transferred.MyAccess.Source, Is.EqualTo("MEMBERSHIP"));
            Assert.That(transferred.MyAccess.Permissions, Does.Contain("SPACE_UPDATE"));
        });

        var persisted = await FindSpaceAsync(space.PublicId);
        var memberships = await GetSpaceMembershipsAsync(space.Id);
        var links = await GetSpaceMembershipRoleLinksAsync(space.Id);
        var roles = await GetSpaceRolesAsync(space.Id);
        var previousOwnerMembership = memberships.SingleOrDefault(
            membership => membership.UserId == previousOwner.Id);
        var adminRole = roles.Single(role => role.SystemKey == SpaceRoleSystemKey.Admin);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.OwnerUserId, Is.EqualTo(newOwner.Id));
            Assert.That(persisted.Version, Is.EqualTo(6));
            Assert.That(
                memberships.Any(membership => membership.Id == newOwnerMembership.Id),
                Is.False,
                "The new OWNER's Membership must be removed.");
            Assert.That(
                links.Any(link => link.MembershipId == newOwnerMembership.Id),
                Is.False,
                "The new OWNER's Role links must be removed.");
            Assert.That(previousOwnerMembership, Is.Not.Null);
            Assert.That(previousOwnerMembership!.Status, Is.EqualTo(SpaceMembershipStatus.Active));
            Assert.That(
                links.Where(link => link.MembershipId == previousOwnerMembership.Id)
                    .Select(link => link.RoleId),
                Is.EqualTo(new[] { adminRole.Id }));
        });
    }

    [Test]
    public async Task TransferSpaceOwnership_ShouldReturnCurrentSpaceForCurrentOwner()
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, version: 4);
        var body = $"{{\"newOwnerUserPublicId\":\"{owner.PublicId:D}\"}}";

        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/ownership-transfers",
            body,
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v4\"");
        var transferred = await ReadSpaceAsync(response);
        AssertETag(response, "\"v4\"");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(transferred.OwnerUserPublicId, Is.EqualTo(owner.PublicId));
            Assert.That(transferred.UpdatedAt, Is.EqualTo(space.UpdatedAt));
            Assert.That(persisted!.OwnerUserId, Is.EqualTo(owner.Id));
            Assert.That(persisted.Version, Is.EqualTo(4));
        });
        Assert.That(await GetSpaceMembershipsAsync(space.Id), Is.Empty);
    }

    [TestCase(null)]
    [TestCase(SpaceMembershipStatus.Suspended)]
    [TestCase(SpaceMembershipStatus.Left)]
    public async Task TransferSpaceOwnership_ShouldRejectIneligibleTarget(
        SpaceMembershipStatus? membershipStatus)
    {
        var owner = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, version: 2);
        if (membershipStatus.HasValue)
        {
            await SeedMembershipAsync(space, target, owner, membershipStatus.Value);
        }

        var body = $"{{\"newOwnerUserPublicId\":\"{target.PublicId:D}\"}}";
        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/ownership-transfers",
            body,
            "application/json",
            owner.AccessToken,
            ifMatch: "\"v2\"");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Conflict,
            "SPACE_TRANSFER_TARGET_INVALID");

        var persisted = await FindSpaceAsync(space.PublicId);
        var memberships = await GetSpaceMembershipsAsync(space.Id);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.OwnerUserId, Is.EqualTo(owner.Id));
            Assert.That(persisted.Version, Is.EqualTo(2));
            Assert.That(
                memberships.Any(membership => membership.UserId == owner.Id),
                Is.False,
                "A failed transfer must not create an owner Membership.");
        });
    }

    [Test]
    public async Task TransferSpaceOwnership_SystemAdministratorOwner_ShouldReturnMembershipAccess()
    {
        var previousOwner = await RegisterSpaceUserAsync();
        var newOwner = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(previousOwner.PublicId, SystemRole.SystemAdmin);
        var space = await SeedSpaceAsync(previousOwner, version: 5);
        await SeedMembershipAsync(space, newOwner, previousOwner);
        var body = $"{{\"newOwnerUserPublicId\":\"{newOwner.PublicId:D}\"}}";

        using var response = await Api.SendRawAsync(
            HttpMethod.Post,
            $"{SpacePath(space.PublicId)}/ownership-transfers",
            body,
            "application/json",
            previousOwner.AccessToken,
            ifMatch: "\"v5\"");
        var transferred = await ReadSpaceAsync(response);

        AssertETag(response, "\"v6\"");
        Assert.Multiple(() =>
        {
            Assert.That(transferred.OwnerUserPublicId, Is.EqualTo(newOwner.PublicId));
            Assert.That(transferred.MyAccess.Source, Is.EqualTo("MEMBERSHIP"));
            Assert.That(transferred.MyAccess.Permissions, Does.Contain("SPACE_UPDATE"));
        });
    }

    [Test]
    public async Task UpdateSpace_SystemAdministratorViewer_ShouldReturnPermissionDenied()
    {
        var owner = await RegisterSpaceUserAsync();
        var administrator = await RegisterSpaceUserAsync();
        await ChangeSystemRoleAsync(administrator.PublicId, SystemRole.SystemAdmin);
        var space = await SeedSpaceAsync(owner, "Original", version: 4);
        await SeedMembershipAsync(
            space,
            administrator,
            owner,
            roleKeys: [SpaceRoleSystemKey.Viewer]);

        using var response = await Api.SendRawAsync(
            HttpMethod.Patch,
            SpacePath(space.PublicId),
            """{"name":"Changed"}""",
            "application/json",
            administrator.AccessToken,
            ifMatch: "\"v4\"");
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Forbidden,
            "PERMISSION_DENIED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.Name, Is.EqualTo("Original"));
            Assert.That(persisted.Version, Is.EqualTo(4));
        });
    }

    [TestCase("delete", "\"v4\"")]
    [TestCase("delete", null)]
    [TestCase("quota", "\"v4\"")]
    [TestCase("quota", null)]
    [TestCase("transfer", "\"v4\"")]
    [TestCase("transfer", null)]
    public async Task OwnerOnlyOperations_ShouldRejectNonOwnerAdministrator(string operation, string? ifMatch)
    {
        var owner = await RegisterSpaceUserAsync();
        var administrator = await RegisterSpaceUserAsync();
        var target = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, allowedBytes: 1000, version: 4);
        await SeedMembershipAsync(
            space,
            administrator,
            owner,
            roleKeys: [SpaceRoleSystemKey.Admin]);
        await SeedMembershipAsync(space, target, owner);

        using var response = operation switch
        {
            "delete" => await SendSpaceActionAsync(
                HttpMethod.Delete,
                SpacePath(space.PublicId),
                administrator,
                ifMatch),
            "quota" => await Api.SendRawAsync(
                HttpMethod.Patch,
                $"{SpacePath(space.PublicId)}/quota",
                """{"allowedBytes":2000}""",
                "application/json",
                administrator.AccessToken,
                ifMatch: ifMatch),
            "transfer" => await Api.SendRawAsync(
                HttpMethod.Post,
                $"{SpacePath(space.PublicId)}/ownership-transfers",
                $"{{\"newOwnerUserPublicId\":\"{target.PublicId:D}\"}}",
                "application/json",
                administrator.AccessToken,
                ifMatch: ifMatch),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        await IntegrationAssert.AssertErrorAsync(
            response,
            HttpStatusCode.Forbidden,
            "PERMISSION_DENIED");

        var persisted = await FindSpaceAsync(space.PublicId);
        Assert.Multiple(() =>
        {
            Assert.That(persisted!.OwnerUserId, Is.EqualTo(owner.Id));
            Assert.That(persisted.AllowedBytes, Is.EqualTo(1000));
            Assert.That(persisted.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(persisted.Version, Is.EqualTo(4));
        });
        Assert.That(await GetSpaceMembershipsAsync(space.Id), Has.Count.EqualTo(2));
    }
}
