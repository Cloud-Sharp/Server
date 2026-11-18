using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceBootstrapWriter(AppDbContext dbContext) : ISpaceBootstrapWriter
{
    public Task<Result> AddAsync(
        long spaceId,
        IReadOnlyList<SpaceRole> roles,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roles);
        cancellationToken.ThrowIfCancellationRequested();

        if (spaceId <= 0)
        {
            return Task.FromResult(Result.Fail(SpaceRoleError.SpaceIdInvalid()));
        }

        if (createdAt.Offset != TimeSpan.Zero)
        {
            return Task.FromResult(Result.Fail(
                SpaceRoleError.InvalidState("Space bootstrap requires UTC createdAt.")));
        }

        var entities = new List<SpaceRoleEntity>(roles.Count);
        foreach (var role in roles)
        {
            if (role is null)
            {
                throw new ArgumentException("Roles must not contain null elements.", nameof(roles));
            }

            if (!role.IsManaged || role.SpaceId != spaceId || role.CreatedAt != createdAt)
            {
                return Task.FromResult(Result.Fail(
                    SpaceRoleError.InvalidState("Bootstrap roles must be managed and match the Space id and creation time.")));
            }

            entities.Add(SpaceRoleEntity.FromDomain(role));
        }

        dbContext.SpaceRoles.AddRange(entities);
        return Task.FromResult(Result.Ok());
    }
}
