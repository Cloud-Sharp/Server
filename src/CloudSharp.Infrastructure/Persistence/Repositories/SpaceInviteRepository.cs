using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceInviteRepository(AppDbContext dbContext) : ISpaceInviteRepository
{
    public Task<Result<SpaceInvite?>> FindByPublicIdAsync(
        long spaceId,
        Guid invitePublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.SpaceInvites
                .AsNoTracking()
                .SingleOrDefaultAsync(invite =>
                    invite.SpaceId == spaceId && invite.PublicId == invitePublicId, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result<SpaceInvite?>> FindByTokenHashAsync(
        TokenHash tokenHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        cancellationToken.ThrowIfCancellationRequested();
        var storedHash = tokenHash.Value;

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.SpaceInvites
                .AsNoTracking()
                .SingleOrDefaultAsync(invite => invite.TokenHash == storedHash, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result> AddAsync(SpaceInvite invite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invite);
        cancellationToken.ThrowIfCancellationRequested();

        dbContext.SpaceInvites.Add(SpaceInviteEntity.FromDomain(invite));
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> UpdateAsync(
        SpaceInvite invite,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invite);
        cancellationToken.ThrowIfCancellationRequested();

        if (invite.Id <= 0)
        {
            throw new InvalidOperationException("UpdateAsync requires an invite with a generated id.");
        }

        var entity = dbContext.SpaceInvites.Local.FirstOrDefault(tracked => tracked.Id == invite.Id);
        if (entity is null)
        {
            entity = SpaceInviteEntity.FromDomain(invite);
            dbContext.SpaceInvites.Update(entity);
        }
        else
        {
            entity.UpdateFrom(invite);
        }

        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        return Task.FromResult(Result.Ok());
    }
}
