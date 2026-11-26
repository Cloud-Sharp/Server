namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountGrantPage(
    IReadOnlyList<MountGrantSnapshot> Grants,
    string? NextCursor);
