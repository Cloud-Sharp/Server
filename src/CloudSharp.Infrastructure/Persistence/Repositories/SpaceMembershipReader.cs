using System.Linq.Expressions;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

/// <summary>
/// Space Membership을 조회한다. 목록 cursor는 <see cref="KeysetCursor"/> 위치 토큰이며 요청자·정렬 조건 묶음과
/// 서명은 API가 맡는다.
/// </summary>
internal sealed class SpaceMembershipReader(AppDbContext dbContext) : ISpaceMembershipReader
{
    private const string JoinedAtSort = "joinedAt";
    private const string UpdatedAtSort = "updatedAt";

    public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
        long spaceId,
        long ownerUserId,
        string? cursor,
        int limit,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeSort(sort, out var normalizedSort))
        {
            return Task.FromResult(Result.Fail<SpaceMembershipReadPage>(SpaceError.ListSortInvalid()));
        }

        KeysetCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!KeysetCursor.TryRead(cursor, out var parsed) || parsed.SortValue is null)
            {
                return Task.FromResult(Result.Fail<SpaceMembershipReadPage>(SpaceError.ListCursorInvalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var memberships = context.SpaceMemberships.AsNoTracking()
                    .Where(member => member.SpaceId == spaceId
                        && member.UserId != ownerUserId
                        && (member.Status == SpaceMembershipStatus.Active
                            || member.Status == SpaceMembershipStatus.Suspended
                            || member.Status == SpaceMembershipStatus.Left));

                if (position is { SortValue: { } sortValue, PublicId: var publicId })
                {
                    memberships = ApplySeek(memberships, normalizedSort, sortValue, publicId, descending);
                }

                var sortByJoinedAt = normalizedSort == JoinedAtSort;
                var page = ApplySort(
                    memberships,
                    sortByJoinedAt ? member => member.JoinedAt : member => member.UpdatedAt,
                    member => member.PublicId,
                    descending).Take(limit + 1);
                var expanded = ApplySort(
                    Expand(context, page),
                    sortByJoinedAt ? row => row.Membership.JoinedAt : row => row.Membership.UpdatedAt,
                    row => row.Membership.PublicId,
                    descending);
                var rows = await expanded.ToArrayAsync(ct);
                var groups = rows.GroupBy(row => row.Membership.Id).ToArray();
                var snapshots = groups.Take(limit).Select(CreateSnapshot).ToArray();
                if (groups.Length <= limit)
                {
                    return new SpaceMembershipReadPage(snapshots, null);
                }

                var last = snapshots[^1].Membership;
                var nextCursor = KeysetCursor.Issue(last.PublicId, sortByJoinedAt ? last.JoinedAt : last.UpdatedAt);
                return new SpaceMembershipReadPage(snapshots, nextCursor);
            },
            cancellationToken);
    }

    public Task<Result<SpaceMembershipReadSnapshot?>> ReadAsync(
        long spaceId,
        Guid membershipPublicId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var memberships = context.SpaceMemberships.AsNoTracking()
                    .Where(candidate =>
                        candidate.SpaceId == spaceId
                        && candidate.PublicId == membershipPublicId);
                var rows = await Expand(context, memberships).ToArrayAsync(ct);
                return rows.Length == 0 ? null : CreateSnapshot(rows);
            },
            cancellationToken);
    }

    private static IQueryable<MembershipRow> Expand(
        AppDbContext context,
        IQueryable<SpaceMembershipEntity> memberships) =>
        from membership in memberships
        join user in context.Users.AsNoTracking() on membership.UserId equals user.Id
        from assignment in context.SpaceMembershipRoles.AsNoTracking()
            .Where(link => link.MembershipId == membership.Id && link.SpaceId == membership.SpaceId)
            .DefaultIfEmpty()
        from role in context.SpaceRoles.AsNoTracking()
            .Where(candidate => assignment != null
                && candidate.Id == assignment.RoleId
                && candidate.SpaceId == membership.SpaceId)
            .DefaultIfEmpty()
        select new MembershipRow
        {
            Membership = membership,
            UserPublicId = user.PublicId,
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            Assignment = assignment,
            Role = role,
        };

    private static SpaceMembershipReadSnapshot CreateSnapshot(IEnumerable<MembershipRow> rows)
    {
        var first = rows.First();
        var assignments = rows.Where(row => row.Assignment is not null).Select(row => row.Assignment!);
        var roles = rows.Where(row => row.Role is not null).Select(row => row.Role!.ToDomain()).ToArray();
        return new SpaceMembershipReadSnapshot(
            first.Membership.ToDomain(assignments),
            first.UserPublicId,
            first.UserName,
            first.DisplayName,
            roles);
    }

    private static IQueryable<SpaceMembershipEntity> ApplySeek(
        IQueryable<SpaceMembershipEntity> memberships,
        string normalizedSort,
        DateTimeOffset sortValue,
        Guid publicId,
        bool descending)
    {
        if (normalizedSort == JoinedAtSort)
        {
            return descending
                ? memberships.Where(member =>
                    member.JoinedAt < sortValue
                    || (member.JoinedAt == sortValue && member.PublicId.CompareTo(publicId) < 0))
                : memberships.Where(member =>
                    member.JoinedAt > sortValue
                    || (member.JoinedAt == sortValue && member.PublicId.CompareTo(publicId) > 0));
        }

        return descending
            ? memberships.Where(member =>
                member.UpdatedAt < sortValue
                || (member.UpdatedAt == sortValue && member.PublicId.CompareTo(publicId) < 0))
            : memberships.Where(member =>
                member.UpdatedAt > sortValue
                || (member.UpdatedAt == sortValue && member.PublicId.CompareTo(publicId) > 0));
    }

    private static IOrderedQueryable<T> ApplySort<T>(
        IQueryable<T> query,
        Expression<Func<T, DateTimeOffset>> sortValue,
        Expression<Func<T, Guid>> publicId,
        bool descending) =>
        descending
            ? query.OrderByDescending(sortValue).ThenByDescending(publicId)
            : query.OrderBy(sortValue).ThenBy(publicId);

    private static bool TryNormalizeSort(string? sort, out string normalizedSort)
    {
        if (sort is null
            || string.Equals(sort, JoinedAtSort, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSort = JoinedAtSort;
            return true;
        }

        if (string.Equals(sort, UpdatedAtSort, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSort = UpdatedAtSort;
            return true;
        }

        normalizedSort = string.Empty;
        return false;
    }

    private sealed class MembershipRow
    {
        public required SpaceMembershipEntity Membership { get; init; }
        public Guid UserPublicId { get; init; }
        public string? UserName { get; init; }
        public string? DisplayName { get; init; }
        public SpaceMembershipRoleEntity? Assignment { get; init; }
        public SpaceRoleEntity? Role { get; init; }
    }
}
