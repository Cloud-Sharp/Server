using CloudSharp.Core.Domain.Invites;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record SpaceInviteReadPage(
    IReadOnlyList<SpaceInvite> Items,
    string? NextCursor);
