using CloudSharp.Core.Domain.Memberships;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface ISpaceOwnershipMembershipRepository
{
    Task<Result<SpaceMembership?>> FindByUserIdAsync(
        long spaceId,
        long userId,
        CancellationToken ct = default);

    Task<Result<SpaceRole?>> FindManagedAdminRoleAsync(
        long spaceId,
        CancellationToken ct = default);

    Task<Result> DeleteAsync(
        SpaceMembership membership,
        long expectedVersion,
        CancellationToken ct = default);

    Task<Result> AddAsync(
        SpaceMembership membership,
        CancellationToken ct = default);
}
