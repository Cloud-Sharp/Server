using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Users;

namespace CloudSharp.Core.Abstractions.Persistence;

public sealed record MountPreviewUserSnapshot(
    User User,
    SpaceMembership? Membership,
    IReadOnlyCollection<SpaceRole> Roles);
