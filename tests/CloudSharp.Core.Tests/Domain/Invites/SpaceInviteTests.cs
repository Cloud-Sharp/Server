using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Core.Domain.Memberships;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Invites;

[TestFixture]
public class SpaceInviteTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 30, 3, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CreatedAt = Now.AddDays(-1);

    private static readonly DateTimeOffset ExpiresAt = Now.AddDays(7);

    [Test]
    public void Create_WithValidInputs_ShouldCreateActiveInviteWithDefaults()
    {
        var result = SpaceInvite.Create(7, 9, Hash(), ExpiresAt, 20, Now);

        Assert.That(result.IsSuccess, Is.True);
        var invite = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(invite.Id, Is.Zero);
            Assert.That(invite.PublicId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(invite.PublicId.ToString("D")[14], Is.EqualTo('7'));
            Assert.That(invite.SpaceId, Is.EqualTo(7));
            Assert.That(invite.CreatedByUserId, Is.EqualTo(9));
            Assert.That(invite.TokenHash, Is.EqualTo(Hash()));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(invite.ExpiresAt, Is.EqualTo(ExpiresAt));
            Assert.That(invite.UseCount, Is.Zero);
            Assert.That(invite.MaxUses, Is.EqualTo(20));
            Assert.That(invite.GrantedRole, Is.EqualTo(SpaceRoleSystemKey.Viewer));
            Assert.That(invite.Version, Is.EqualTo(1));
            Assert.That(invite.CreatedAt, Is.EqualTo(Now));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now));
            Assert.That(invite.RevokedAt, Is.Null);
        });
    }

    [TestCase(1)]
    [TestCase(20)]
    [TestCase(null)]
    public void Create_WithMaxUsesVariations_ShouldPreserveMaxUses(int? maxUses)
    {
        var result = SpaceInvite.Create(7, 9, Hash(), ExpiresAt, maxUses, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.MaxUses, Is.EqualTo(maxUses));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidSpaceId_ShouldFail(long spaceId)
    {
        AssertError(
            SpaceInvite.Create(spaceId, 9, Hash(), ExpiresAt, null, Now),
            ErrorCodes.SpaceInvite.SpaceIdInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidCreatedBy_ShouldFail(long createdByUserId)
    {
        AssertError(
            SpaceInvite.Create(7, createdByUserId, Hash(), ExpiresAt, null, Now),
            ErrorCodes.SpaceInvite.CreatedByInvalid);
    }

    [Test]
    public void Create_WithNullTokenHash_ShouldFail()
    {
        AssertError(
            SpaceInvite.Create(7, 9, null!, ExpiresAt, null, Now),
            ErrorCodes.SpaceInvite.TokenHashInvalid);
    }

    [Test]
    public void Create_WithNonUtcNow_ShouldFail()
    {
        AssertError(
            SpaceInvite.Create(7, 9, Hash(), ExpiresAt, null, Now.ToOffset(TimeSpan.FromHours(9))),
            ErrorCodes.SpaceInvite.InvalidState);
    }

    [TestCaseSource(nameof(NonFutureExpiries))]
    public void Create_WithNonFutureExpiry_ShouldFail(DateTimeOffset expiresAt)
    {
        AssertError(
            SpaceInvite.Create(7, 9, Hash(), expiresAt, null, Now),
            ErrorCodes.SpaceInvite.ExpiresAtInvalid);
    }

    [Test]
    public void Create_WithNonUtcExpiry_ShouldFail()
    {
        AssertError(
            SpaceInvite.Create(7, 9, Hash(), ExpiresAt.ToOffset(TimeSpan.FromHours(9)), null, Now),
            ErrorCodes.SpaceInvite.ExpiresAtInvalid);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidMaxUses_ShouldFail(int maxUses)
    {
        AssertError(
            SpaceInvite.Create(7, 9, Hash(), ExpiresAt, maxUses, Now),
            ErrorCodes.SpaceInvite.MaxUsesInvalid);
    }

    [TestCase(SpaceInviteStatus.Active)]
    [TestCase(SpaceInviteStatus.Revoked)]
    [TestCase(SpaceInviteStatus.Expired)]
    public void GrantedRole_ShouldAlwaysBeViewer(SpaceInviteStatus status)
    {
        var created = SpaceInvite.Create(7, 9, Hash(), ExpiresAt, null, Now);

        Assert.That(created.Value.GrantedRole, Is.EqualTo(SpaceRoleSystemKey.Viewer));
        Assert.That(InState(status).GrantedRole, Is.EqualTo(SpaceRoleSystemKey.Viewer));
    }

    [TestCase(SpaceInviteStatus.Active)]
    [TestCase(SpaceInviteStatus.Revoked)]
    [TestCase(SpaceInviteStatus.Expired)]
    public void Reconstitute_WithValidSnapshot_ShouldRoundTripEveryField(SpaceInviteStatus status)
    {
        var original = InState(status, version: 8, useCount: 3, maxUses: 20);

        var restored = SpaceInvite.Reconstitute(
            original.Id, original.PublicId, original.SpaceId, original.CreatedByUserId, original.TokenHash,
            original.Status, original.UseCount, original.MaxUses, original.ExpiresAt, original.Version,
            original.CreatedAt, original.UpdatedAt, original.RevokedAt);

        Assert.That(Snapshot(restored), Is.EqualTo(Snapshot(original)));
        Assert.That(restored, Is.Not.SameAs(original));
        Assert.That(restored.GrantedRole, Is.EqualTo(SpaceRoleSystemKey.Viewer));
    }

    [Test]
    public void Reconstitute_ShouldPreserveStoredValuesAndAllowUnsavedId()
    {
        var invite = Restore(id: 0, useCount: 5, maxUses: 10);

        Assert.Multiple(() =>
        {
            Assert.That(invite.Id, Is.Zero);
            Assert.That(invite.UseCount, Is.EqualTo(5));
            Assert.That(invite.MaxUses, Is.EqualTo(10));
            Assert.That(invite.CreatedAt, Is.EqualTo(CreatedAt));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now));
        });
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<SpaceInvite> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithUndefinedStatus_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(status: (SpaceInviteStatus)99));
    }

    [Test]
    public void Reconstitute_WithNullTokenHash_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SpaceInvite.Reconstitute(
            42, Guid.CreateVersion7(), 7, 9, null!, SpaceInviteStatus.Active,
            0, null, ExpiresAt, 1, CreatedAt, Now, null));
    }

    [Test]
    public void RecordAcceptance_ShouldIncrementUseCountOnUnlimitedInvite()
    {
        var invite = Restore();

        var result = invite.RecordAcceptance(Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.UseCount, Is.EqualTo(1));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(invite.Version, Is.EqualTo(2));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
        });

        result = invite.RecordAcceptance(Now.AddMinutes(2));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.UseCount, Is.EqualTo(2));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
            Assert.That(invite.Version, Is.EqualTo(3));
        });
    }

    [Test]
    public void RecordAcceptance_WithMaxUses_ShouldExpireOnLastAllowedAcceptance()
    {
        var invite = Restore(maxUses: 2);

        var result = invite.RecordAcceptance(Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.UseCount, Is.EqualTo(1));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Active));
        });

        result = invite.RecordAcceptance(Now.AddMinutes(2));

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.UseCount, Is.EqualTo(2));
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Expired));
            Assert.That(invite.RevokedAt, Is.Null);
            Assert.That(invite.Version, Is.EqualTo(3));
            Assert.That(invite.UpdatedAt, Is.EqualTo(Now.AddMinutes(2)));
        });
    }

    [Test]
    public void RecordAcceptance_AfterLimitExpiry_ShouldReturnUsageExhaustedError()
    {
        var invite = InState(SpaceInviteStatus.Expired, useCount: 1, maxUses: 1);

        AssertFailureUnchanged(
            invite, () => invite.RecordAcceptance(Now), ErrorCodes.SpaceInvite.UsageExhausted);
    }

    [Test]
    public void CanAccept_BeforeExpiry_ShouldSucceedWithoutChangingSnapshot()
    {
        var invite = Restore();
        var before = Snapshot(invite);

        var result = invite.CanAccept(ExpiresAt.AddTicks(-1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Snapshot(invite), Is.EqualTo(before));
    }

    [Test]
    public void CanAccept_AtExpiryBoundary_ShouldFail()
    {
        var invite = Restore();

        AssertFailureUnchanged(invite, () => invite.CanAccept(ExpiresAt), ErrorCodes.SpaceInvite.Expired);
    }

    [Test]
    public void CanAccept_AfterExpiry_ShouldFail()
    {
        var invite = Restore();

        AssertFailureUnchanged(
            invite, () => invite.CanAccept(ExpiresAt.AddTicks(1)), ErrorCodes.SpaceInvite.Expired);
    }

    [Test]
    public void CanAccept_OnRevokedInvite_ShouldReturnRevokedError()
    {
        var invite = InState(SpaceInviteStatus.Revoked, useCount: 1, maxUses: 5);

        AssertFailureUnchanged(invite, () => invite.CanAccept(Now), ErrorCodes.SpaceInvite.Revoked);
    }

    [Test]
    public void CanAccept_OnRevokedInviteWithExhaustedUses_ShouldReturnRevokedErrorBeforeUsageError()
    {
        var invite = InState(SpaceInviteStatus.Revoked, useCount: 2, maxUses: 2);

        AssertFailureUnchanged(invite, () => invite.CanAccept(Now), ErrorCodes.SpaceInvite.Revoked);
    }

    [Test]
    public void CanAccept_OnLimitExpiredInvite_ShouldReturnUsageExhaustedErrorBeforeExpiryError()
    {
        var invite = InState(SpaceInviteStatus.Expired, useCount: 2, maxUses: 2);

        AssertFailureUnchanged(invite, () => invite.CanAccept(Now), ErrorCodes.SpaceInvite.UsageExhausted);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CanAccept_WithInvalidTime_ShouldFailWithoutChangingSnapshot(bool nonUtc)
    {
        var invite = Restore();
        var invalidTime = nonUtc ? Now.ToOffset(TimeSpan.FromHours(9)) : Now.AddTicks(-1);

        AssertFailureUnchanged(
            invite, () => invite.CanAccept(invalidTime), ErrorCodes.SpaceInvite.InvalidState);
    }

    [Test]
    public void Revoke_ShouldMarkRevokedAndRecordTimestamp()
    {
        var invite = Restore();
        var revokedAt = Now.AddMinutes(5);

        var result = invite.Revoke(revokedAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Revoked));
            Assert.That(invite.RevokedAt, Is.EqualTo(revokedAt));
            Assert.That(invite.Version, Is.EqualTo(2));
            Assert.That(invite.UpdatedAt, Is.EqualTo(revokedAt));
            Assert.That(invite.UseCount, Is.Zero);
        });
    }

    [Test]
    public void Expire_WithTimeReached_ShouldTransitionToExpired()
    {
        var invite = Restore();

        var result = invite.Expire(ExpiresAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Expired));
            Assert.That(invite.RevokedAt, Is.Null);
            Assert.That(invite.Version, Is.EqualTo(2));
            Assert.That(invite.UpdatedAt, Is.EqualTo(ExpiresAt));
        });
    }

    [Test]
    public void Expire_WithUsageLimitReached_ShouldTransitionToExpired()
    {
        var invite = Restore(maxUses: 1, useCount: 1);

        var result = invite.Expire(Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(invite.Status, Is.EqualTo(SpaceInviteStatus.Expired));
            Assert.That(invite.UseCount, Is.EqualTo(1));
            Assert.That(invite.Version, Is.EqualTo(2));
        });
    }

    [Test]
    public void Expire_BeforeConditionsMet_ShouldFailWithoutChangingSnapshot()
    {
        var invite = Restore();

        AssertFailureUnchanged(invite, () => invite.Expire(Now), ErrorCodes.SpaceInvite.InvalidState);
    }

    [TestCaseSource(nameof(TerminalMutations))]
    public void Mutation_OnTerminalInvite_ShouldFailWithoutChangingSnapshot(
        SpaceInviteStatus status, Func<SpaceInvite, Result> mutate, string expectedCode)
    {
        var invite = InState(status, useCount: 3, maxUses: 10);

        AssertFailureUnchanged(invite, () => mutate(invite), expectedCode);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Mutations_WithInvalidTime_ShouldFailWithoutChangingSnapshot(bool nonUtc)
    {
        var invalidTime = nonUtc ? Now.ToOffset(TimeSpan.FromHours(9)) : Now.AddTicks(-1);
        foreach (var mutation in Mutations())
        {
            var invite = Restore();
            AssertFailureUnchanged(
                invite, () => mutation(invite, invalidTime), ErrorCodes.SpaceInvite.InvalidState);
        }
    }

    [Test]
    public void Mutations_AtMaximumVersion_ShouldFailWithoutChangingSnapshot()
    {
        foreach (var mutation in Mutations())
        {
            var invite = Restore(version: long.MaxValue);
            AssertFailureUnchanged(
                invite, () => mutation(invite, Now), ErrorCodes.SpaceInvite.InvalidState);
        }
    }

    [Test]
    public void RecordAcceptance_AtMaximumUseCount_ShouldFailWithoutChangingSnapshot()
    {
        var invite = Restore(useCount: int.MaxValue);

        AssertFailureUnchanged(invite, () => invite.RecordAcceptance(Now), ErrorCodes.SpaceInvite.InvalidState);
    }

    private static IEnumerable<TestCaseData> NonFutureExpiries()
    {
        yield return new TestCaseData(Now).SetArgDisplayNames("expiry equals now");
        yield return new TestCaseData(Now.AddTicks(-1)).SetArgDisplayNames("expiry in the past");
    }

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return InvalidSnapshot("negative id", () => Restore(id: -1));
        yield return InvalidSnapshot("empty public id", () => Restore(publicId: Guid.Empty));
        yield return InvalidSnapshot("zero space id", () => Restore(spaceId: 0));
        yield return InvalidSnapshot("zero created by", () => Restore(createdByUserId: 0));
        yield return InvalidSnapshot("negative use count", () => Restore(useCount: -1));
        yield return InvalidSnapshot("zero max uses", () => Restore(maxUses: 0));
        yield return InvalidSnapshot("negative max uses", () => Restore(maxUses: -1));
        yield return InvalidSnapshot("use count above max uses", () => Restore(useCount: 2, maxUses: 1));
        yield return InvalidSnapshot("zero version", () => Restore(version: 0));
        yield return InvalidSnapshot("non UTC created time",
            () => Restore(createdAt: CreatedAt.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("non UTC expiry",
            () => Restore(expiresAt: ExpiresAt.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("expiry equal to created", () => Restore(expiresAt: CreatedAt));
        yield return InvalidSnapshot("expiry before created", () => Restore(expiresAt: CreatedAt.AddTicks(-1)));
        yield return InvalidSnapshot("non UTC updated time",
            () => Restore(updatedAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("updated before created", () => Restore(updatedAt: CreatedAt.AddTicks(-1)));
        yield return InvalidSnapshot("non UTC revoked time", () =>
            Restore(status: SpaceInviteStatus.Revoked,
                revokedAt: Now.AddHours(-1).ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("revoked before created", () =>
            Restore(status: SpaceInviteStatus.Revoked, revokedAt: CreatedAt.AddTicks(-1)));
        yield return InvalidSnapshot("revoked after updated", () =>
            Restore(status: SpaceInviteStatus.Revoked, revokedAt: Now.AddMinutes(1)));
        yield return InvalidSnapshot("active with revoked timestamp",
            () => Restore(revokedAt: Now.AddHours(-1)));
        yield return InvalidSnapshot("revoked without timestamp",
            () => Restore(status: SpaceInviteStatus.Revoked));
        yield return InvalidSnapshot("expired with revoked timestamp", () =>
            Restore(status: SpaceInviteStatus.Expired, revokedAt: Now.AddHours(-1)));
    }

    private static TestCaseData InvalidSnapshot(string name, Func<SpaceInvite> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> TerminalMutations()
    {
        yield return TerminalMutation("revoke on revoked", SpaceInviteStatus.Revoked,
            ErrorCodes.SpaceInvite.InvalidState,
            invite => invite.Revoke(Now));
        yield return TerminalMutation("revoke on expired", SpaceInviteStatus.Expired,
            ErrorCodes.SpaceInvite.InvalidState,
            invite => invite.Revoke(Now));
        yield return TerminalMutation("expire on revoked", SpaceInviteStatus.Revoked,
            ErrorCodes.SpaceInvite.InvalidState,
            invite => invite.Expire(Now));
        yield return TerminalMutation("expire on expired", SpaceInviteStatus.Expired,
            ErrorCodes.SpaceInvite.InvalidState,
            invite => invite.Expire(Now));
        yield return TerminalMutation("record acceptance on revoked", SpaceInviteStatus.Revoked,
            ErrorCodes.SpaceInvite.Revoked,
            invite => invite.RecordAcceptance(Now));
        yield return TerminalMutation("record acceptance on expired", SpaceInviteStatus.Expired,
            ErrorCodes.SpaceInvite.Expired,
            invite => invite.RecordAcceptance(Now));
    }

    private static TestCaseData TerminalMutation(
        string name,
        SpaceInviteStatus status,
        string expectedCode,
        Func<SpaceInvite, Result> apply) =>
        new TestCaseData(status, apply, expectedCode).SetArgDisplayNames(name);

    private static IEnumerable<Func<SpaceInvite, DateTimeOffset, Result>> Mutations()
    {
        yield return (invite, now) => invite.RecordAcceptance(now);
        yield return (invite, now) => invite.Revoke(now);
        yield return (invite, now) => invite.Expire(now);
    }

    private static SpaceInvite InState(
        SpaceInviteStatus status,
        long version = 1,
        int useCount = 0,
        int? maxUses = null) =>
        Restore(
            status: status,
            version: version,
            useCount: useCount,
            maxUses: maxUses,
            revokedAt: status == SpaceInviteStatus.Revoked ? Now.AddHours(-1) : null);

    private static SpaceInvite Restore(
        long id = 42,
        Guid? publicId = null,
        long spaceId = 7,
        long createdByUserId = 9,
        TokenHash? tokenHash = null,
        SpaceInviteStatus status = SpaceInviteStatus.Active,
        int useCount = 0,
        int? maxUses = null,
        DateTimeOffset? expiresAt = null,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? revokedAt = null) =>
        SpaceInvite.Reconstitute(
            id, publicId ?? Guid.CreateVersion7(), spaceId, createdByUserId,
            tokenHash ?? Hash(), status, useCount, maxUses,
            expiresAt ?? ExpiresAt, version,
            createdAt ?? CreatedAt, updatedAt ?? Now, revokedAt);

    private static TokenHash Hash() => TokenHash.Reconstitute("aW52aXRlLXRva2VuLWhhc2g");

    private static object?[] Snapshot(SpaceInvite invite) =>
    [
        invite.Id, invite.PublicId, invite.SpaceId, invite.CreatedByUserId, invite.TokenHash,
        invite.Status, invite.ExpiresAt, invite.UseCount, invite.MaxUses, invite.Version,
        invite.CreatedAt, invite.UpdatedAt, invite.RevokedAt
    ];

    private static void AssertFailureUnchanged(SpaceInvite invite, Func<Result> mutate, string expectedCode)
    {
        var before = Snapshot(invite);
        var result = mutate();

        AssertError(result, expectedCode);
        Assert.That(Snapshot(invite), Is.EqualTo(before), "Failed mutation must preserve every field.");
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }
}