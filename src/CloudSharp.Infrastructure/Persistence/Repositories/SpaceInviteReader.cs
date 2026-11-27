using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

/// <summary>
/// Space의 활성 초대를 생성 시각과 PublicId keyset으로 조회한다. cursor는 <see cref="KeysetCursor"/> 위치
/// 토큰이며 요청자·정렬 조건 묶음과 서명은 API가 맡는다.
/// </summary>
internal sealed class SpaceInviteReader(AppDbContext dbContext) : ISpaceInviteReader
{
    private const string CreatedAtSort = "createdAt";

    public Task<Result<SpaceInviteReadPage>> ReadPageAsync(
        long spaceId,
        string? cursor,
        int limit,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeSort(sort, out _))
        {
            return Task.FromResult(Result.Fail<SpaceInviteReadPage>(SpaceError.ListSortInvalid()));
        }

        KeysetCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!KeysetCursor.TryRead(cursor, out var parsed) || parsed.SortValue is null)
            {
                return Task.FromResult(Result.Fail<SpaceInviteReadPage>(SpaceError.ListCursorInvalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var invites = context.SpaceInvites.AsNoTracking()
                    .Where(invite => invite.SpaceId == spaceId && invite.Status == SpaceInviteStatus.Active);
                if (position is { SortValue: { } createdAt, PublicId: var publicId })
                {
                    invites = ApplySeek(invites, createdAt, publicId, descending);
                }

                var rows = await ApplySort(invites, descending)
                    .Take(limit + 1)
                    .ToArrayAsync(ct);
                var snapshots = rows.Take(limit).Select(row => row.ToDomain()).ToArray();
                if (rows.Length <= limit)
                {
                    return new SpaceInviteReadPage(snapshots, null);
                }

                var last = snapshots[^1];
                var nextCursor = KeysetCursor.Issue(last.PublicId, last.CreatedAt);
                return new SpaceInviteReadPage(snapshots, nextCursor);
            },
            cancellationToken);
    }

    private static IQueryable<SpaceInviteEntity> ApplySeek(
        IQueryable<SpaceInviteEntity> invites,
        DateTimeOffset createdAt,
        Guid publicId,
        bool descending)
    {
        return descending
            ? invites.Where(invite =>
                invite.CreatedAt < createdAt
                || (invite.CreatedAt == createdAt && invite.PublicId.CompareTo(publicId) < 0))
            : invites.Where(invite =>
                invite.CreatedAt > createdAt
                || (invite.CreatedAt == createdAt && invite.PublicId.CompareTo(publicId) > 0));
    }

    private static IOrderedQueryable<SpaceInviteEntity> ApplySort(
        IQueryable<SpaceInviteEntity> invites,
        bool descending) =>
        descending
            ? invites.OrderByDescending(invite => invite.CreatedAt).ThenByDescending(invite => invite.PublicId)
            : invites.OrderBy(invite => invite.CreatedAt).ThenBy(invite => invite.PublicId);

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
}
