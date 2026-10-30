using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.Domain.Spaces;

[TestFixture]
public class SpaceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 22, 3, 30, 0, TimeSpan.Zero);

    [Test]
    public void Create_WithValidInputs_ShouldCreateActiveNamespaceWithDefaults()
    {
        var result = Space.Create(7, "  Engineering  ", "  Team files  ", 100, Now);

        Assert.That(result.IsSuccess, Is.True);
        var space = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(space.Id, Is.Zero);
            Assert.That(space.PublicId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(space.PublicId.ToString("D")[14], Is.EqualTo('7'));
            Assert.That(space.OwnerUserId, Is.EqualTo(7));
            Assert.That(space.Name, Is.EqualTo("Engineering"));
            Assert.That(space.Description, Is.EqualTo("Team files"));
            Assert.That(space.Status, Is.EqualTo(SpaceStatus.Active));
            Assert.That(space.AllowedBytes, Is.EqualTo(100));
            Assert.That(space.UsedBytes, Is.Zero);
            Assert.That(space.ReservedBytes, Is.Zero);
            Assert.That(space.AvailableBytes, Is.EqualTo(100));
            Assert.That(space.Version, Is.EqualTo(1));
            Assert.That(space.CreatedAt, Is.EqualTo(Now));
            Assert.That(space.UpdatedAt, Is.EqualTo(Now));
            Assert.That(space.ArchivedAt, Is.Null);
            Assert.That(space.DeletingAt, Is.Null);
            Assert.That(space.DeletedAt, Is.Null);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t\n ")]
    [TestCase(201)]
    public void Create_WithInvalidName_ShouldFail(object? input)
    {
        var name = input is int length ? new string('n', length) : (string?)input;

        var result = Space.Create(7, name!, null, 100, Now);

        AssertError(result, ErrorCodes.Space.NameInvalid);
    }

    [TestCase(null)]
    [TestCase(" \t ")]
    public void Create_WithBlankDescription_ShouldStoreNull(string? description)
    {
        var result = Space.Create(7, "Engineering", description, 0, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Description, Is.Null);
        Assert.That(result.Value.AvailableBytes, Is.Zero);
    }

    [Test]
    public void Create_WithMaximumNameAndLongDescription_ShouldSucceed()
    {
        var name = new string('n', 200);
        var description = new string('d', 5000);

        var result = Space.Create(7, $" {name} ", description, null, Now);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Name, Is.EqualTo(name));
        Assert.That(result.Value.Description, Is.EqualTo(description));
        Assert.That(result.Value.AvailableBytes, Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Create_WithInvalidOwner_ShouldFail(long ownerUserId)
    {
        AssertError(Space.Create(ownerUserId, "Engineering", null, 100, Now), ErrorCodes.Space.OwnerInvalid);
    }

    [Test]
    public void Create_WithNegativeQuota_ShouldFail()
    {
        AssertError(Space.Create(7, "Engineering", null, -1, Now), ErrorCodes.Space.QuotaInvalid);
    }

    [Test]
    public void Create_WithNonUtcTime_ShouldFail()
    {
        AssertError(
            Space.Create(7, "Engineering", null, 100, Now.ToOffset(TimeSpan.FromHours(9))),
            ErrorCodes.Space.InvalidState);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public void Reconstitute_WithValidSnapshot_ShouldRoundTripEveryField(SpaceStatus status)
    {
        var original = InState(status, usedBytes: 10, reservedBytes: 20, version: 8);

        var restored = Space.Reconstitute(
            original.Id, original.PublicId, original.OwnerUserId, original.Name, original.Description,
            original.Status, original.AllowedBytes, original.UsedBytes, original.ReservedBytes,
            original.Version, original.CreatedAt, original.UpdatedAt,
            original.ArchivedAt, original.DeletingAt, original.DeletedAt);

        Assert.That(Snapshot(restored), Is.EqualTo(Snapshot(original)));
        Assert.That(restored.AvailableBytes, Is.EqualTo(status == SpaceStatus.Deleted ? 100 : 70));
        Assert.That(restored, Is.Not.SameAs(original));
    }

    [Test]
    public void Reconstitute_ShouldPreserveStoredTextAndAllowUnsavedId()
    {
        var space = Restore(id: 0, name: " Engineering ", description: "  Stored text  ");

        Assert.That(space.Id, Is.Zero);
        Assert.That(space.Name, Is.EqualTo(" Engineering "));
        Assert.That(space.Description, Is.EqualTo("  Stored text  "));
    }

    [TestCaseSource(nameof(InvalidSnapshots))]
    public void Reconstitute_WithInvalidSnapshot_ShouldThrow(Func<Space> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Reconstitute_WithUndefinedStatus_ShouldThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Restore(status: (SpaceStatus)99));
    }

    [TestCaseSource(nameof(InvalidLifecycleSnapshots))]
    public void Reconstitute_WithInvalidLifecycleTimestamps_ShouldThrow(Func<Space> restore)
    {
        Assert.That(() => { restore(); }, Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void UpdateMetadata_ShouldNormalizeTextAndBumpVersionOnce()
    {
        var space = Restore();

        var result = space.UpdateMetadata("  Design  ", " \t ", Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(space.Name, Is.EqualTo("Design"));
        Assert.That(space.Description, Is.Null);
        Assert.That(space.Version, Is.EqualTo(2));
        Assert.That(space.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
        Assert.That(space.CreatedAt, Is.EqualTo(Now.AddDays(-1)));
    }

    [TestCase("metadata")]
    [TestCase("quota")]
    [TestCase("owner")]
    public void UnchangedUpdate_AtMaximumVersion_ShouldSucceedWithoutChangingSnapshot(string operation)
    {
        var space = Restore(version: long.MaxValue);
        var before = Snapshot(space);

        var result = operation switch
        {
            "metadata" => space.UpdateMetadata("  Engineering ", " Team files ", Now.AddMinutes(1)),
            "quota" => space.ChangeQuota(100, Now.AddMinutes(1)),
            "owner" => space.TransferOwnership(7, Now.AddMinutes(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Snapshot(space), Is.EqualTo(before));
    }

    [Test]
    public void StorageReservationFlow_ShouldPreserveCapacityAccounting()
    {
        var space = Restore(allowedBytes: 100);

        Assert.That(space.ReserveStorage(80, Now).IsSuccess, Is.True);
        Assert.That(space.ReservedBytes, Is.EqualTo(80));
        Assert.That(space.AvailableBytes, Is.EqualTo(20));

        Assert.That(space.ConsumeReservation(50, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(space.UsedBytes, Is.EqualTo(50));
        Assert.That(space.ReservedBytes, Is.EqualTo(30));
        Assert.That(space.AvailableBytes, Is.EqualTo(20));

        Assert.That(space.ReleaseReservation(30, Now.AddMinutes(2)).IsSuccess, Is.True);
        Assert.That(space.ReleaseUsedStorage(50, Now.AddMinutes(3)).IsSuccess, Is.True);
        Assert.That(space.UsedBytes, Is.Zero);
        Assert.That(space.ReservedBytes, Is.Zero);
        Assert.That(space.AvailableBytes, Is.EqualTo(100));
        Assert.That(space.Version, Is.EqualTo(5));
        Assert.That(space.UpdatedAt, Is.EqualTo(Now.AddMinutes(3)));
    }

    [Test]
    public void ChangeQuota_ShouldAllowExactlyCommittedCapacityAndUnlimitedQuota()
    {
        var space = Restore(usedBytes: 40, reservedBytes: 30);

        Assert.That(space.ChangeQuota(70, Now).IsSuccess, Is.True);
        Assert.That(space.AvailableBytes, Is.Zero);
        Assert.That(space.ChangeQuota(null, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(space.AllowedBytes, Is.Null);
        Assert.That(space.AvailableBytes, Is.Null);
        Assert.That(space.Version, Is.EqualTo(3));
    }

    [Test]
    public void ArchiveThenReactivate_ShouldMaintainLifecycleTimestamps()
    {
        var space = Restore(usedBytes: 40, reservedBytes: 30);
        var archivedAt = Now.AddMinutes(1);
        var reactivatedAt = Now.AddMinutes(2);

        Assert.That(space.Archive(archivedAt).IsSuccess, Is.True);
        Assert.That(space.Status, Is.EqualTo(SpaceStatus.Archived));
        Assert.That(space.ArchivedAt, Is.EqualTo(archivedAt));
        Assert.That(space.Version, Is.EqualTo(2));
        Assert.That(space.Reactivate(reactivatedAt).IsSuccess, Is.True);
        Assert.That(space.Status, Is.EqualTo(SpaceStatus.Active));
        Assert.That(space.ArchivedAt, Is.Null);
        Assert.That(space.DeletingAt, Is.Null);
        Assert.That(space.DeletedAt, Is.Null);
        Assert.That(space.UsedBytes, Is.EqualTo(40));
        Assert.That(space.ReservedBytes, Is.EqualTo(30));
        Assert.That(space.Version, Is.EqualTo(3));
        Assert.That(space.UpdatedAt, Is.EqualTo(reactivatedAt));
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public void BeginDelete_ShouldKeepStorageCountersAndClearArchivedTimestamp(SpaceStatus status)
    {
        var space = InState(status, usedBytes: 40, reservedBytes: 30);
        var deletingAt = Now.AddMinutes(1);

        var result = space.BeginDelete(deletingAt);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(space.Status, Is.EqualTo(SpaceStatus.Deleting));
        Assert.That(space.ArchivedAt, Is.Null);
        Assert.That(space.DeletingAt, Is.EqualTo(deletingAt));
        Assert.That(space.DeletedAt, Is.Null);
        Assert.That(space.UsedBytes, Is.EqualTo(40));
        Assert.That(space.ReservedBytes, Is.EqualTo(30));
        Assert.That(space.Version, Is.EqualTo(2));
        Assert.That(space.UpdatedAt, Is.EqualTo(deletingAt));
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    public void Cleanup_ShouldReleaseStorageUntilDeletionCompletes(SpaceStatus status)
    {
        var space = InState(status, usedBytes: 10, reservedBytes: 20);
        var archivedAt = space.ArchivedAt;
        var deletingAt = space.DeletingAt;

        Assert.That(space.ReleaseReservation(5, Now.AddMinutes(1)).IsSuccess, Is.True);
        Assert.That(space.ReleaseUsedStorage(5, Now.AddMinutes(2)).IsSuccess, Is.True);
        Assert.That(space.UsedBytes, Is.EqualTo(5));
        Assert.That(space.ReservedBytes, Is.EqualTo(15));
        Assert.That(space.Status, Is.EqualTo(status));
        Assert.That(space.ArchivedAt, Is.EqualTo(archivedAt));
        Assert.That(space.DeletingAt, Is.EqualTo(deletingAt));
        Assert.That(space.Version, Is.EqualTo(3));
    }

    [Test]
    public void CompleteDelete_AfterStorageCleanup_ShouldRetainDeletionHistory()
    {
        var space = InState(SpaceStatus.Deleting, usedBytes: 10, reservedBytes: 20);
        var deletingAt = space.DeletingAt;

        AssertFailureUnchanged(space, () => space.CompleteDelete(Now), ErrorCodes.Space.InvalidState);
        Assert.That(space.ReleaseUsedStorage(10, Now).IsSuccess, Is.True);
        AssertFailureUnchanged(space, () => space.CompleteDelete(Now), ErrorCodes.Space.InvalidState);
        Assert.That(space.ReleaseReservation(20, Now).IsSuccess, Is.True);

        var deletedAt = Now.AddMinutes(1);
        Assert.That(space.CompleteDelete(deletedAt).IsSuccess, Is.True);
        Assert.That(space.Status, Is.EqualTo(SpaceStatus.Deleted));
        Assert.That(space.DeletingAt, Is.EqualTo(deletingAt));
        Assert.That(space.DeletedAt, Is.EqualTo(deletedAt));
        Assert.That(space.UpdatedAt, Is.EqualTo(deletedAt));
        Assert.That(space.Version, Is.EqualTo(4));
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public void ActiveOperations_OnInactiveSpace_ShouldFailWithoutChangingSnapshot(SpaceStatus status)
    {
        foreach (var mutation in Mutations().Where(m => m.RequiresActive))
        {
            var space = InState(status, usedBytes: 10, reservedBytes: 20);
            AssertFailureUnchanged(space, () => mutation.Apply(space, Now), ErrorCodes.Space.Inactive);
        }
    }

    [Test]
    public void Cleanup_OnDeletedSpace_ShouldFailWithoutChangingSnapshot()
    {
        var space = InState(SpaceStatus.Deleted, usedBytes: 10, reservedBytes: 20);

        AssertFailureUnchanged(space, () => space.ReleaseReservation(1, Now), ErrorCodes.Space.Inactive);
        AssertFailureUnchanged(space, () => space.ReleaseUsedStorage(1, Now), ErrorCodes.Space.Inactive);
    }

    [TestCase("archive", SpaceStatus.Archived)]
    [TestCase("archive", SpaceStatus.Deleting)]
    [TestCase("reactivate", SpaceStatus.Active)]
    [TestCase("reactivate", SpaceStatus.Deleted)]
    [TestCase("beginDelete", SpaceStatus.Deleting)]
    [TestCase("beginDelete", SpaceStatus.Deleted)]
    [TestCase("completeDelete", SpaceStatus.Active)]
    public void LifecycleOperation_InInvalidState_ShouldFailWithoutChangingSnapshot(string operation, SpaceStatus status)
    {
        var space = InState(status);
        var mutation = Mutations().Single(m => m.Name == operation);

        AssertFailureUnchanged(space, () => mutation.Apply(space, Now), ErrorCodes.Space.InvalidState);
    }

    [TestCaseSource(nameof(InvalidChanges))]
    public void Mutation_WithInvalidInput_ShouldFailWithoutChangingSnapshot(
        Func<Space, Result> mutate, string expectedCode)
    {
        var space = Restore(usedBytes: 10, reservedBytes: 20);

        AssertFailureUnchanged(space, () => mutate(space), expectedCode);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Mutations_WithInvalidTime_ShouldFailWithoutChangingSnapshot(bool nonUtc)
    {
        var invalidTime = nonUtc ? Now.ToOffset(TimeSpan.FromHours(9)) : Now.AddTicks(-1);
        foreach (var mutation in Mutations())
        {
            var space = ForMutation(mutation);
            AssertFailureUnchanged(space, () => mutation.Apply(space, invalidTime), ErrorCodes.Space.InvalidState);
        }
    }

    [Test]
    public void Mutations_AtMaximumVersion_ShouldFailWithoutChangingSnapshot()
    {
        foreach (var mutation in Mutations())
        {
            var space = ForMutation(mutation, long.MaxValue);
            AssertFailureUnchanged(space, () => mutation.Apply(space, Now), ErrorCodes.Space.InvalidState);
        }
    }

    [Test]
    public void ReserveStorage_WithUnlimitedQuota_ShouldRejectCounterOverflow()
    {
        var space = Restore(allowedBytes: null, usedBytes: long.MaxValue - 5, reservedBytes: 4);

        Assert.That(space.ReserveStorage(1, Now).IsSuccess, Is.True);
        Assert.That(space.ReservedBytes, Is.EqualTo(5));
        Assert.That(space.AvailableBytes, Is.Null);
        AssertFailureUnchanged(space, () => space.ReserveStorage(1, Now), ErrorCodes.Space.QuotaExceeded);
    }

    [Test]
    public void TransferOwnership_ShouldChangeOwnerAndVersionWithoutChangingNamespace()
    {
        var space = Restore(usedBytes: 10, reservedBytes: 20);
        var publicId = space.PublicId;

        var result = space.TransferOwnership(21, Now.AddMinutes(1));

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(space.OwnerUserId, Is.EqualTo(21));
        Assert.That(space.PublicId, Is.EqualTo(publicId));
        Assert.That(space.Name, Is.EqualTo("Engineering"));
        Assert.That(space.UsedBytes, Is.EqualTo(10));
        Assert.That(space.ReservedBytes, Is.EqualTo(20));
        Assert.That(space.Version, Is.EqualTo(2));
        Assert.That(space.UpdatedAt, Is.EqualTo(Now.AddMinutes(1)));
    }

    private static IEnumerable<TestCaseData> InvalidSnapshots()
    {
        yield return InvalidSnapshot("negative id", () => Restore(id: -1));
        yield return InvalidSnapshot("empty public id", () => Restore(publicId: Guid.Empty));
        yield return InvalidSnapshot("invalid owner", () => Restore(ownerUserId: 0));
        yield return InvalidSnapshot("blank name", () => Restore(name: "   "));
        yield return InvalidSnapshot("negative quota", () => Restore(allowedBytes: -1));
        yield return InvalidSnapshot("negative usage", () => Restore(usedBytes: -1));
        yield return InvalidSnapshot("negative reservation", () => Restore(reservedBytes: -1));
        yield return InvalidSnapshot("zero version", () => Restore(version: 0));
        yield return InvalidSnapshot("counter overflow", () => Restore(allowedBytes: null, usedBytes: long.MaxValue, reservedBytes: 1));
        yield return InvalidSnapshot("quota exceeded", () => Restore(usedBytes: 60, reservedBytes: 41));
        yield return InvalidSnapshot("non UTC created time", () => Restore(createdAt: Now.ToOffset(TimeSpan.FromHours(9))));
        yield return InvalidSnapshot("updated before created", () => Restore(createdAt: Now.AddMinutes(1)));
    }

    private static IEnumerable<TestCaseData> InvalidLifecycleSnapshots()
    {
        yield return InvalidSnapshot("active with archived timestamp", () => Restore(archivedAt: Now));
        yield return InvalidSnapshot("archived without timestamp", () => Restore(status: SpaceStatus.Archived));
        yield return InvalidSnapshot("deleting with archived timestamp", () => Restore(status: SpaceStatus.Deleting, archivedAt: Now, deletingAt: Now));
        yield return InvalidSnapshot("deleted without deleting timestamp", () => Restore(status: SpaceStatus.Deleted, deletedAt: Now));
        yield return InvalidSnapshot("state timestamp before creation", () => Restore(status: SpaceStatus.Archived, archivedAt: Now.AddDays(-2)));
        yield return InvalidSnapshot("state timestamp after update", () => Restore(status: SpaceStatus.Archived, archivedAt: Now.AddMinutes(1)));
        yield return InvalidSnapshot("deleted before deleting", () => Restore(status: SpaceStatus.Deleted, deletingAt: Now, deletedAt: Now.AddMinutes(-1)));
        yield return InvalidSnapshot("deleted with remaining storage", () => Restore(status: SpaceStatus.Deleted, usedBytes: 1, deletingAt: Now, deletedAt: Now));
    }

    private static TestCaseData InvalidSnapshot(string name, Func<Space> restore) =>
        new TestCaseData(restore).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> InvalidChanges()
    {
        yield return InvalidChange("reserve zero", s => s.ReserveStorage(0, Now), ErrorCodes.Space.StorageAmountInvalid);
        yield return InvalidChange("release negative reservation", s => s.ReleaseReservation(-1, Now), ErrorCodes.Space.StorageAmountInvalid);
        yield return InvalidChange("consume zero", s => s.ConsumeReservation(0, Now), ErrorCodes.Space.StorageAmountInvalid);
        yield return InvalidChange("release zero usage", s => s.ReleaseUsedStorage(0, Now), ErrorCodes.Space.StorageAmountInvalid);
        yield return InvalidChange("reserve beyond quota", s => s.ReserveStorage(71, Now), ErrorCodes.Space.QuotaExceeded);
        yield return InvalidChange("release beyond reservation", s => s.ReleaseReservation(21, Now), ErrorCodes.Space.ReservationInvalid);
        yield return InvalidChange("consume beyond reservation", s => s.ConsumeReservation(21, Now), ErrorCodes.Space.ReservationInvalid);
        yield return InvalidChange("release beyond usage", s => s.ReleaseUsedStorage(11, Now), ErrorCodes.Space.StorageAmountInvalid);
        yield return InvalidChange("quota below usage and reservation", s => s.ChangeQuota(29, Now), ErrorCodes.Space.QuotaTooSmall);
        yield return InvalidChange("negative quota", s => s.ChangeQuota(-1, Now), ErrorCodes.Space.QuotaInvalid);
        yield return InvalidChange("invalid transfer target", s => s.TransferOwnership(0, Now), ErrorCodes.Space.TransferTargetInvalid);
        yield return InvalidChange("blank metadata name", s => s.UpdateMetadata(" ", "new description", Now), ErrorCodes.Space.NameInvalid);
    }

    private static TestCaseData InvalidChange(string name, Func<Space, Result> mutate, string code) =>
        new TestCaseData(mutate, code).SetArgDisplayNames(name, code);

    private static IEnumerable<Mutation> Mutations()
    {
        yield return new Mutation("metadata", SpaceStatus.Active, true, (s, now) => s.UpdateMetadata("Design", null, now));
        yield return new Mutation("quota", SpaceStatus.Active, true, (s, now) => s.ChangeQuota(200, now));
        yield return new Mutation("reserve", SpaceStatus.Active, true, (s, now) => s.ReserveStorage(1, now));
        yield return new Mutation("releaseReservation", SpaceStatus.Active, false, (s, now) => s.ReleaseReservation(1, now));
        yield return new Mutation("consume", SpaceStatus.Active, true, (s, now) => s.ConsumeReservation(1, now));
        yield return new Mutation("releaseUsed", SpaceStatus.Active, false, (s, now) => s.ReleaseUsedStorage(1, now));
        yield return new Mutation("archive", SpaceStatus.Active, false, (s, now) => s.Archive(now));
        yield return new Mutation("reactivate", SpaceStatus.Archived, false, (s, now) => s.Reactivate(now));
        yield return new Mutation("beginDelete", SpaceStatus.Active, false, (s, now) => s.BeginDelete(now));
        yield return new Mutation("completeDelete", SpaceStatus.Deleting, false, (s, now) => s.CompleteDelete(now));
        yield return new Mutation("owner", SpaceStatus.Active, true, (s, now) => s.TransferOwnership(21, now));
    }

    private static Space ForMutation(Mutation mutation, long version = 1) =>
        InState(mutation.Status,
            usedBytes: mutation.Name == "completeDelete" ? 0 : 10,
            reservedBytes: mutation.Name == "completeDelete" ? 0 : 20,
            version: version);

    private static Space InState(SpaceStatus status, long usedBytes = 0, long reservedBytes = 0, long version = 1) =>
        Restore(
            status: status,
            usedBytes: status == SpaceStatus.Deleted ? 0 : usedBytes,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : reservedBytes,
            version: version,
            archivedAt: status == SpaceStatus.Archived ? Now.AddHours(-1) : null,
            deletingAt: status is SpaceStatus.Deleting or SpaceStatus.Deleted ? Now.AddHours(-1) : null,
            deletedAt: status == SpaceStatus.Deleted ? Now : null);

    private static Space Restore(
        long id = 42,
        Guid? publicId = null,
        long ownerUserId = 7,
        string name = "Engineering",
        string? description = "Team files",
        SpaceStatus status = SpaceStatus.Active,
        long? allowedBytes = 100,
        long usedBytes = 0,
        long reservedBytes = 0,
        long version = 1,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        DateTimeOffset? archivedAt = null,
        DateTimeOffset? deletingAt = null,
        DateTimeOffset? deletedAt = null) =>
        Space.Reconstitute(
            id, publicId ?? Guid.CreateVersion7(), ownerUserId, name, description, status,
            allowedBytes, usedBytes, reservedBytes, version, createdAt ?? Now.AddDays(-1),
            updatedAt ?? Now, archivedAt, deletingAt, deletedAt);

    private static object?[] Snapshot(Space space) =>
    [
        space.Id, space.PublicId, space.OwnerUserId, space.Name, space.Description,
        space.Status, space.AllowedBytes, space.UsedBytes, space.ReservedBytes,
        space.AvailableBytes, space.Version, space.CreatedAt, space.UpdatedAt,
        space.ArchivedAt, space.DeletingAt, space.DeletedAt
    ];

    private static void AssertFailureUnchanged(Space space, Func<Result> mutate, string expectedCode)
    {
        var before = Snapshot(space);
        var result = mutate();

        AssertError(result, expectedCode);
        Assert.That(Snapshot(space), Is.EqualTo(before), "Failed mutation must preserve every field.");
    }

    private static void AssertError(ResultBase result, string expectedCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.Any(error =>
            error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
            && code is string actual && actual == expectedCode), Is.True,
            $"Expected error code {expectedCode} in metadata.");
    }

    private sealed record Mutation(
        string Name, SpaceStatus Status, bool RequiresActive, Func<Space, DateTimeOffset, Result> Apply);
}
