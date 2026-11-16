using CloudSharp.Core.Policies.Authorization;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceListCandidate(
    SpaceAuthorizationSnapshot Authorization,
    Guid OwnerUserPublicId,
    Guid RootFolderPublicId,
    string CursorAfter);
