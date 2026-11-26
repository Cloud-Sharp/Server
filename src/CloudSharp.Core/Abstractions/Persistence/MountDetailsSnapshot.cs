using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountDetailsSnapshot(
    Mount Mount,
    Guid RootFolderPublicId,
    long UsedBytes);
