using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class MountListCandidateReader(AppDbContext dbContext, ITokenHasher tokenHasher) : IMountListCandidateReader
{
    private const string CreatedAtSort = "CREATED_AT";

    public Task<Result<MountListCandidatePage>> ReadAsync(
        long spaceId,
        CurrentActor actor,
        string? cursor,
        int batchSize,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        if (sort is not null and not CreatedAtSort)
        {
            var error = new Error("Sort must be CREATED_AT when supplied.")
                .WithMetadata(CloudSharpError.ErrorCodeMetadataKey, ErrorCodes.Mount.ListSortInvalid);
            return Task.FromResult(Result.Fail<MountListCandidatePage>(error));
        }

        var scope = FormattableString.Invariant(
            $"list:{spaceId}:{actor.ActorType}:{actor.UserId}:{actor.UserPublicId:N}:{actor.SystemRole}:{actor.McpCredentialPublicId:N}:{CreatedAtSort}:{descending}");
        MountReadCursor.Position? position = null;
        if (cursor is not null)
        {
            if (!MountReadCursor.TryRead(tokenHasher, scope, cursor, out var parsed)
                || parsed.CreatedAtTicks is null or < 0
                || parsed.CreatedAtTicks > DateTime.MaxValue.Ticks)
            {
                return Task.FromResult(Result.Fail<MountListCandidatePage>(MountReadCursor.Invalid()));
            }

            position = parsed;
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var mounts = context.Mounts.AsNoTracking().Where(mount => mount.SpaceId == spaceId);
                if (position is { CreatedAtTicks: { } ticks, PublicId: var publicId })
                {
                    var createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
                    mounts = descending
                        ? mounts.Where(mount => mount.CreatedAt < createdAt
                            || (mount.CreatedAt == createdAt && mount.PublicId.CompareTo(publicId) < 0))
                        : mounts.Where(mount => mount.CreatedAt > createdAt
                            || (mount.CreatedAt == createdAt && mount.PublicId.CompareTo(publicId) > 0));
                }

                var ordered = descending
                    ? mounts.OrderByDescending(mount => mount.CreatedAt).ThenByDescending(mount => mount.PublicId)
                    : mounts.OrderBy(mount => mount.CreatedAt).ThenBy(mount => mount.PublicId);
                var rows = MountSnapshotQuery.IncludeGrants(context, ordered.Take(checked(batchSize + 1)));
                var orderedRows = descending
                    ? rows.OrderByDescending(row => row.Mount.CreatedAt).ThenByDescending(row => row.Mount.PublicId)
                    : rows.OrderBy(row => row.Mount.CreatedAt).ThenBy(row => row.Mount.PublicId);

                var materialized = await orderedRows.ToArrayAsync(ct);
                var groups = materialized.GroupBy(row => row.Mount.Id).ToArray();
                var candidates = groups.Take(batchSize).Select(group =>
                {
                    var mount = MountSnapshotQuery.ToDomain(group);
                    var after = MountReadCursor.Issue(tokenHasher, scope, mount.PublicId, mount.CreatedAt.UtcTicks);
                    return new MountListCandidate(mount, after);
                }).ToArray();
                return new MountListCandidatePage(candidates, groups.Length > batchSize);
            },
            cancellationToken);
    }
}
