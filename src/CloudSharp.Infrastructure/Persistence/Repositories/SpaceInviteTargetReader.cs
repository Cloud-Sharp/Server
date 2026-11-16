using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Transactions;
using Dapper;
using FluentResults;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceInviteTargetReader(DapperDbSession session) : ISpaceInviteTargetReader
{
    private const string ReadSpaceSql = """
        SELECT id, public_id AS PublicId, owner_user_id AS OwnerUserId, status::text AS Status
        FROM spaces
        WHERE id = @SpaceId
        FOR SHARE
        """;

    public async Task<Result<SpaceInviteTargetSnapshot?>> ReadLockedAsync(
        long spaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var command = session.CreateCommand(ReadSpaceSql, new { SpaceId = spaceId }, cancellationToken: cancellationToken);
        if (command.Transaction is null)
        {
            throw new InvalidOperationException("A Space invite target can only be locked in an active transaction.");
        }

        try
        {
            var row = await session.Connection.QuerySingleOrDefaultAsync<SpaceRow>(command);
            return Result.Ok<SpaceInviteTargetSnapshot?>(row?.ToSnapshot());
        }
        catch (Exception exception) when (PostgresAvailabilityErrors.IsDependencyUnavailable(exception))
        {
            return Result.Fail<SpaceInviteTargetSnapshot?>(CommonError.DependencyUnavailable());
        }
    }

    private sealed record SpaceRow(long Id, Guid PublicId, long OwnerUserId, string Status)
    {
        public SpaceInviteTargetSnapshot ToSnapshot() => new(Id, PublicId, OwnerUserId, Status switch
        {
            "ACTIVE" => SpaceStatus.Active,
            "ARCHIVED" => SpaceStatus.Archived,
            "DELETING" => SpaceStatus.Deleting,
            "DELETED" => SpaceStatus.Deleted,
            _ => throw new InvalidOperationException("The Space has an unsupported persisted status."),
        });
    }
}
