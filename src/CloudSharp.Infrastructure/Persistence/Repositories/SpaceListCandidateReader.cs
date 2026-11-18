using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceListCandidateReader(
    AppDbContext dbContext,
    ITokenHasher tokenHasher) : ISpaceListCandidateReader
{
    private const string CreatedAtSort = "createdAt";
    private const string AscendingDirection = "ASC";
    private const string DescendingDirection = "DESC";
    private const string CursorSigningPrefix = "cloudsharp:space-list:cursor:v1:";
    private const string ActorScopePrefix = "cloudsharp:space-list:actor:v1:";
    private const int CursorVersion = 1;

    private static readonly JsonSerializerOptions CursorJsonOptions = new();

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

        if (!TryNormalizeSort(sort, out var normalizedSort))
        {
            return Task.FromResult(Result.Fail<SpaceListCandidatePage>(SpaceError.ListSortInvalid()));
        }

        var actorScope = CreateActorScope(actor);
        CursorPosition? position = null;
        if (cursor is not null
            && !TryReadCursor(cursor, normalizedSort, descending, actorScope, out position))
        {
            return Task.FromResult(Result.Fail<SpaceListCandidatePage>(SpaceError.ListCursorInvalid()));
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var spaces = context.Spaces.AsNoTracking();
                if (position is not null)
                {
                    var createdAt = position.CreatedAt;
                    var publicId = position.PublicId;
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
                return CreatePage(rows, batchSize, normalizedSort, descending, actorScope);
            },
            cancellationToken);
    }

    private SpaceListCandidatePage CreatePage(
        IReadOnlyCollection<SpaceListRow> rows,
        int batchSize,
        string normalizedSort,
        bool descending,
        string actorScope)
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
            var cursorAfter = CreateCursor(
                first.Space.CreatedAt,
                first.Space.PublicId,
                normalizedSort,
                descending,
                actorScope);
            return new SpaceListCandidate(authorization, first.OwnerUserPublicId, cursorAfter);
        }).ToArray();

        return new SpaceListCandidatePage(candidates, groups.Length > batchSize);
    }

    private string CreateCursor(
        DateTimeOffset createdAt,
        Guid publicId,
        string normalizedSort,
        bool descending,
        string actorScope)
    {
        var claims = new CursorClaims(
            CursorVersion,
            createdAt.UtcDateTime.Ticks,
            publicId,
            normalizedSort,
            descending ? DescendingDirection : AscendingDirection,
            actorScope);
        var envelope = new CursorEnvelope(claims, Sign(claims));
        return Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(envelope, CursorJsonOptions));
    }

    private bool TryReadCursor(
        string cursor,
        string normalizedSort,
        bool descending,
        string actorScope,
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
                || claims.SpacePublicId == Guid.Empty
                || !string.Equals(claims.Sort, normalizedSort, StringComparison.Ordinal)
                || !string.Equals(claims.Direction, expectedDirection, StringComparison.Ordinal)
                || !string.Equals(claims.ActorScope, actorScope, StringComparison.Ordinal)
                || !tokenHasher.Verify(SigningInput(claims), envelope.Signature))
            {
                return false;
            }

            position = new CursorPosition(
                new DateTimeOffset(claims.CreatedAtTicks, TimeSpan.Zero),
                claims.SpacePublicId);
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

    private string CreateActorScope(CurrentActor actor) =>
        tokenHasher.Hash($"{ActorScopePrefix}{actor.ActorType}:{actor.UserPublicId:N}:{actor.SystemRole}");

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

    private sealed record CursorPosition(DateTimeOffset CreatedAt, Guid PublicId);

    private sealed record CursorClaims(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("createdAtTicks")] long CreatedAtTicks,
        [property: JsonPropertyName("spacePublicId")] Guid SpacePublicId,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("actorScope")] string? ActorScope);

    private sealed record CursorEnvelope(
        [property: JsonPropertyName("claims")] CursorClaims? Claims,
        [property: JsonPropertyName("signature")] string? Signature);

    private sealed class SpaceListRow
    {
        public required SpaceEntity Space { get; init; }
        public Guid OwnerUserPublicId { get; init; }
        public SpaceMembershipEntity? Membership { get; init; }
        public SpaceMembershipRoleEntity? Assignment { get; init; }
        public SpaceRoleEntity? Role { get; init; }
    }
}
