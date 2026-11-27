using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountOverwriteReader(AppDbContext dbContext) : IMountOverwriteReader
{
    public Task<Result<MountOverwritePage>> ReadAsync(
        long spaceId,
        long mountId,
        MountOverwriteTargetType targetType,
        string? cursor,
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (!Enum.IsDefined(targetType))
        {
            return Task.FromResult(Result.Fail<MountOverwritePage>(MountError.TargetTypeInvalid()));
        }

        KeysetCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!KeysetCursor.TryRead(cursor, out var parsed) || parsed.SortValue is not null)
            {
                return Task.FromResult(Result.Fail<MountOverwritePage>(KeysetCursor.Invalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var grants = targetType == MountOverwriteTargetType.Role
                    ? from grant in context.MountRoleGrants.AsNoTracking()
                      join role in context.SpaceRoles.AsNoTracking() on grant.RoleId equals role.Id
                      where grant.MountId == mountId && grant.SpaceId == spaceId && role.SpaceId == spaceId
                      select new { PublicId = role.PublicId, TargetId = role.Id, grant.AllowMask, grant.DenyMask }
                    : from grant in context.MountUserGrants.AsNoTracking()
                      join user in context.Users.AsNoTracking() on grant.UserId equals user.Id
                      join mount in context.Mounts.AsNoTracking() on grant.MountId equals mount.Id
                      where mount.Id == mountId && mount.SpaceId == spaceId
                      select new { PublicId = user.PublicId, TargetId = user.Id, grant.AllowMask, grant.DenyMask };

                if (position is { PublicId: var publicId })
                {
                    grants = grants.Where(grant => grant.PublicId.CompareTo(publicId) > 0);
                }

                var rows = await grants.OrderBy(grant => grant.PublicId).Take(checked(limit + 1)).ToArrayAsync(ct);
                var items = rows.Take(limit).Select(row => new MountOverwriteSnapshot(
                    MountPermissionOverwrite.Reconstitute(
                        targetType,
                        row.TargetId,
                        MountOverwriteMask.Decode(row.AllowMask),
                        MountOverwriteMask.Decode(row.DenyMask)),
                    row.PublicId)).ToArray();
                var next = rows.Length > limit
                    ? KeysetCursor.Issue(items[^1].TargetPublicId)
                    : null;
                return new MountOverwritePage(items, next);
            },
            cancellationToken);
    }
}
