using System.Net;
using System.Text.Json;
using CloudSharp.Api.IntegrationTests.TestHost;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.Entities;
using NUnit.Framework;

namespace CloudSharp.Api.IntegrationTests.Spaces;

[TestFixture]
[Category("ApiIntegration")]
[NonParallelizable]
public sealed class InviteAcceptanceEndpointTests : InviteIntegrationTestBase
{
    [TestCase(false, null)]
    [TestCase(true, 2)]
    public async Task Accept_AsNewOrReturningMember_ShouldGrantViewerAndConsumeOneSlot(bool returning, int? maxUses)
    {
        var owner = await RegisterSpaceUserAsync();
        var joiner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var previousMembership = returning
            ? await SeedMembershipAsync(space, joiner, owner, SpaceMembershipStatus.Left, SpaceRoleSystemKey.Member)
            : null;
        var invite = await SeedInviteAsync(space, owner, maxUses: maxUses);
        Clock.Advance(TimeSpan.FromMinutes(1));

        using var response = await AcceptAsync(invite.Token, joiner.AccessToken, NewIdempotencyKey());
        var accepted = await ReadAcceptanceAsync(response, invite.Token);

        var memberships = await GetSpaceMembershipsAsync(space.Id);
        var membership = memberships.Single(row => row.Status == SpaceMembershipStatus.Active);
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(new Acceptance(space.PublicId, membership.PublicId, false)));
            Assert.That(memberships, Has.Count.EqualTo(returning ? 2 : 1));
            Assert.That(membership.UserId, Is.EqualTo(joiner.Id));
            Assert.That(membership.JoinedAt, Is.EqualTo(Clock.UtcNow).Within(TimeSpan.FromMicroseconds(1)));
        });
        if (previousMembership is not null)
        {
            var history = memberships.Single(row => row.Id == previousMembership.Id);
            Assert.Multiple(() =>
            {
                Assert.That(history.PublicId, Is.EqualTo(previousMembership.PublicId));
                Assert.That(history.Status, Is.EqualTo(SpaceMembershipStatus.Left));
                Assert.That(history.LeftAt, Is.EqualTo(previousMembership.LeftAt));
                Assert.That(history.Version, Is.EqualTo(previousMembership.Version));
            });
        }

        await AssertViewerGrantAsync(space, membership);
        await AssertSingleUseAsync(invite.Row, SpaceInviteStatus.Active);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Accept_AsOwnerOrActiveMember_ShouldReturnExistingRelationshipWithoutConsumingASlot(bool asOwner)
    {
        var owner = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var actor = asOwner ? owner : await RegisterSpaceUserAsync();
        var existing = asOwner ? null : await SeedMembershipAsync(space, actor, owner);
        var originalLinks = await GetSpaceMembershipRoleLinksAsync(space.Id);
        var invite = await SeedInviteAsync(space, owner, maxUses: 1);
        Clock.Advance(TimeSpan.FromMinutes(1));

        using var response = await AcceptAsync(invite.Token, actor.AccessToken, NewIdempotencyKey());
        var accepted = await ReadAcceptanceAsync(response, invite.Token);

        Assert.That(accepted, Is.EqualTo(new Acceptance(space.PublicId, existing?.PublicId, true)));
        await AssertMembershipUnchangedAsync(space.Id, existing, originalLinks);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    [Test]
    public async Task Accept_AsSuspendedMember_ShouldReturnConflictWithoutChangingMembershipOrInvite()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var membership = await SeedMembershipAsync(space, actor, owner, SpaceMembershipStatus.Suspended);
        var originalLinks = await GetSpaceMembershipRoleLinksAsync(space.Id);
        var invite = await SeedInviteAsync(space, owner);

        using var response = await AcceptAsync(invite.Token, actor.AccessToken, NewIdempotencyKey());

        await AssertAcceptanceErrorAsync(response, HttpStatusCode.Conflict, "MEMBER_INVALID_STATE", invite.Token);
        await AssertMembershipUnchangedAsync(space.Id, membership, originalLinks);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Accept_WithUsableInviteForArchivedSpace_ShouldReturnConflict(bool alreadyMember)
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);
        var membership = alreadyMember ? await SeedMembershipAsync(space, actor, owner) : null;
        var originalLinks = await GetSpaceMembershipRoleLinksAsync(space.Id);
        var invite = await SeedInviteAsync(space, owner);

        using var response = await AcceptAsync(invite.Token, actor.AccessToken, NewIdempotencyKey());

        await AssertAcceptanceErrorAsync(response, HttpStatusCode.Conflict, "SPACE_ARCHIVED", invite.Token);
        await AssertMembershipUnchangedAsync(space.Id, membership, originalLinks);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    [Test]
    public async Task Accept_WhenCapabilityIsUnusable_ShouldReturnSame404()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var active = await SeedSpaceAsync(owner);
        var archived = await SeedSpaceAsync(owner, status: SpaceStatus.Archived);
        var deleting = await SeedSpaceAsync(owner, status: SpaceStatus.Deleting);
        var deleted = await SeedSpaceAsync(owner, status: SpaceStatus.Deleted);
        var noViewer = await SeedSpaceAsync(owner);
        await RemoveViewerRoleAsync(noViewer.Id);

        var revoked = await SeedInviteAsync(active, owner, status: SpaceInviteStatus.Revoked);
        var exhausted = await SeedInviteAsync(active, owner, status: SpaceInviteStatus.Expired, maxUses: 1, useCount: 1);
        var expiresAtBoundary = await SeedInviteAsync(active, owner, expiresAt: Clock.UtcNow.AddMinutes(1));
        var revokedForArchived = await SeedInviteAsync(archived, owner, status: SpaceInviteStatus.Revoked);
        var inviteForDeleting = await SeedInviteAsync(deleting, owner);
        var inviteForDeleted = await SeedInviteAsync(deleted, owner);
        var inviteWithoutViewer = await SeedInviteAsync(noViewer, owner);
        Clock.Set(expiresAtBoundary.Row.ExpiresAt);

        var errors = new List<ErrorEnvelope>();
        foreach (var token in new[]
                 {
                     NewInviteToken(),
                     "malformed-invite",
                     revoked.Token,
                     exhausted.Token,
                     expiresAtBoundary.Token,
                     revokedForArchived.Token,
                     inviteForDeleting.Token,
                     inviteForDeleted.Token,
                     inviteWithoutViewer.Token,
                 })
        {
            using var response = await AcceptAsync(token, actor.AccessToken, NewIdempotencyKey());
            errors.Add(await AssertAcceptanceErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", token));
        }

        AssertSamePublicError(errors.ToArray());
        foreach (var invite in new[] { revoked, exhausted, expiresAtBoundary, revokedForArchived, inviteForDeleting, inviteForDeleted, inviteWithoutViewer })
        {
            var actual = (await GetInvitesAsync(invite.Row.SpaceId)).Single(row => row.Id == invite.Row.Id);
            AssertInviteUnchanged(actual, invite.Row);
        }

        foreach (var space in new[] { active, archived, deleting, deleted, noViewer })
        {
            await AssertNoMembershipWritesAsync(space.Id);
        }
    }

    [Test]
    public async Task Accept_WithoutAnActiveSession_ShouldReturnUnauthorizedWithoutCreatingMembership()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner);
        await SuspendUserAsync(actor.PublicId);

        foreach (var (session, code) in new (string?, string)[]
                 {
                     (null, "AUTH_TOKEN_REQUIRED"),
                     ("cs_mcp_forged", "AUTH_TOKEN_TYPE_NOT_ALLOWED"),
                     ("cs_sess_unregistered", "AUTH_TOKEN_INVALID"),
                     (actor.AccessToken, "AUTH_TOKEN_INVALID"),
                 })
        {
            using var response = await AcceptAsync(invite.Token, session, NewIdempotencyKey());
            await AssertAcceptanceErrorAsync(response, HttpStatusCode.Unauthorized, code, invite.Token);
        }

        await AssertNoMembershipWritesAsync(space.Id);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    [Test]
    public async Task Accept_WithMissingOrInvalidIdempotencyKey_ShouldReturnBadRequestWithoutCreatingMembership()
    {
        var owner = await RegisterSpaceUserAsync();
        var actor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner);
        const string code = "REQUEST_VALIDATION_FAILED";

        foreach (var key in new[] { null, string.Empty, new string('a', 15), new string('a', 129), new string('é', 16) })
        {
            using var response = await AcceptAsync(invite.Token, actor.AccessToken, key);
            await AssertAcceptanceErrorAsync(response, HttpStatusCode.BadRequest, code, invite.Token);
        }

        using var duplicate = await Api.SendAsync(
            HttpMethod.Post,
            InviteAcceptancePath(invite.Token),
            actor.AccessToken,
            configure: request => request.Headers.TryAddWithoutValidation("Idempotency-Key", new[] { NewIdempotencyKey(), NewIdempotencyKey() }));
        await AssertAcceptanceErrorAsync(duplicate, HttpStatusCode.BadRequest, code, invite.Token);

        await AssertNoMembershipWritesAsync(space.Id);
        AssertInviteUnchanged((await GetInvitesAsync(space.Id)).Single(), invite.Row);
    }

    [Test]
    [Explicit("Requires persisted invite idempotency and response recovery.")]
    public async Task Accept_AfterConsumingLastSlot_ShouldReplayOriginalResultWithSameKeyAfterRedisReset()
    {
        var owner = await RegisterSpaceUserAsync();
        var registration = TestData.NewRegistration();
        var actor = await RegisterSpaceUserAsync(registration);
        var other = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner, maxUses: 1);
        var key = NewIdempotencyKey();

        using var first = await AcceptAsync(invite.Token, actor.AccessToken, key);
        var accepted = await ReadAcceptanceAsync(first, invite.Token);
        using var anonymous = await AcceptAsync(invite.Token, null, key);
        await AssertAcceptanceErrorAsync(anonymous, HttpStatusCode.Unauthorized, "AUTH_TOKEN_REQUIRED", invite.Token);
        using var replay = await AcceptAsync(invite.Token, actor.AccessToken, key);
        Assert.That(await ReadAcceptanceAsync(replay, invite.Token), Is.EqualTo(accepted));
        using var exhausted = await AcceptAsync(invite.Token, other.AccessToken, NewIdempotencyKey());
        await AssertAcceptanceErrorAsync(exhausted, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", invite.Token);

        var refreshedSession = await ReauthenticateAfterRedisResetAsync(registration);
        using var recovered = await AcceptAsync(invite.Token, refreshedSession, key);
        Assert.That(await ReadAcceptanceAsync(recovered, invite.Token), Is.EqualTo(accepted));

        var membership = (await GetSpaceMembershipsAsync(space.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(new Acceptance(space.PublicId, membership.PublicId, false)));
            Assert.That(membership.UserId, Is.EqualTo(actor.Id));
        });
        await AssertViewerGrantAsync(space, membership);
        await AssertSingleUseAsync(invite.Row, SpaceInviteStatus.Expired);
    }

    [Test]
    [Explicit("Requires persisted invite idempotency and independent key scopes.")]
    public async Task Accept_WithSameKeyForAnotherActorOrInvite_ShouldUseIndependentResults()
    {
        var owner = await RegisterSpaceUserAsync();
        var firstActor = await RegisterSpaceUserAsync();
        var secondActor = await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var firstInvite = await SeedInviteAsync(space, owner);
        var secondInvite = await SeedInviteAsync(space, owner);
        var key = NewIdempotencyKey();

        using var first = await AcceptAsync(firstInvite.Token, firstActor.AccessToken, key);
        var firstAcceptance = await ReadAcceptanceAsync(first, firstInvite.Token);
        using var second = await AcceptAsync(firstInvite.Token, secondActor.AccessToken, key);
        var secondAcceptance = await ReadAcceptanceAsync(second, firstInvite.Token);
        using var anotherInvite = await AcceptAsync(secondInvite.Token, firstActor.AccessToken, key);
        var existingAcceptance = await ReadAcceptanceAsync(anotherInvite, secondInvite.Token);

        var memberships = await GetSpaceMembershipsAsync(space.Id);
        var firstMembership = memberships.Single(row => row.UserId == firstActor.Id);
        var secondMembership = memberships.Single(row => row.UserId == secondActor.Id);
        var stored = await GetInvitesAsync(space.Id);
        Assert.Multiple(() =>
        {
            Assert.That(memberships, Has.Count.EqualTo(2));
            Assert.That(firstAcceptance, Is.EqualTo(new Acceptance(space.PublicId, firstMembership.PublicId, false)));
            Assert.That(secondAcceptance, Is.EqualTo(new Acceptance(space.PublicId, secondMembership.PublicId, false)));
            Assert.That(existingAcceptance, Is.EqualTo(new Acceptance(space.PublicId, firstMembership.PublicId, true)));
            Assert.That(stored.Single(row => row.Id == firstInvite.Row.Id).UseCount, Is.EqualTo(2));
        });
        AssertInviteUnchanged(stored.Single(row => row.Id == secondInvite.Row.Id), secondInvite.Row);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Accept_WithConcurrentRequestsAndDistinctKeys_ShouldCommitOneMembershipAndOneSlot(bool sameActor)
    {
        var owner = await RegisterSpaceUserAsync();
        var firstActor = await RegisterSpaceUserAsync();
        var secondActor = sameActor ? firstActor : await RegisterSpaceUserAsync();
        var space = await SeedSpaceAsync(owner);
        var invite = await SeedInviteAsync(space, owner, maxUses: sameActor ? 3 : 1);

        var firstRequest = AcceptAsync(invite.Token, firstActor.AccessToken, NewIdempotencyKey());
        var secondRequest = AcceptAsync(invite.Token, secondActor.AccessToken, NewIdempotencyKey());
        using var first = await firstRequest;
        using var second = await secondRequest;

        var successful = new List<(Acceptance Result, long UserId)>();
        foreach (var (response, actor) in new[] { (first, firstActor), (second, secondActor) })
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                successful.Add((await ReadAcceptanceAsync(response, invite.Token), actor.Id));
            }
            else if (!sameActor && response.StatusCode == HttpStatusCode.NotFound)
            {
                await AssertAcceptanceErrorAsync(response, HttpStatusCode.NotFound, "RESOURCE_NOT_FOUND", invite.Token);
            }
            else
            {
                await AssertAcceptanceErrorAsync(response, HttpStatusCode.Conflict, "REQUEST_CONFLICT", invite.Token);
            }
        }

        var membership = (await GetSpaceMembershipsAsync(space.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(successful.Count(entry => !entry.Result.AlreadyMember), Is.EqualTo(1));
            Assert.That(successful.All(entry =>
                entry.UserId == membership.UserId
                && entry.Result.SpacePublicId == space.PublicId
                && entry.Result.MembershipPublicId == membership.PublicId), Is.True);
        });
        await AssertViewerGrantAsync(space, membership);
        await AssertSingleUseAsync(invite.Row, sameActor ? SpaceInviteStatus.Active : SpaceInviteStatus.Expired);
    }

    private Task<HttpResponseMessage> AcceptAsync(string inviteToken, string? accessToken, string? key) =>
        Api.SendAsync(
            HttpMethod.Post,
            InviteAcceptancePath(inviteToken),
            accessToken,
            configure: WithIdempotencyKey(key));

    private static async Task<Acceptance> ReadAcceptanceAsync(HttpResponseMessage response, string inviteToken)
    {
        var body = await ReadSuccessBodyAsync(response, HttpStatusCode.OK);
        AssertCapabilityHeaders(response);
        IntegrationAssert.AssertDoesNotContain(body, inviteToken, "Acceptance must not disclose the invite token.");
        using var document = JsonDocument.Parse(body);
        var result = document.RootElement;
        IntegrationAssert.AssertPropertyNames(result, "spacePublicId", "membershipPublicId", "alreadyMember");
        var spacePublicId = result.GetProperty("spacePublicId").GetGuid();
        IntegrationAssert.AssertUuidV7(spacePublicId, "spacePublicId");
        Assert.That(spacePublicId.Variant, Is.InRange(8, 11));
        var membership = result.GetProperty("membershipPublicId");
        Guid? membershipPublicId = membership.ValueKind == JsonValueKind.Null ? null : membership.GetGuid();
        if (membershipPublicId is Guid id)
        {
            IntegrationAssert.AssertUuidV7(id, "membershipPublicId");
            Assert.That(id.Variant, Is.InRange(8, 11));
        }

        return new Acceptance(spacePublicId, membershipPublicId, result.GetProperty("alreadyMember").GetBoolean());
    }

    private static async Task<ErrorEnvelope> AssertAcceptanceErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code,
        string inviteToken)
    {
        var error = await IntegrationAssert.AssertErrorAsync(response, status, code);
        AssertCapabilityHeaders(response);
        IntegrationAssert.AssertDoesNotContain(
            await TestApiClient.ReadBodyAsync(response),
            inviteToken,
            "The error must not disclose the capability from the URL.");
        return error;
    }

    private async Task AssertViewerGrantAsync(SpaceEntity space, SpaceMembershipEntity membership)
    {
        var viewer = (await GetSpaceRolesAsync(space.Id)).Single(role => role.SystemKey == SpaceRoleSystemKey.Viewer);
        var links = await GetSpaceMembershipRoleLinksAsync(space.Id);
        Assert.That(links.Where(link => link.MembershipId == membership.Id).Select(link => link.RoleId),
            Is.EqualTo(new[] { viewer.Id }));
    }

    private async Task AssertSingleUseAsync(SpaceInviteEntity original, SpaceInviteStatus expectedStatus)
    {
        var invite = (await GetInvitesAsync(original.SpaceId)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(invite.PublicId, Is.EqualTo(original.PublicId));
            Assert.That(invite.Status, Is.EqualTo(expectedStatus));
            Assert.That(invite.UseCount, Is.EqualTo(original.UseCount + 1));
            Assert.That(invite.Version, Is.EqualTo(original.Version + 1));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Clock.UtcNow).Within(TimeSpan.FromMicroseconds(1)));
            Assert.That(invite.ExpiresAt, Is.EqualTo(original.ExpiresAt));
            Assert.That(invite.MaxUses, Is.EqualTo(original.MaxUses));
            Assert.That(invite.RevokedAt, Is.Null);
        });
    }

    private async Task AssertNoMembershipWritesAsync(long spaceId)
    {
        Assert.That(await GetSpaceMembershipsAsync(spaceId), Is.Empty);
        Assert.That(await GetSpaceMembershipRoleLinksAsync(spaceId), Is.Empty);
    }

    private async Task AssertMembershipUnchangedAsync(
        long spaceId,
        SpaceMembershipEntity? original,
        IReadOnlyList<SpaceMembershipRoleEntity> originalLinks)
    {
        var memberships = await GetSpaceMembershipsAsync(spaceId);
        var links = await GetSpaceMembershipRoleLinksAsync(spaceId);
        if (original is null)
        {
            Assert.That(memberships, Is.Empty);
        }
        else
        {
            var actual = memberships.Single();
            Assert.Multiple(() =>
            {
                Assert.That(actual.PublicId, Is.EqualTo(original.PublicId));
                Assert.That(actual.Status, Is.EqualTo(original.Status));
                Assert.That(actual.Version, Is.EqualTo(original.Version));
                Assert.That(actual.UpdatedAt, Is.EqualTo(original.UpdatedAt));
                Assert.That(actual.SuspendedAt, Is.EqualTo(original.SuspendedAt));
            });
        }

        Assert.That(links.Select(link => (link.MembershipId, link.RoleId, link.AssignedByUserId, link.AssignedAt)),
            Is.EquivalentTo(originalLinks.Select(link => (link.MembershipId, link.RoleId, link.AssignedByUserId, link.AssignedAt))));
    }

    private sealed record Acceptance(Guid SpacePublicId, Guid? MembershipPublicId, bool AlreadyMember);
}
