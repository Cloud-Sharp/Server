using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountListCandidate(
    Mount Mount,
    string CursorAfter);
