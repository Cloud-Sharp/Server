using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.QuotaChanges;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.QuotaChanges;

[TestFixture]
public class ChangeSpaceQuotaUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 4, 40, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness(Space? storedSpace = null)
        {
            SpaceRepository = new FakeSpaceRepository();
            if (storedSpace is not null)
            {
                SpaceRepository.Seed(storedSpace);
            }

            UnitOfWork = new FakeSpaceUnitOfWork(SpaceRepository);
            TransactionExecutor = new FakeTransactionExecutor();
            Clock = new FakeClock(Now);
        }

        public FakeSpaceRepository SpaceRepository { get; }
        public FakeSpaceUnitOfWork UnitOfWork { get; }
        public FakeTransactionExecutor TransactionExecutor { get; }
        public FakeClock Clock { get; }

        public ChangeSpaceQuotaUseCase UseCase => new(
            SpaceRepository,
            UnitOfWork,
            TransactionExecutor,
            Clock);
    }

    private static IEnumerable<TestCaseData> QuotaChanges()
    {
        yield return new TestCaseData(100L, 60L, 20L, 10L, 30L)
            .SetName("Reduce finite quota with remaining allowance");
        yield return new TestCaseData(100L, 30L, 20L, 10L, 0L)
            .SetName("Reduce finite quota to occupied storage");
        yield return new TestCaseData(100L, null, 20L, 10L, null)
            .SetName("Change finite quota to unlimited");
        yield return new TestCaseData(null, 80L, 20L, 10L, 50L)
            .SetName("Change unlimited quota to finite");
        yield return new TestCaseData(100L, 0L, 0L, 0L, 0L)
            .SetName("Set zero quota on empty space");
    }

    [TestCaseSource(nameof(QuotaChanges))]
    public async Task ExecuteAsync_ShouldChangeQuota(
        long? initialAllowedBytes,
        long? requestedAllowedBytes,
        long usedBytes,
        long reservedBytes,
        long? expectedAvailableBytes)
    {
        // Arrange
        var space = StoredSpace(
            SpaceStatus.Active,
            initialAllowedBytes,
            usedBytes,
            reservedBytes);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, requestedAllowedBytes);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertSuccessfulWrite(harness, space, result, requestedAllowedBytes, expectedAvailableBytes);
    }

    [Test]
    public async Task ExecuteAsync_ShouldAllowSystemAdministratorWhoOwnsSpace()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, ownerUserId: 42);
        var administrator = TestSpaceAuthorizationSnapshots.Session(42, SystemRole.SystemAdmin);
        var context = Authorize(space, administrator);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
        AssertSuccessfulWrite(harness, space, result, requestedAllowedBytes: 60, expectedAvailableBytes: 30);
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyFormerOwnerAfterOwnershipTransfer()
    {
        // Arrange
        var authorizedSpace = StoredSpace(SpaceStatus.Active, ownerUserId: 21);
        var persistedSpace = StoredSpace(SpaceStatus.Active, ownerUserId: 99);
        var context = OwnerContext(authorizedSpace);
        var harness = new Harness(persistedSpace);
        var command = new ChangeSpaceQuotaCommand(context, persistedSpace.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.UserId, Is.Not.EqualTo(persistedSpace.OwnerUserId));
        AssertPermissionDenied(result);
        AssertFailedWrite(harness, persistedSpace.Id);
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyNonOwnerSystemAdministrator()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, ownerUserId: 21);
        var administrator = TestSpaceAuthorizationSnapshots.Session(42, SystemRole.SystemAdmin);
        var context = Authorize(space, administrator);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
        AssertPermissionDenied(result);
        AssertFailedWrite(harness, space.Id);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = OwnerContext(space);
        var harness = new Harness();
        var command = new ChangeSpaceQuotaCommand(context, space.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpaceNotFound));
        AssertFailedWrite(harness, space.Id);
    }

    [TestCase(SpaceStatus.Archived)]
    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedState(SpaceStatus persistedStatus)
    {
        // Arrange
        var authorizedSpace = StoredSpace(SpaceStatus.Active);
        var persistedSpace = StoredSpace(persistedStatus);
        var context = OwnerContext(authorizedSpace);
        var harness = new Harness(persistedSpace);
        var command = new ChangeSpaceQuotaCommand(context, persistedSpace.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertStateNotAllowed(result, persistedStatus);
        AssertFailedWrite(harness, persistedSpace.Id);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersionBeforeStagingUpdate()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, version: 4);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version - 1, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        AssertFailedWrite(harness, space.Id);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnConcurrencyFailureAndRollback()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        var command = new ChangeSpaceQuotaCommand(context, space.Version, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.UpdateCalls.Single().ExpectedVersion, Is.EqualTo(space.Version));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [TestCase(100L)]
    [TestCase(null)]
    public async Task ExecuteAsync_ShouldPreserveVersionForEquivalentQuota(long? allowedBytes)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, allowedBytes, version: 4);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, allowedBytes);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var persistedSpace = harness.SpaceRepository.PersistedSpacesById[space.Id];
        var expectedAvailableBytes = allowedBytes is null ? null : allowedBytes - 30;
        Assert.Multiple(() =>
        {
            Assert.That(persistedSpace.Version, Is.EqualTo(space.Version));
            Assert.That(persistedSpace.UpdatedAt, Is.EqualTo(space.UpdatedAt));
            Assert.That(result.Value.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(result.Value.AllowedBytes, Is.EqualTo(allowedBytes));
            Assert.That(result.Value.UsedBytes, Is.EqualTo(20L));
            Assert.That(result.Value.ReservedBytes, Is.EqualTo(10L));
            Assert.That(result.Value.AvailableBytes, Is.EqualTo(expectedAvailableBytes));
            Assert.That(result.Value.Version, Is.EqualTo(space.Version));
            Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleNoOpBeforeStagingUpdate()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, allowedBytes: 100, version: 4);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version - 1, 100);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        AssertFailedWrite(harness, space.Id);
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectQuotaBelowOccupiedStorage()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, allowedBytes: 100);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, 29);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Space.QuotaTooSmall));
        AssertFailedWrite(harness, space.Id);
        Assert.That(harness.SpaceRepository.PersistedSpacesById[space.Id].AllowedBytes, Is.EqualTo(100L));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var harness = new Harness();
        var command = new ChangeSpaceQuotaCommand(null!, 1, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.AuthorizationContextRequired,
            nameof(ChangeSpaceQuotaCommand.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldReturnVersionValidationErrorWithoutDependencyCalls(
        long expectedVersion)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, expectedVersion, 60);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(ChangeSpaceQuotaCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnQuotaValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = OwnerContext(space);
        var harness = new Harness(space);
        var command = new ChangeSpaceQuotaCommand(context, space.Version, -1);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.QuotaInvalid,
            nameof(ChangeSpaceQuotaCommand.AllowedBytes));
        AssertNoDependencyCalls(harness);
    }

    private static Space StoredSpace(
        SpaceStatus status,
        long? allowedBytes = 100,
        long usedBytes = 20,
        long reservedBytes = 10,
        long ownerUserId = 21,
        long version = 3) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId,
            allowedBytes: allowedBytes,
            usedBytes: status == SpaceStatus.Deleted ? 0 : usedBytes,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : reservedBytes,
            version: version,
            createdAt: Now.AddDays(-7),
            updatedAt: Now.AddDays(-1));

    private static AuthorizedSpaceContext OwnerContext(Space space) =>
        Authorize(space, TestSpaceAuthorizationSnapshots.Session(space.OwnerUserId));

    private static AuthorizedSpaceContext Authorize(Space space, CurrentActor actor)
    {
        var query = TestSpaceAuthorizationSnapshots.Query(actor: actor, spacePublicId: space.PublicId);
        var snapshot = new SpaceAuthorizationSnapshot(space, null, []);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static void AssertSuccessfulWrite(
        Harness harness,
        Space originalSpace,
        Result<SpaceQuotaDto> result,
        long? requestedAllowedBytes,
        long? expectedAvailableBytes)
    {
        Assert.That(result.IsSuccess, Is.True);
        var update = harness.SpaceRepository.UpdateCalls.Single();
        var persistedSpace = harness.SpaceRepository.PersistedSpacesById[originalSpace.Id];
        var dto = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(update.ExpectedVersion, Is.EqualTo(originalSpace.Version));
            Assert.That(update.Space.PublicId, Is.EqualTo(originalSpace.PublicId));
            Assert.That(update.Space.AllowedBytes, Is.EqualTo(requestedAllowedBytes));
            Assert.That(update.Space.UsedBytes, Is.EqualTo(originalSpace.UsedBytes));
            Assert.That(update.Space.ReservedBytes, Is.EqualTo(originalSpace.ReservedBytes));
            Assert.That(update.Space.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(update.Space.UpdatedAt, Is.EqualTo(Now));
            Assert.That(persistedSpace.AllowedBytes, Is.EqualTo(requestedAllowedBytes));
            Assert.That(persistedSpace.UsedBytes, Is.EqualTo(originalSpace.UsedBytes));
            Assert.That(persistedSpace.ReservedBytes, Is.EqualTo(originalSpace.ReservedBytes));
            Assert.That(persistedSpace.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(persistedSpace.UpdatedAt, Is.EqualTo(Now));
            Assert.That(dto.SpacePublicId, Is.EqualTo(originalSpace.PublicId));
            Assert.That(dto.AllowedBytes, Is.EqualTo(requestedAllowedBytes));
            Assert.That(dto.UsedBytes, Is.EqualTo(originalSpace.UsedBytes));
            Assert.That(dto.ReservedBytes, Is.EqualTo(originalSpace.ReservedBytes));
            Assert.That(dto.AvailableBytes, Is.EqualTo(expectedAvailableBytes));
            Assert.That(dto.Version, Is.EqualTo(originalSpace.Version + 1));
            Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { originalSpace.Id }));
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertPermissionDenied(Result<SpaceQuotaDto> result)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpacePermissionDenied));
    }

    private static void AssertStateNotAllowed(Result<SpaceQuotaDto> result, SpaceStatus expectedStatus)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                error.Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Auth.SpaceStateNotAllowed));
            Assert.That(error, Is.InstanceOf<SpaceAuthorizationError>());
            Assert.That(((SpaceAuthorizationError)error).ActualSpaceStatus, Is.EqualTo(expectedStatus));
        });
    }

    private static void AssertFailedWrite(Harness harness, long spaceId)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { spaceId }));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
        });
    }

    private static void AssertNoDependencyCalls(Harness harness)
    {
        AssertNoWrites(harness);
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedPublicIds, Is.Empty);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    private static void AssertValidationError(
        Result<SpaceQuotaDto> result,
        string expectedErrorCode,
        string expectedPropertyName)
    {
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], Is.EqualTo(expectedErrorCode));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(expectedPropertyName));
        });
    }

    private static string FirstErrorCode(Result<SpaceQuotaDto> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
