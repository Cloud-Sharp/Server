using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Listings;

public sealed class ListSpacesUseCase : IUseCase<ListSpacesQuery, SpacePageDto>
{
    private ISpaceListCandidateReader CandidateReader { get; } = null!;
    private ISpaceListAccessPolicy AccessPolicy { get; } = null!;

    public ListSpacesUseCase() { }

    public ListSpacesUseCase(ISpaceListCandidateReader candidateReader, ISpaceListAccessPolicy accessPolicy)
    {
        CandidateReader = candidateReader;
        AccessPolicy = accessPolicy;
    }

    public async Task<Result<SpacePageDto>> ExecuteAsync(
        ListSpacesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListSpacesQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<SpacePageDto>();
        }

        if (CandidateReader is null || AccessPolicy is null)
        {
            return Result.Fail<SpacePageDto>(CommonError.DependencyUnavailable());
        }

        var accessible = new List<(SpaceDto Item, string Cursor)>();
        var traversalCursor = query.Cursor;
        var traversedCursors = new HashSet<string>(StringComparer.Ordinal);
        if (traversalCursor is not null)
        {
            traversedCursors.Add(traversalCursor);
        }

        while (true)
        {
            var readResult = await CandidateReader.ReadAsync(
                query.Actor,
                traversalCursor,
                query.Limit + 1,
                query.Sort,
                query.Descending,
                cancellationToken);
            if (readResult.IsFailed)
            {
                return Result.Fail<SpacePageDto>(readResult.Errors);
            }

            var page = readResult.Value;
            if (page.Candidates.Count == 0)
            {
                if (page.HasMore)
                {
                    throw new InvalidOperationException(
                        "Space list reader returned HasMore with an empty candidate batch.");
                }

                break;
            }

            var continuationCursor = page.Candidates[^1].CursorAfter;
            if (string.IsNullOrWhiteSpace(continuationCursor))
            {
                throw new InvalidOperationException("Space list reader returned a blank continuation cursor.");
            }

            if (!traversedCursors.Add(continuationCursor))
            {
                throw new InvalidOperationException("Space list reader repeated a continuation cursor.");
            }

            foreach (var candidate in page.Candidates)
            {
                var authorization = AccessPolicy.Evaluate(query.Actor, candidate.Authorization);
                if (authorization is not null)
                {
                    accessible.Add((ToSpaceDto(candidate, authorization), candidate.CursorAfter));
                    if (accessible.Count > query.Limit)
                    {
                        break;
                    }
                }
            }

            if (accessible.Count > query.Limit || !page.HasMore)
            {
                break;
            }

            traversalCursor = continuationCursor;
        }

        var items = accessible.Take(query.Limit).Select(entry => entry.Item).ToArray();
        var nextCursor = accessible.Count > query.Limit
            ? accessible[query.Limit - 1].Cursor
            : null;
        return Result.Ok(new SpacePageDto(items, nextCursor));
    }

    private static SpaceDto ToSpaceDto(
        SpaceListCandidate candidate,
        AuthorizedSpaceContext authorization)
    {
        var space = candidate.Authorization.Space;
        return new SpaceDto(
            space.PublicId,
            candidate.OwnerUserPublicId,
            space.Name,
            space.Description,
            space.Status,
            new SpaceAccessDto(authorization.AuthorizationSource, authorization.EffectivePermissions),
            space.CreatedAt,
            space.UpdatedAt);
    }
}
