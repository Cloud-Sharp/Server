using System.Text.Json;
using System.Text.Json.Serialization;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Invites;
using CloudSharp.Infrastructure.Auth.Tokens;
using CloudSharp.Infrastructure.Persistence.DbContext;
using CloudSharp.Infrastructure.Persistence.Entities;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace CloudSharp.Infrastructure.Persistence.Repositories;

internal sealed class SpaceInviteReader(
    AppDbContext dbContext,
    ITokenHasher tokenHasher) : ISpaceInviteReader
{
    private const string CreatedAtSort = "createdAt";
    private const string AscendingDirection = "ASC";
    private const string DescendingDirection = "DESC";
    private const string CursorSigningPrefix = "cloudsharp:space-invite-list:cursor:v1:";
    private const string FilterScopePrefix = "cloudsharp:space-invite-list:scope:v1:";
    private const int CursorVersion = 1;

    private static readonly JsonSerializerOptions CursorJsonOptions = new();

    public Task<Result<SpaceInviteReadPage>> ReadPageAsync(
        long spaceId,
        string? cursor,
        int limit,
        string? sort,
        bool descending,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryNormalizeSort(sort, out var normalizedSort))
        {
            return Task.FromResult(Result.Fail<SpaceInviteReadPage>(SpaceError.ListSortInvalid()));
        }

        var filterScope = CreateFilterScope(spaceId);
        CursorPosition? position = null;
        if (cursor is not null
            && !TryReadCursor(cursor, normalizedSort, descending, filterScope, out position))
        {
            return Task.FromResult(Result.Fail<SpaceInviteReadPage>(SpaceError.ListCursorInvalid()));
        }

        return dbContext.ExecuteReadAsync(
            async (context, ct) =>
            {
                var invites = context.SpaceInvites.AsNoTracking()
                    .Where(invite => invite.SpaceId == spaceId && invite.Status == SpaceInviteStatus.Active);
                if (position is not null)
                {
                    invites = ApplySeek(invites, position, descending);
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
                var nextCursor = CreateCursor(
                    last.CreatedAt,
                    last.PublicId,
                    normalizedSort,
                    descending,
                    filterScope);
                return new SpaceInviteReadPage(snapshots, nextCursor);
            },
            cancellationToken);
    }

    private static IQueryable<SpaceInviteEntity> ApplySeek(
        IQueryable<SpaceInviteEntity> invites,
        CursorPosition position,
        bool descending)
    {
        var createdAt = position.CreatedAt;
        var publicId = position.PublicId;
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

    private string CreateCursor(
        DateTimeOffset createdAt,
        Guid publicId,
        string normalizedSort,
        bool descending,
        string filterScope)
    {
        var claims = new CursorClaims(
            CursorVersion,
            createdAt.UtcDateTime.Ticks,
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
                || claims.InvitePublicId == Guid.Empty
                || !string.Equals(claims.Sort, normalizedSort, StringComparison.Ordinal)
                || !string.Equals(claims.Direction, expectedDirection, StringComparison.Ordinal)
                || !string.Equals(claims.FilterScope, filterScope, StringComparison.Ordinal)
                || !tokenHasher.Verify(SigningInput(claims), envelope.Signature))
            {
                return false;
            }

            position = new CursorPosition(
                new DateTimeOffset(claims.CreatedAtTicks, TimeSpan.Zero),
                claims.InvitePublicId);
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

    private string CreateFilterScope(long spaceId) =>
        tokenHasher.Hash($"{FilterScopePrefix}{spaceId}:ACTIVE");

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
        [property: JsonPropertyName("invitePublicId")] Guid InvitePublicId,
        [property: JsonPropertyName("sort")] string? Sort,
        [property: JsonPropertyName("direction")] string? Direction,
        [property: JsonPropertyName("filterScope")] string? FilterScope);

    private sealed record CursorEnvelope(
        [property: JsonPropertyName("claims")] CursorClaims? Claims,
        [property: JsonPropertyName("signature")] string? Signature);
}
