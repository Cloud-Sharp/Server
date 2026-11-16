using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Persistence;

public interface ISpaceInviteRepository
{
    Task<Result<SpaceInvite?>> FindByPublicIdAsync(
        long spaceId,
        Guid invitePublicId,
        CancellationToken cancellationToken = default);

    Task<Result<SpaceInvite?>> FindByTokenHashAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default);

    Task<Result> AddAsync(
        SpaceInvite invite,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        SpaceInvite invite,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
