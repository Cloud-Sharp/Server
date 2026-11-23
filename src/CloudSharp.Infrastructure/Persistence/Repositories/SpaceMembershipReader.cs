using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceMembershipReader(
    AppDbContext dbContext,
    ITokenHasher tokenHasher) : ISpaceMembershipReader
{
    private const string JoinedAtSort = "joinedAt";
    private const string UpdatedAtSort = "updatedAt";
    private const string AscendingDirection = "ASC";
    private const string DescendingDirection = "DESC";
    private const string CursorSigningPrefix = "cloudsharp:space-membership-list:cursor:v1:";
    private const string FilterScopePrefix = "cloudsharp:space-membership-list:scope:v1:";
    private const int CursorVersion = 1;

    private static readonly JsonSerializerOptions CursorJsonOptions = new();

    public Task<Result<SpaceMembershipReadPage>> ReadPageAsync(
        long spaceId,
        long ownerUserId,
        long requesterUserId,
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

        var filterScope = CreateFilterScope(spaceId, ownerUserId, requesterUserId);
        CursorPosition? position = null;
        if (cursor is not null
            && !TryReadCursor(cursor, normalizedSort, descending, filterScope, out position))
        {
            return Task.FromResult(Result.Fail<SpaceMembershipReadPage>(SpaceError.ListCursorInvalid()));
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

                if (position is not null)
                {
                    memberships = ApplySeek(memberships, normalizedSort, position, descending);
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
                var nextCursor = CreateCursor(
                    sortByJoinedAt ? last.JoinedAt : last.UpdatedAt,
                    last.PublicId,
                    normalizedSort,
                    descending,
                    filterScope);
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
        CursorPosition position,
        bool descending)
    {
        var sortValue = position.SortValue;
        var publicId = position.PublicId;
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

    private string CreateCursor(
        DateTimeOffset sortValue,
        Guid publicId,
        string normalizedSort,
        bool descending,
        string filterScope)
    {
        var claims = new CursorClaims(
            CursorVersion,
            sortValue.UtcDateTime.Ticks,
            publicId,
            normalizedSort,
            descending ? DescendingDirection : AscendingDirection,
            filterScope);
        var envelope = new CursorEnvelope(claims, Sign(claims));
        return Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(envelope, CursorJsonOptions));
    }

    private bool TryReadCursor(
        string cursor,
        string normalizedSort,
        bool descending,
        string filterScope,
        out CursorPosition? position)
    {
        position = null;
        try
        {
            var bytes = Base64Url.Decode(cursor);
            if (!string.Equals(Base64Url.Encode(bytes), cursor, StringComparison.Ordinal))
            {
                return false;
            }

            var envelope = JsonSerializer.Deserialize<CursorEnvelope>(bytes, CursorJsonOptions);
            var claims = envelope?.Claims;
            if (envelope is null || claims is null || string.IsNullOrEmpty(envelope.Signature)
                || !bytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(envelope, CursorJsonOptions)))
            {
                return false;
            }

            var expectedDirection = descending ? DescendingDirection : AscendingDirection;
            if (claims.Version != CursorVersion
                || claims.MembershipPublicId == Guid.Empty
                || !string.Equals(claims.Sort, normalizedSort, StringComparison.Ordinal)
                || !string.Equals(claims.Direction, expectedDirection, StringComparison.Ordinal)
                || !string.Equals(claims.FilterScope, filterScope, StringComparison.Ordinal)
                || !tokenHasher.Verify(SigningInput(claims), envelope.Signature))
            {
                return false;
            }

            position = new CursorPosition(
                new DateTimeOffset(claims.SortValueTicks, TimeSpan.Zero),
                claims.MembershipPublicId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private string Sign(CursorClaims claims) => tokenHasher.Hash(SigningInput(claims));

    private static string SigningInput(CursorClaims claims) =>
        CursorSigningPrefix + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(claims, CursorJsonOptions));

    private string CreateFilterScope(long spaceId, long ownerUserId, long requesterUserId) =>
        tokenHasher.Hash(FormattableString.Invariant($"{FilterScopePrefix}{spaceId}:{ownerUserId}:{requesterUserId}"));

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

    private sealed record CursorPosition(DateTimeOffset SortValue, Guid PublicId);

    private sealed record CursorClaims(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("sortValueTicks")] long SortValueTicks,
        [property: JsonPropertyName("membershipPublicId")] Guid MembershipPublicId,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("filterScope")] string? FilterScope);

    private sealed record CursorEnvelope(
        [property: JsonPropertyName("claims")] CursorClaims? Claims,
        [property: JsonPropertyName("signature")] string? Signature);

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
