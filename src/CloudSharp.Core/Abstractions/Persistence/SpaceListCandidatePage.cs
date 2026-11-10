namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceListCandidatePage(
    IReadOnlyList<SpaceListCandidate> Candidates,
    bool HasMore);
