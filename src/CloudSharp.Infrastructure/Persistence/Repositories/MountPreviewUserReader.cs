using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountPreviewUserReader(AppDbContext dbContext, ITokenHasher tokenHasher) : IMountPreviewUserReader
{
    public Task<Result<MountPreviewUserSnapshot?>> ReadUserAsync(
        long spaceId,
        Guid userPublicId,
        CancellationToken cancellationToken = default) =>
        dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var users = ReadScopedUsers(context, spaceId).Where(user => user.PublicId == userPublicId);
                var rows = await IncludeMembershipRoles(context, spaceId, users).ToArrayAsync(ct);
                return rows.GroupBy(row => row.User.Id).Select(ToDomain).SingleOrDefault();
            },
            cancellationToken);

    public Task<Result<MountPreviewCandidatePage>> ReadCandidatesAsync(
        long spaceId,
        string? cursor,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var scope = FormattableString.Invariant($"preview:{spaceId}");
        MountReadCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!MountReadCursor.TryRead(tokenHasher, scope, cursor, out var parsed) || parsed.CreatedAtTicks is not null)
            {
                return Task.FromResult(Result.Fail<MountPreviewCandidatePage>(MountReadCursor.Invalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var users = ReadScopedUsers(context, spaceId);
                if (position is { PublicId: var publicId })
                {
                    users = users.Where(user => user.PublicId.CompareTo(publicId) > 0);
                }

                var batch = users.OrderBy(user => user.PublicId).Take(checked(batchSize + 1));
                var rows = await IncludeMembershipRoles(context, spaceId, batch)
                    .OrderBy(row => row.User.PublicId)
                    .ToArrayAsync(ct);
                var groups = rows.GroupBy(row => row.User.Id).ToArray();
                var candidates = groups.Take(batchSize).Select(group =>
                {
                    var subject = ToDomain(group);
                    return new MountPreviewCandidate(subject, MountReadCursor.Issue(tokenHasher, scope, subject.User.PublicId));
                }).ToArray();
                return new MountPreviewCandidatePage(candidates, groups.Length > batchSize);
            },
            cancellationToken);
    }

    private static IQueryable<SpaceMembershipEntity> CurrentMemberships(AppDbContext context, long spaceId) =>
        context.SpaceMemberships.AsNoTracking().Where(member => member.SpaceId == spaceId
            && (member.Status == SpaceMembershipStatus.Active || member.Status == SpaceMembershipStatus.Suspended));

    private static IQueryable<UserEntity> ReadScopedUsers(AppDbContext context, long spaceId)
    {
        var memberships = CurrentMemberships(context, spaceId);
        return context.Users.AsNoTracking().Where(user =>
            context.Spaces.Any(space => space.Id == spaceId && space.OwnerUserId == user.Id)
            || memberships.Any(member => member.UserId == user.Id));
    }

    private static IQueryable<PreviewRow> IncludeMembershipRoles(
        AppDbContext context,
        long spaceId,
        IQueryable<UserEntity> users)
    {
        var memberships = CurrentMemberships(context, spaceId);
        return from user in users
            from membership in memberships.Where(member => member.UserId == user.Id).DefaultIfEmpty()
            from assignment in context.SpaceMembershipRoles.AsNoTracking()
                .Where(link => membership != null && link.SpaceId == spaceId && link.MembershipId == membership.Id)
                .DefaultIfEmpty()
            from role in context.SpaceRoles.AsNoTracking()
                .Where(role => assignment != null && role.SpaceId == spaceId && role.Id == assignment.RoleId)
                .DefaultIfEmpty()
            select new PreviewRow
            {
                User = user,
                Membership = membership,
                Assignment = assignment,
                Role = role,
            };
    }

    private static MountPreviewUserSnapshot ToDomain(IGrouping<long, PreviewRow> group)
    {
        var first = group.First();
        var assignments = group.Select(row => row.Assignment).OfType<SpaceMembershipRoleEntity>();
        var roles = group.Select(row => row.Role).OfType<SpaceRoleEntity>().Select(role => role.ToDomain()).ToArray();
        return new MountPreviewUserSnapshot(first.User.ToDomain(), first.Membership?.ToDomain(assignments), roles);
    }

    private sealed class PreviewRow
    {
        public required UserEntity User { get; init; }
        public SpaceMembershipEntity? Membership { get; init; }
        public SpaceMembershipRoleEntity? Assignment { get; init; }
        public SpaceRoleEntity? Role { get; init; }
    }
}
