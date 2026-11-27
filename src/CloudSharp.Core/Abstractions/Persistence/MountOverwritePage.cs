namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountOverwritePage(
    IReadOnlyList<MountOverwriteSnapshot> Overwrites,
    string? NextCursor);
