using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.Tests.TestSupport.Builders;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.Listings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;

namespace CloudSharp.Core.Tests.UseCases.Spaces.Listings;

[TestFixture]
public class ListSpacesUseCaseTests
{
    private const long UserId = 42;
    private static readonly ISpaceListAccessPolicy AccessPolicy = new TestSpaceListAccessPolicy();

    [Test]
    public async Task ExecuteAsync_ShouldFilterCandidatesBeforeApplyingPageLimit()
    {
        // Arrange
        var candidates = new[]
        {
            Owned(1),
            WithoutAccess(2),
            Member(3, membershipStatus: SpaceMembershipStatus.Suspended),
            Member(4, status: SpaceStatus.Archived),
            Owned(5, SpaceStatus.Deleting),
            Member(6, membershipStatus: SpaceMembershipStatus.Left),
            Owned(7, SpaceStatus.Deleted),
            Owned(8),
        };
        var reader = new FakeSpaceListCandidateReader(candidates, maxBatchSize: 2);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Limit: 2));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Items.Select(item => item.SpacePublicId),
                Is.EqualTo(new[] { PublicId(1), PublicId(4) }));
            Assert.That(result.Value.NextCursor, Is.EqualTo(candidates[3].CursorAfter));
            Assert.That(reader.Reads.Count, Is.GreaterThan(1));
        });
        AssertItemMatchesCandidate(result.Value.Items[0], candidates[0]);
        AssertItemMatchesCandidate(result.Value.Items[1], candidates[3]);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items[0].MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(result.Value.Items[1].MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Membership));
            Assert.That(
                result.Value.Items[1].MyAccess.Permissions,
                Is.EquivalentTo(new[] { SpacePermission.SpaceView, SpacePermission.FilesRead }));
        });
    }

    [Test]
    public async Task ExecuteAsync_SystemAdministratorWithoutBypass_ShouldFilterOutsideSpaces()
    {
        // Arrange
        var actor = TestSpaceAuthorizationSnapshots.Session(UserId, SystemRole.SystemAdmin);
        var candidates = new[] { Owned(40), WithoutAccess(41), Member(42) };
        var reader = new FakeSpaceListCandidateReader(candidates, maxBatchSize: 3);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(actor));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Items.Select(item => item.SpacePublicId),
                Is.EqualTo(new[] { PublicId(40), PublicId(42) }));
            Assert.That(result.Value.Items[0].MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Owner));
            Assert.That(result.Value.Items[1].MyAccess.Source, Is.EqualTo(SpaceAuthorizationSource.Membership));
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldNotEmitCursorForInaccessibleRemainder()
    {
        // Arrange
        var candidates = new[]
        {
            Owned(9),
            WithoutAccess(10),
            Owned(11, SpaceStatus.Deleting),
        };
        var reader = new FakeSpaceListCandidateReader(candidates, maxBatchSize: 1);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Limit: 1));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items.Select(item => item.SpacePublicId), Is.EqualTo(new[] { PublicId(9) }));
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(reader.Reads.Count, Is.EqualTo(candidates.Length));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReplayCursorWithoutDuplicatesAndEndWithNullCursor()
    {
        // Arrange
        var candidates = new[] { Owned(10), Member(11), Owned(12) };
        var reader = new FakeSpaceListCandidateReader(candidates, maxBatchSize: 2);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var firstPage = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Limit: 2));
        var secondPage = await useCase.ExecuteAsync(
            new ListSpacesQuery(Actor(), Cursor: firstPage.Value.NextCursor, Limit: 2));

        // Assert
        Assert.That(firstPage.IsSuccess, Is.True);
        Assert.That(secondPage.IsSuccess, Is.True);
        var identifiers = firstPage.Value.Items.Concat(secondPage.Value.Items)
            .Select(item => item.SpacePublicId)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(firstPage.Value.NextCursor, Is.EqualTo(candidates[1].CursorAfter));
            Assert.That(secondPage.Value.NextCursor, Is.Null);
            Assert.That(identifiers, Is.EqualTo(new[] { PublicId(10), PublicId(11), PublicId(12) }));
            Assert.That(identifiers.Distinct().Count(), Is.EqualTo(identifiers.Length));
            Assert.That(
                reader.Reads.Select(read => read.Cursor),
                Does.Contain(firstPage.Value.NextCursor));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReturnEmptyPageWhenNothingIsAccessible()
    {
        // Arrange
        var candidates = new[]
        {
            WithoutAccess(20),
            Member(21, membershipStatus: SpaceMembershipStatus.Suspended),
            Owned(22, SpaceStatus.Deleting),
        };
        var reader = new FakeSpaceListCandidateReader(candidates, maxBatchSize: 2);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Limit: 2));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.NextCursor, Is.Null);
        });
    }

    private static IEnumerable<TestCaseData> MalformedCandidatePages()
    {
        yield return new TestCaseData(new List<SpaceListCandidatePage>
            {
                new([], true),
            })
            .SetName("HasMore accompanies an empty batch");

        var repeatedPage = new SpaceListCandidatePage([Owned(30)], true);
        yield return new TestCaseData(new List<SpaceListCandidatePage>
            {
                repeatedPage,
                repeatedPage,
            })
            .SetName("Continuation cursor repeats");
    }

    [TestCaseSource(nameof(MalformedCandidatePages))]
    public void ExecuteAsync_ShouldThrowInvalidOperationException(
        IReadOnlyList<SpaceListCandidatePage> pages)
    {
        // Arrange
        var reader = new ScriptedSpaceListCandidateReader(pages);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await useCase.ExecuteAsync(new ListSpacesQuery(Actor())));
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task ExecuteAsync_ShouldReturnLimitValidationErrorWithoutDependencyCalls(int limit)
    {
        // Arrange
        var reader = new FakeSpaceListCandidateReader([]);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Limit: limit));

        // Assert
        AssertValidationError(result, ErrorCodes.Space.ListLimitInvalid, nameof(ListSpacesQuery.Limit));
        Assert.That(reader.Reads, Is.Empty);
    }

    [TestCase("")]
    [TestCase("  ")]
    public async Task ExecuteAsync_ShouldReturnCursorValidationErrorWithoutDependencyCalls(string cursor)
    {
        // Arrange
        var reader = new FakeSpaceListCandidateReader([]);
        var useCase = new ListSpacesUseCase(reader, AccessPolicy);

        // Act
        var result = await useCase.ExecuteAsync(new ListSpacesQuery(Actor(), Cursor: cursor));

        // Assert
        AssertValidationError(result, ErrorCodes.Space.ListCursorInvalid, nameof(ListSpacesQuery.Cursor));
        Assert.That(reader.Reads, Is.Empty);
    }

    private sealed class TestSpaceListAccessPolicy : ISpaceListAccessPolicy
    {
        public AuthorizedSpaceContext? Evaluate(CurrentActor actor, SpaceAuthorizationSnapshot snapshot)
        {
            var query = new SpaceAuthorizationQuery(
                actor, snapshot.Space.PublicId, [], [SpaceStatus.Active, SpaceStatus.Archived]);
            var result = SessionSpaceAuthorizationPolicy.Evaluate(query, snapshot);
            return result.IsSuccess ? result.Value : null;
        }
    }

    private sealed class ScriptedSpaceListCandidateReader(IReadOnlyList<SpaceListCandidatePage> pages)
        : ISpaceListCandidateReader
    {
        private int nextPageIndex;

        public Task<Result<SpaceListCandidatePage>> ReadAsync(
            CurrentActor actor,
            string? cursor,
            int batchSize,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (nextPageIndex >= pages.Count)
            {
                return Task.FromResult(Result.Fail<SpaceListCandidatePage>(
                    CommonError.DependencyUnavailable()));
            }

            return Task.FromResult(Result.Ok(pages[nextPageIndex++]));
        }
    }

    private static CurrentActor Actor() => TestSpaceAuthorizationSnapshots.Session(UserId);

    private static SpaceListCandidate Owned(long id, SpaceStatus status = SpaceStatus.Active)
    {
        var space = Space(id, status, ownerUserId: UserId);
        return new SpaceListCandidate(
            new SpaceAuthorizationSnapshot(space, null, []),
            Actor().UserPublicId,
            $"opaque-cursor-{id}");
    }

    private static SpaceListCandidate Member(
        long id,
        SpaceStatus status = SpaceStatus.Active,
        SpaceMembershipStatus membershipStatus = SpaceMembershipStatus.Active)
    {
        var space = Space(id, status, ownerUserId: 21);
        var role = TestSpaceAuthorizationSnapshots.Role(id: 1_000 + id, spaceId: id);
        var membership = TestSpaceAuthorizationSnapshots.Membership(
            membershipStatus,
            UserId,
            id,
            500 + id,
            role.Id);
        return new SpaceListCandidate(
            new SpaceAuthorizationSnapshot(space, membership, [role]),
            PublicId(10_000 + id),
            $"opaque-cursor-{id}");
    }

    private static SpaceListCandidate WithoutAccess(long id)
    {
        var space = Space(id, SpaceStatus.Active, ownerUserId: 21);
        return new SpaceListCandidate(
            new SpaceAuthorizationSnapshot(space, null, []),
            PublicId(10_000 + id),
            $"opaque-cursor-{id}");
    }

    private static Space Space(long id, SpaceStatus status, long ownerUserId) =>
        TestSpaceAuthorizationSnapshots.Space(
            status,
            ownerUserId,
            PublicId(id),
            id,
            name: $"Space {id}",
            description: $"Description {id}");

    private static Guid PublicId(long value) =>
        Guid.Parse($"00000000-0000-7000-8000-{value:D12}");

    private static void AssertItemMatchesCandidate(SpaceDto item, SpaceListCandidate candidate)
    {
        var space = candidate.Authorization.Space;
        Assert.Multiple(() =>
        {
            Assert.That(item.SpacePublicId, Is.EqualTo(space.PublicId));
            Assert.That(item.OwnerUserPublicId, Is.EqualTo(candidate.OwnerUserPublicId));
            Assert.That(item.Name, Is.EqualTo(space.Name));
            Assert.That(item.Description, Is.EqualTo(space.Description));
            Assert.That(item.Status, Is.EqualTo(space.Status));
            Assert.That(item.CreatedAt, Is.EqualTo(space.CreatedAt));
            Assert.That(item.UpdatedAt, Is.EqualTo(space.UpdatedAt));
        });
    }

    private static void AssertValidationError(
        Result<SpacePageDto> result,
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
}
