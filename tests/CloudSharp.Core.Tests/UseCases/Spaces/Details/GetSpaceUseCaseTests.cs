using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Details;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.Details;

[TestFixture]
public class GetSpaceUseCaseTests
{
    private static readonly Guid OwnerUserPublicId = Guid.Parse("01977b58-7dcb-78a2-bf2b-f5aad30ec099");

    private sealed class Harness
    {
        public FakeSpaceDetailsReader DetailsReader { get; } = new();

        public GetSpaceUseCase UseCase => new(DetailsReader);
    }

    [TestCase(SpaceStatus.Active)]
    [TestCase(SpaceStatus.Archived)]
    public async Task ExecuteAsync_ShouldReturnAuthorizedSpaceDetails(SpaceStatus status)
    {
        // Arrange
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space(status, version: 1);
        var context = OwnerContext(authorizedSpace);
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(
            status,
            name: "Product",
            description: "Product documents",
            version: 9);
        var harness = new Harness();
        harness.DetailsReader.Snapshot = new SpaceDetailsSnapshot(
            persistedSpace,
            OwnerUserPublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuery(context));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        var space = result.Value.Space;
        Assert.Multiple(() =>
        {
            Assert.That(space.SpacePublicId, Is.EqualTo(persistedSpace.PublicId));
            Assert.That(space.OwnerUserPublicId, Is.EqualTo(OwnerUserPublicId));
            Assert.That(space.Name, Is.EqualTo("Product"));
            Assert.That(space.Description, Is.EqualTo("Product documents"));
            Assert.That(space.Status, Is.EqualTo(status));
            Assert.That(space.CreatedAt, Is.EqualTo(persistedSpace.CreatedAt));
            Assert.That(space.UpdatedAt, Is.EqualTo(persistedSpace.UpdatedAt));
            Assert.That(space.MyAccess.Source, Is.EqualTo(context.AuthorizationSource));
            Assert.That(space.MyAccess.Permissions, Is.EquivalentTo(context.EffectivePermissions));
            Assert.That(space.MyAccess.Permissions, Does.Contain(SpacePermission.SpaceView));
            Assert.That(result.Value.Version, Is.EqualTo(9L));
            Assert.That(result.Value.Version, Is.Not.EqualTo(authorizedSpace.Version));
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { context.SpaceId }));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnNotFoundForMissingSpace()
    {
        // Arrange
        var space = TestSpaceAuthorizationSnapshots.Space();
        var context = OwnerContext(space);
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuery(context));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpaceNotFound));
        Assert.That(harness.DetailsReader.ReadSpaceIds, Is.EqualTo(new[] { context.SpaceId }));
    }

    [TestCase(SpaceStatus.Deleting)]
    [TestCase(SpaceStatus.Deleted)]
    public async Task ExecuteAsync_ShouldRejectDisallowedPersistedState(SpaceStatus persistedStatus)
    {
        // Arrange
        var authorizedSpace = TestSpaceAuthorizationSnapshots.Space(SpaceStatus.Active);
        var context = OwnerContext(authorizedSpace);
        var persistedSpace = TestSpaceAuthorizationSnapshots.Space(persistedStatus, version: 4);
        var harness = new Harness();
        harness.DetailsReader.Snapshot = new SpaceDetailsSnapshot(
            persistedSpace,
            OwnerUserPublicId);

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuery(context));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        Assert.That(FirstErrorCode(result), Is.EqualTo(ErrorCodes.Auth.SpaceStateNotAllowed));
        var error = result.Errors.Single();
        Assert.That(error, Is.InstanceOf<SpaceAuthorizationError>());
        Assert.That(((SpaceAuthorizationError)error).ActualSpaceStatus, Is.EqualTo(persistedStatus));
    }

    [Test]
    public async Task ExecuteAsync_ShouldRequireDetailContextWithoutDependencyCalls()
    {
        // Arrange
        var harness = new Harness();

        // Act
        var result = await harness.UseCase.ExecuteAsync(new GetSpaceQuery(null!));

        // Assert
        Assert.That(result.IsFailed, Is.True);
        var error = result.Errors.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                error.Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(ErrorCodes.Space.AuthorizationContextRequired));
            Assert.That(error.Metadata["PropertyName"], Is.EqualTo(nameof(GetSpaceQuery.Space)));
            Assert.That(harness.DetailsReader.ReadSpaceIds, Is.Empty);
        });
    }

    private static AuthorizedSpaceContext OwnerContext(Space space)
    {
        var actor = TestSpaceAuthorizationSnapshots.Session(space.OwnerUserId);
        var query = TestSpaceAuthorizationSnapshots.Query(actor: actor, spacePublicId: space.PublicId);
        var snapshot = new SpaceAuthorizationSnapshot(space, null, []);
        return SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot).Value;
    }

    private static string FirstErrorCode(Result<SpaceResultDto> result) =>
        (string)result.Errors[0].Metadata[CloudSharpError.ErrorCodeMetadataKey]!;
}
