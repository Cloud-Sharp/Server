using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceRepository(AppDbContext dbContext) : ISpaceRepository
{
    public Task<Result<Space?>> FindByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.Spaces
                .AsNoTracking()
                .SingleOrDefaultAsync(space => space.Id == id, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result<Space?>> FindByPublicIdAsync(
        Guid publicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) => (await context.Spaces
                .AsNoTracking()
                .SingleOrDefaultAsync(space => space.PublicId == publicId, ct))?.ToDomain(),
            cancellationToken);
    }

    public Task<Result> AddAsync(Space space, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        cancellationToken.ThrowIfCancellationRequested();

        dbContext.Spaces.Add(SpaceEntity.FromDomain(space));
        return Task.FromResult(Result.Ok());
    }

    public Task<Result> UpdateAsync(
        Space space,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        cancellationToken.ThrowIfCancellationRequested();

        if (space.Id <= 0)
        {
            throw new InvalidOperationException("UpdateAsync requires a space with a generated id.");
        }

        var entity = dbContext.Spaces.Local.FirstOrDefault(tracked => tracked.Id == space.Id);
        if (entity is null)
        {
            entity = SpaceEntity.FromDomain(space);
            dbContext.Spaces.Update(entity);
        }
        else
        {
            entity.UpdateFrom(space);
        }

        var versionEntry = dbContext.Entry(entity).Property(tracked => tracked.Version);
        versionEntry.OriginalValue = expectedVersion;
        versionEntry.IsModified = true;

        return Task.FromResult(Result.Ok());
    }
}
