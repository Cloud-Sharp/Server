using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using CloudSharp.Core.UseCases.Mounts.Listings;
using CloudSharp.TestSupport.Fakes;
using FluentResults;
using NUnit.Framework;
using static CloudSharp.Core.Tests.TestSupport.Builders.TestMountReadSnapshots;

namespace CloudSharp.Core.Tests.UseCases.Mounts.Listings;

[TestFixture]
public sealed class ListMountsUseCaseTests
{
    [Test]
    public async Task ExecuteAsync_ShouldFillPageAfterFilteringHiddenPrivateMounts()
    {
        // Arrange
        var actor = Actor();
        var candidates = new[]
        {
            Candidate(1),
            Candidate(2, MountVisibility.Private),
            Candidate(3, MountVisibility.Private),
            Candidate(4),
            Candidate(5),
        };
        var harness = new Harness(actor, [Member(actor)], candidates, maxBatchSize: 2);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, limit: 2));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Items.Select(item => item.MountPublicId),
                Is.EqualTo(new[] { candidates[0].Mount.PublicId, candidates[3].Mount.PublicId }));
            Assert.That(result.Value.NextCursor, Is.EqualTo(candidates[3].CursorAfter));
            Assert.That(result.Value.CanCreateMount, Is.False);
            Assert.That(harness.CandidateReader.Reads.Count, Is.GreaterThan(1));
            Assert.That(harness.CandidateReader.Reads.All(read => read.SpaceId == SpaceId), Is.True);
            Assert.That(harness.CandidateReader.Reads.All(read => read.Actor == actor), Is.True);
            Assert.That(harness.CandidateReader.Reads.All(read => read.BatchSize == 3), Is.True);
            Assert.That(harness.CandidateReader.Reads.All(read => read.Descending), Is.True);
        });
        AssertItemMatchesMount(result.Value.Items[0], candidates[0].Mount);
        AssertItemMatchesMount(result.Value.Items[1], candidates[3].Mount);
    }

    [Test]
    public async Task ExecuteAsync_ShouldNotEmitCursorWhenOnlyHiddenCandidatesRemain()
    {
        // Arrange
        var actor = Actor();
        var candidates = new[]
        {
            Candidate(10),
            Candidate(11, MountVisibility.Private),
            Candidate(12, MountVisibility.Private),
        };
        var harness = new Harness(actor, [Member(actor)], candidates, maxBatchSize: 1);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, limit: 1));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                result.Value.Items.Select(item => item.MountPublicId),
                Is.EqualTo(new[] { candidates[0].Mount.PublicId }));
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(harness.CandidateReader.Reads, Has.Count.EqualTo(candidates.Length));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldReevaluateRoleAssignmentsForNextPage()
    {
        // Arrange
        var actor = Actor();
        var previousRole = Role(101);
        var assignedRole = Role(102);
        var firstSnapshot = Member(
            actor,
            [previousRole, assignedRole],
            assignedRoleIds: [previousRole.Id]);
        var secondSnapshot = Member(
            actor,
            [previousRole, assignedRole],
            assignedRoleIds: [assignedRole.Id]);
        var privateMount = Mount(
            21,
            visibility: MountVisibility.Private,
            grants: [Grant(MountGrantTargetType.Role, assignedRole.Id, MountPermission.FilesRead)]);
        var candidates = new[]
        {
            Candidate(20),
            new MountListCandidate(privateMount, "cursor-21"),
            Candidate(22),
        };
        var harness = new Harness(actor, [firstSnapshot, secondSnapshot], candidates, maxBatchSize: 2);

        // Act
        var firstPage = await harness.UseCase.ExecuteAsync(Query(actor, limit: 1));
        var secondPage = await harness.UseCase.ExecuteAsync(Query(actor, limit: 1, cursor: firstPage.Value.NextCursor));

        // Assert
        Assert.That(firstPage.IsSuccess, Is.True);
        Assert.That(secondPage.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(
                firstPage.Value.Items.Select(item => item.MountPublicId),
                Is.EqualTo(new[] { candidates[0].Mount.PublicId }));
            Assert.That(firstPage.Value.NextCursor, Is.EqualTo(candidates[0].CursorAfter));
            Assert.That(
                secondPage.Value.Items.Select(item => item.MountPublicId),
                Is.EqualTo(new[] { privateMount.PublicId }));
            Assert.That(secondPage.Value.NextCursor, Is.EqualTo(candidates[1].CursorAfter));
            Assert.That(harness.AuthorizationReader.Reads, Has.Count.EqualTo(2));
            Assert.That(harness.CandidateReader.Reads.Last().Cursor, Is.EqualTo(firstPage.Value.NextCursor));
        });
    }

    [Test]
    public async Task ExecuteAsync_ShouldAllowActiveSpaceManagerToCreateMountOnEmptyPage()
    {
        // Arrange
        var actor = Actor();
        var harness = new Harness(actor, [Owner(actor)], []);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor));

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.Items, Is.Empty);
            Assert.That(result.Value.NextCursor, Is.Null);
            Assert.That(result.Value.CanCreateMount, Is.True);
        });
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task ExecuteAsync_ShouldReturnLimitValidationErrorWithoutDependencyCalls(int limit)
    {
        // Arrange
        var actor = Actor();
        var harness = new Harness(actor, [Member(actor)], []);

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, limit: limit));

        // Assert
        AssertValidationError(result, ErrorCodes.Mount.ListLimitInvalid, nameof(ListMountsQuery.Limit));
        Assert.Multiple(() =>
        {
            Assert.That(harness.AuthorizationReader.Reads, Is.Empty);
            Assert.That(harness.CandidateReader.Reads, Is.Empty);
        });
    }

    [TestCase(ErrorCodes.Common.CursorInvalid)]
    [TestCase(ErrorCodes.Common.DependencyUnavailable)]
    public async Task ExecuteAsync_ShouldPropagateCandidateReaderFailure(string errorCode)
    {
        // Arrange
        var actor = Actor();
        var failure = ReadError(errorCode);
        var harness = new Harness(actor, [Member(actor)], []);
        harness.CandidateReader.Failure = failure;

        // Act
        var result = await harness.UseCase.ExecuteAsync(Query(actor, cursor: "opaque-cursor"));

        // Assert
        AssertReadFailure(result, failure, errorCode);
    }

    private static ListMountsQuery Query(CurrentActor actor, int limit = 50, string? cursor = null) =>
        new(actor, SpacePublicId, cursor, limit, "CREATED_AT", Descending: true);

    private static MountListCandidate Candidate(long id, MountVisibility visibility = MountVisibility.Public) =>
        new(Mount(id, visibility: visibility), $"cursor-{id}");

    private static void AssertItemMatchesMount(MountSummaryDto item, Mount mount)
    {
        Assert.Multiple(() =>
        {
            Assert.That(item.MountPublicId, Is.EqualTo(mount.PublicId));
            Assert.That(item.Name, Is.EqualTo(mount.Name));
            Assert.That(item.Visibility, Is.EqualTo(mount.Visibility));
            Assert.That(item.Status, Is.EqualTo(mount.Status));
            Assert.That(item.CreatedAt, Is.EqualTo(mount.CreatedAt));
            Assert.That(item.UpdatedAt, Is.EqualTo(mount.UpdatedAt));
            Assert.That(
                item.MyAccess.Permissions,
                Is.EquivalentTo(new[] { MountPermission.FilesRead, MountPermission.FilesDownload }));
            Assert.That(item.MyAccess.CanManage, Is.False);
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

    private static void AssertReadFailure<T>(Result<T> result, IError failure, string errorCode)
    {
        Assert.That(result.IsFailed, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(result.Errors.Single(), Is.SameAs(failure));
            Assert.That(
                result.Errors.Single().Metadata[CloudSharpError.ErrorCodeMetadataKey],
                Is.EqualTo(errorCode));
        });
    }

    private sealed class Harness
    {
        public Harness(
            CurrentActor actor,
            IEnumerable<SpaceAuthorizationSnapshot> snapshots,
            IEnumerable<MountListCandidate> candidates,
            int maxBatchSize = int.MaxValue)
        {
            AuthorizationReader = new FakeSpaceAuthorizationReader(snapshots.ToArray());
            CandidateReader = new RecordingMountListCandidateReader(candidates, maxBatchSize);
        }

        public FakeSpaceAuthorizationReader AuthorizationReader { get; }
        public RecordingMountListCandidateReader CandidateReader { get; }
        public ListMountsUseCase UseCase => new(AuthorizationReader, CandidateReader);
    }

    private sealed class RecordingMountListCandidateReader : IMountListCandidateReader
    {
        private readonly MountListCandidate[] candidates;
        private readonly List<MountListRead> reads = [];

        public RecordingMountListCandidateReader(IEnumerable<MountListCandidate> candidates, int maxBatchSize)
        {
            this.candidates = candidates.ToArray();
            MaxBatchSize = maxBatchSize;
        }

        public int MaxBatchSize { get; }
        public IError? Failure { get; set; }
        public IReadOnlyList<MountListRead> Reads => reads;

        public Task<Result<MountListCandidatePage>> ReadAsync(
            long spaceId,
            CurrentActor actor,
            string? cursor,
            int batchSize,
            string? sort,
            bool descending,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reads.Add(new MountListRead(spaceId, actor, cursor, batchSize, sort, descending));
            if (Failure is { } failure)
            {
                return Task.FromResult(Result.Fail<MountListCandidatePage>(failure));
            }

            var start = 0;
            if (cursor is not null)
            {
                var cursorIndex = Array.FindIndex(candidates, candidate => candidate.CursorAfter == cursor);
                if (cursorIndex < 0)
                {
                    throw new InvalidOperationException($"Unknown test cursor: {cursor}");
                }

                start = cursorIndex + 1;
            }

            var page = candidates.Skip(start).Take(Math.Min(batchSize, MaxBatchSize)).ToArray();
            return Task.FromResult(Result.Ok(new MountListCandidatePage(
                page,
                start + page.Length < candidates.Length)));
        }
    }

    private sealed record MountListRead(
        long SpaceId,
        CurrentActor Actor,
        string? Cursor,
        int BatchSize,
        string? Sort,
        bool Descending);
}
