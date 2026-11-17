using CloudSharp.Core.Domain.Spaces;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceDetailsSnapshot(
    Space Space,
    Guid OwnerUserPublicId);
