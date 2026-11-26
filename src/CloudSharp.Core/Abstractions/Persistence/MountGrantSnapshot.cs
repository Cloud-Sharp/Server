using CloudSharp.Core.Domain.Mounts;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountGrantSnapshot(
    MountAccessGrant Grant,
    Guid TargetPublicId);
