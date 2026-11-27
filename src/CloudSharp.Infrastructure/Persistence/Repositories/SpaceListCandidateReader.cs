using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

/// <summary>
/// Space 목록 후보를 생성 시각과 PublicId keyset으로 조회한다. cursor는 <see cref="KeysetCursor"/> 위치 토큰이며
/// 요청자·정렬 조건 묶음과 서명은 API가 맡는다.
/// </summary>
internal sealed class SpaceListCandidateReader(AppDbContext dbContext) : ISpaceListCandidateReader
{
    private const string CreatedAtSort = "createdAt";

    public Task<Result<SpaceListCandidatePage>> ReadAsync(
        CurrentActor actor,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeSort(sort, out _))
        {
            return Task.FromResult(Result.Fail<SpaceListCandidatePage>(SpaceError.ListSortInvalid()));
        }

        KeysetCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!KeysetCursor.TryRead(cursor, out var parsed) || parsed.SortValue is null)
            {
                return Task.FromResult(Result.Fail<SpaceListCandidatePage>(SpaceError.ListCursorInvalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var spaces = context.Spaces.AsNoTracking();
                if (position is { SortValue: { } createdAt, PublicId: var publicId })
                {
                    spaces = descending
                        ? spaces.Where(space =>
                            space.CreatedAt < createdAt
                            || (space.CreatedAt == createdAt && space.PublicId.CompareTo(publicId) < 0))
                        : spaces.Where(space =>
                            space.CreatedAt > createdAt
                            || (space.CreatedAt == createdAt && space.PublicId.CompareTo(publicId) > 0));
                }

                spaces = descending
                    ? spaces.OrderByDescending(space => space.CreatedAt)
                        .ThenByDescending(space => space.PublicId)
                    : spaces.OrderBy(space => space.CreatedAt)
                        .ThenBy(space => space.PublicId);

                var spaceBatch = spaces.Take(batchSize + 1);
                var expanded =
                    from space in spaceBatch
                    join owner in context.Users.AsNoTracking() on space.OwnerUserId equals owner.Id
                    from membership in context.SpaceMemberships.AsNoTracking()
                        .Where(member =>
                            member.SpaceId == space.Id
                            && member.UserId == actor.UserId
                            && (member.Status == SpaceMembershipStatus.Active
                                || member.Status == SpaceMembershipStatus.Suspended))
                        .DefaultIfEmpty()
                    from assignment in context.SpaceMembershipRoles.AsNoTracking()
                        .Where(link =>
                            membership != null
                            && link.MembershipId == membership.Id
                            && link.SpaceId == space.Id)
                        .DefaultIfEmpty()
                    from role in context.SpaceRoles.AsNoTracking()
                        .Where(candidate =>
                            assignment != null
                            && candidate.Id == assignment.RoleId
                            && candidate.SpaceId == space.Id)
                        .DefaultIfEmpty()
                    select new SpaceListRow
                    {
                        Space = space,
                        OwnerUserPublicId = owner.PublicId,
                        Membership = membership,
                        Assignment = assignment,
                        Role = role,
                    };

                expanded = descending
                    ? expanded.OrderByDescending(row => row.Space.CreatedAt)
                        .ThenByDescending(row => row.Space.PublicId)
                    : expanded.OrderBy(row => row.Space.CreatedAt)
                        .ThenBy(row => row.Space.PublicId);

                var rows = await expanded.ToArrayAsync(ct);
                return CreatePage(rows, batchSize);
            },
            cancellationToken);
    }

    private static SpaceListCandidatePage CreatePage(IReadOnlyCollection<SpaceListRow> rows, int batchSize)
    {
        var groups = rows.GroupBy(row => row.Space.Id).ToArray();
        var candidates = groups.Take(batchSize).Select(group =>
        {
            var first = group.First();
            var membershipEntity = group.Select(row => row.Membership).FirstOrDefault(entity => entity is not null);
            var assignments = group
                .Where(row => row.Assignment is not null)
                .Select(row => row.Assignment!)
                .ToArray();
            var membership = membershipEntity?.ToDomain(assignments);
            var roles = group
                .Where(row => row.Role is not null)
                .Select(row => row.Role!.ToDomain())
                .ToArray();
            var authorization = new SpaceAuthorizationSnapshot(first.Space.ToDomain(), membership, roles);
            var cursorAfter = KeysetCursor.Issue(first.Space.PublicId, first.Space.CreatedAt);
            return new SpaceListCandidate(authorization, first.OwnerUserPublicId, cursorAfter);
        }).ToArray();

        return new SpaceListCandidatePage(candidates, groups.Length > batchSize);
    }

    private static bool TryNormalizeSort(string? sort, out string normalizedSort)
    {
        if (sort is null || string.Equals(sort, CreatedAtSort, StringComparison.OrdinalIgnoreCase))
        {
            normalizedSort = CreatedAtSort;
            return true;
        }

        normalizedSort = string.Empty;
        return false;
    }

    private sealed class SpaceListRow
    {
        public required SpaceEntity Space { get; init; }
        public Guid OwnerUserPublicId { get; init; }
        public SpaceMembershipEntity? Membership { get; init; }
        public SpaceMembershipRoleEntity? Assignment { get; init; }
        public SpaceRoleEntity? Role { get; init; }
    }
}
