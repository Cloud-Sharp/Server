namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountPreviewCandidate(
    MountPreviewUserSnapshot Subject,
    string CursorAfter);
