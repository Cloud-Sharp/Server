namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountPreviewCandidatePage(
    IReadOnlyList<MountPreviewCandidate> Candidates,
    bool HasMore);
