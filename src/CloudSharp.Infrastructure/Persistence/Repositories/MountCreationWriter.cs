using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountCreationWriter(AppDbContext dbContext) : IMountCreationWriter
{
    public Task<Result> AddAsync(Mount mount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mount);
        cancellationToken.ThrowIfCancellationRequested();

        var entity = MountEntity.FromDomain(mount);
        dbContext.Mounts.Add(entity);
        foreach (var grant in mount.Grants.Where(grant => grant.TargetType == MountGrantTargetType.Role))
        {
            dbContext.MountRoleGrants.Add(MountRoleGrantEntity.FromDomain(entity, grant));
        }

        foreach (var grant in mount.Grants.Where(grant => grant.TargetType == MountGrantTargetType.User))
        {
            dbContext.MountUserGrants.Add(MountUserGrantEntity.FromDomain(entity, grant));
        }

        return Task.FromResult(Result.Ok());
    }
}
