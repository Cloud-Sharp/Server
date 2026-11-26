namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountListCandidatePage(
    IReadOnlyList<MountListCandidate> Candidates,
    bool HasMore);
