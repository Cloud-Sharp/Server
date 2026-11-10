using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.QuotaReads;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.QuotaReads;

[TestFixture]
public class GetSpaceQuotaUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 4, 30, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public Harness(Space? storedSpace = null)
        {
            SpaceRepository = new FakeSpaceRepository();
            if (storedSpace is not null)
            {
                SpaceRepository.Seed(storedSpace);
            }
        }

        public FakeSpaceRepository SpaceRepository { get; }

        public GetSpaceQuotaUseCase UseCase => new(SpaceRepository);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldReturnPersistedQuotaForSession(SpaceStatus status)
    {
        // Arrange
        var authorizedSpace = StoredSpace(status, allowedBytes: 50, version: 1);
        var context = MemberContext(authorizedSpace);
        var persistedSpace = StoredSpace(status, allowedBytes: 100, version: 9);
        var harness = new Harness(persistedSpace);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        Assert.That(context.EffectivePermissions, Does.Contain(SpacePermission.SpaceView));
        Assert.That(result.Value.Version, Is.Not.EqualTo(authorizedSpace.Version));
        AssertSuccessfulRead(harness, context, persistedSpace, result, expectedAvailableBytes: 70);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnQuotaForAuthorizedMcpContext()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, allowedBytes: 100, version: 6);
        var context = McpContext(space);
        var harness = new Harness(space);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        Assert.That(context.ActorType, Is.EqualTo(ActorType.Mcp));
        AssertSuccessfulRead(harness, context, space, result, expectedAvailableBytes: 70);
    }

    [TestCase(100L, 70L)]
    [TestCase(30L, 0L)]
    [TestCase(null, null)]
    public async Task ExecuteAsync_ShouldCalculateAvailableBytes(
        long? allowedBytes,
        long? expectedAvailableBytes)
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active, allowedBytes, version: 4);
        var context = MemberContext(space);
        var harness = new Harness(space);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        AssertSuccessfulRead(harness, context, space, result, expectedAvailableBytes);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnSpaceNotFound()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpaceNotFound));
        Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { context.SpaceId }));
        AssertNoWrites(harness);
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedState(SpaceStatus persistedStatus)
    {
        // Arrange
        var authorizedSpace = StoredSpace(SpaceStatus.Active, version: 1);
        var context = MemberContext(authorizedSpace);
        var persistedSpace = StoredSpace(persistedStatus, version: 4);
        var harness = new Harness(persistedSpace);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        AssertStateNotAllowed(result, persistedStatus);
        Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { context.SpaceId }));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnDependencyFailureForReaderError()
    {
        // Arrange
        var space = StoredSpace(SpaceStatus.Active);
        var context = MemberContext(space);
        var harness = new Harness(space);
        harness.SpaceRepository.FindByIdFailure = CommonError.DependencyUnavailable();

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(context));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Common.DependencyUnavailable));
        Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { context.SpaceId }));
        AssertNoWrites(harness);
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnContextValidationErrorWithoutDependencyCalls()
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuotaQuery(null!));

        // Assert
        AssertValidationError(
            result,
            ErrorCodes.Space.AuthorizationContextRequired,
            nameof(GetSpaceQuotaQuery.Space));
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.SearchedIds, Is.Empty);
            Assert.That(harness.SpaceRepository.SearchedPublicIds, Is.Empty);
        });
        AssertNoWrites(harness);
    }

    private static Space StoredSpace(
        SpaceStatus status,
        long? allowedBytes = 100,
        long version = 4) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId: 21,
            allowedBytes: allowedBytes,
            usedBytes: status == SpaceStatus.Deleted ? 0 : 20,
            reservedBytes: status == SpaceStatus.Deleted ? 0 : 10,
            version: version,
            createdAt: Now.AddDays(-7),
            updatedAt: Now.AddDays(-1));

    private static AuthorizedSpaceContext MemberContext(Space space)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(userId: 42);
        var membership = TestSpaceAuthorizationSnapshots.Membership(
            userId: actor.UserId,
            spaceId: space.Id);
        var role = TestSpaceAuthorizationSnapshots.Role(
            spaceId: space.Id,
            permissions: [SpacePermission.SpaceView]);
        var snapshot = new SpaceAuthorizationSnapshot(space, membership, [role]);
        var query = TestSpaceAuthorizationSnapshots.Query(
            actor: actor,
            permissions: [SpacePermission.SpaceView],
            statuses: [SpaceStatus.Active, SpaceStatus.Archived],
            spacePublicId: space.PublicId);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static AuthorizedSpaceContext McpContext(Space space) =>
        new(
            space.Id,
            space.PublicId,
            ActorType.Mcp,
            space.OwnerUserId,
            null,
            [SpacePermission.SpaceView],
            SpaceAuthorizationSource.Owner);

    private static void AssertSuccessfulRead(
        Harness harness,
        AuthorizedSpaceContext context,
        Space persistedSpace,
        Result<SpaceQuotaDto> result,
        long? expectedAvailableBytes)
    {
        Assert.That(result.IsSuccess, Is.True);
        var dto = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(dto.SpacePublicId, Is.EqualTo(persistedSpace.PublicId));
            Assert.That(dto.AllowedBytes, Is.EqualTo(persistedSpace.AllowedBytes));
            Assert.That(dto.UsedBytes, Is.EqualTo(20L));
            Assert.That(dto.ReservedBytes, Is.EqualTo(10L));
            Assert.That(dto.AvailableBytes, Is.EqualTo(expectedAvailableBytes));
            Assert.That(dto.Version, Is.EqualTo(persistedSpace.Version));
            Assert.That(harness.SpaceRepository.SearchedIds, Is.EqualTo(new[] { context.SpaceId }));
        });
        AssertNoWrites(harness);
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

    private static void AssertNoWrites(Harness harness)
    {
        Assert.Multiple(() =>
        {
            Assert.That(harness.SpaceRepository.AddedSpaces, Is.Empty);
            Assert.That(harness.SpaceRepository.UpdateCalls, Is.Empty);
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
