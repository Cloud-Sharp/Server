using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.DeletionRequests;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.DeletionRequests;

[TestFixture]
public class RequestSpaceDeletionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);

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

        public RequestSpaceDeletionUseCase UseCase => new(
            SpaceRepository,
            UnitOfWork,
            TransactionExecutor,
            Clock);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldAcceptOwnerDeletion(SpaceStatus status)
    {
        // Arrange
        var space = StoredSpace(status);
        var harness = new Harness(space);
        var command = new RequestSpaceDeletionCommand(OwnerContext(space), space.Version);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var update = harness.SpaceRepository.UpdateCalls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(result.Value.Status, Is.EqualTo(SpaceStatus.Deleting));
            Assert.That(result.Value.AcceptedAt, Is.EqualTo(Now));
            Assert.That(result.Value.Version, Is.EqualTo(space.Version + 1));
            Assert.That(update.ExpectedVersion, Is.EqualTo(space.Version));
            Assert.That(update.Space.PublicId, Is.EqualTo(space.PublicId));
            Assert.That(update.Space.Status, Is.EqualTo(SpaceStatus.Deleting));
            Assert.That(update.Space.Version, Is.EqualTo(space.Version + 1));
            Assert.That(update.Space.UsedBytes, Is.EqualTo(400));
            Assert.That(update.Space.ReservedBytes, Is.EqualTo(50));
            Assert.That(update.Space.AllowedBytes, Is.EqualTo(1000));
            Assert.That(update.Space.DeletingAt, Is.EqualTo(Now));
            Assert.That(update.Space.ArchivedAt, Is.Null);
            Assert.That(update.Space.UpdatedAt, Is.EqualTo(Now));
            Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { space.Id }));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.Zero);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyTransferredOwnershipWithoutWrites()
    {
        // Arrange
        var authorizedSpace = StoredSpace(SpaceStatus.Active, ownerUserId: 21);
        var storedSpace = StoredSpace(SpaceStatus.Active, ownerUserId: 99);
        var harness = new Harness(storedSpace);
        var command = new RequestSpaceDeletionCommand(OwnerContext(authorizedSpace), storedSpace.Version);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertPermissionDenied(result);
        AssertNoWrites(harness);
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldDenyNonOwnerAdministratorWithoutWrites()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, ownerUserId: 21);
        var harness = new Harness(space);
        var administrator = TestSpaceAuthorizationSnapshots.Session(42, SystemRole.SystemAdmin);
        var context = Authorize(space, administrator);
        var command = new RequestSpaceDeletionCommand(context, space.Version);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(context.AuthorizationSource, Is.EqualTo(SpaceAuthorizationSource.SystemAdminBypass));
        AssertPermissionDenied(result);
        AssertNoWrites(harness);
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRejectStaleVersionBeforeStagingUpdate()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, version: 4);
        var harness = new Harness(space);
        var command = new RequestSpaceDeletionCommand(OwnerContext(space), space.Version - 1);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        AssertNoWrites(harness);
        Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnConcurrencyFailureAndRollback()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(space);
        harness.UnitOfWork.SaveChangesFailureFactory = _ => CommonError.PreconditionFailed();
        var command = new RequestSpaceDeletionCommand(OwnerContext(space), space.Version);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.PreconditionFailed));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.UpdateCalls, Has.Count.EqualTo(1));
            Assert.That(harness.UnitOfWork.SaveChangesCallCount, Is.EqualTo(1));
            Assert.That(harness.TransactionExecutor.CommitCount, Is.Zero);
            Assert.That(harness.TransactionExecutor.RollbackCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireDeletionContextWithoutDependencyCalls()
    {
        // Arrange
        var harness = new Harness();
        var command = new RequestSpaceDeletionCommand(null!, 1);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.AuthorizationContextRequired,
            nameof(RequestSpaceDeletionCommand.Space));
        AssertNoDependencyCalls(harness);
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task ExecuteAsync_ShouldRejectNonPositiveVersionWithoutDependencyCalls(long expectedVersion)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var harness = new Harness(space);
        var command = new RequestSpaceDeletionCommand(OwnerContext(space), expectedVersion);

        // Act
        var result = await harness.UseCase.ExecuteAsync(command);

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.ExpectedVersionInvalid,
            nameof(RequestSpaceDeletionCommand.ExpectedVersion));
        AssertNoDependencyCalls(harness);
    }

    private static Space StoredSpace(
        SpaceStatus status,
        long ownerUserId = 21,
        long version = 3) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId,
            allowedBytes: 1000,
            usedBytes: 400,
            reservedBytes: 50,
            version: version,
            updatedAt: Now.AddDays(-1));

    private static AuthorizedSpaceContext OwnerContext(Space space) =>
        Authorize(space, TestSpaceAuthorizationSnapshots.Session(space.OwnerUserId));

    private static AuthorizedSpaceContext Authorize(Space space, CurrentActor actor)
    {
        var query = TestSpaceAuthorizationSnapshots.Query(actor: actor, spacePublicId: space.PublicId);
        var snapshot = new SpaceAuthorizationSnapshot(space, null, []);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static void AssertPermissionDenied(Result<SpaceDeletionAcceptedDto> result)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpacePermissionDenied));
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

    private static void AssertValidationError<T>(
        Result<T> result,
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

    private static string FirstErrorCode<T>(Result<T> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
