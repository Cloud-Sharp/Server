using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using FluentResults;

namespace CloudSharp.TestSupport.Fakes;

public sealed class FakeSpaceListCandidateReader : ISpaceListCandidateReader
{
    private readonly IReadOnlyList<SpaceListCandidate> candidates;
    private readonly List<FakeSpaceListRead> reads = new();

    public FakeSpaceListCandidateReader(IEnumerable<SpaceListCandidate> candidates, int maxBatchSize = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (maxBatchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize));
        }

        this.candidates = candidates.ToArray();
        MaxBatchSize = maxBatchSize;
    }

    public int MaxBatchSize { get; }

    public IReadOnlyList<FakeSpaceListRead> Reads => reads;

    public Task<Result<SpaceListCandidatePage>> ReadAsync(
        CurrentActor actor,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        reads.Add(new FakeSpaceListRead(actor, cursor, batchSize, sort, descending, cancellationToken));

        var start = 0;
        if (cursor is not null)
        {
            var cursorIndex = candidates.ToList().FindIndex(candidate => candidate.CursorAfter == cursor);
            if (cursorIndex < 0)
            {
                throw new InvalidOperationException($"Unknown test cursor: {cursor}");
            }

            start = cursorIndex + 1;
        }

        var page = candidates.Skip(start).Take(Math.Min(batchSize, MaxBatchSize)).ToArray();
        var result = new SpaceListCandidatePage(page, start + page.Length < candidates.Count);
        return Task.FromResult(Result.Ok(result));
    }
}

public sealed record FakeSpaceListRead(
    CurrentActor Actor,
    string? Cursor,
    int BatchSize,
    string? Sort,
    bool Descending,
    CancellationToken CancellationToken);
