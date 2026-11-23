using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Spaces.Listings;

public sealed class ListSpacesUseCase : IUseCase<ListSpacesQuery, SpacePageDto>
{
    private ISpaceListCandidateReader CandidateReader { get; } = null!;

    public ListSpacesUseCase() { }

    public ListSpacesUseCase(ISpaceListCandidateReader candidateReader) => CandidateReader = candidateReader;

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

        var actorValidation = SessionSpaceAuthorizationPolicy.ValidateActor(query.Actor);
        if (actorValidation.IsFailed)
        {
            return Result.Fail<SpacePageDto>(actorValidation.Errors);
        }

        if (CandidateReader is null)
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

        var allowedStatuses = new[] { SpaceStatus.Active, SpaceStatus.Archived };
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
                var authorization = SessionSpaceAuthorizationPolicy.Evaluate(
                    new SpaceAuthorizationQuery(
                        query.Actor,
                        candidate.Authorization.Space.PublicId,
                        Array.Empty<SpacePermission>(),
                        allowedStatuses,
                        query.AllowSystemAdminBypass),
                    candidate.Authorization);
                if (authorization.IsSuccess)
                {
                    accessible.Add((ToSpaceDto(candidate, authorization.Value), candidate.CursorAfter));
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
