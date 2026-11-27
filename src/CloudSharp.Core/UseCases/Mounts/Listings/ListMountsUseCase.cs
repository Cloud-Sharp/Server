using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.Listings;

public sealed class ListMountsUseCase : IUseCase<ListMountsQuery, MountPageDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountListCandidateReader CandidateReader { get; } = null!;

    public ListMountsUseCase() { }

    public ListMountsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountListCandidateReader candidateReader)
    {
        AuthorizationReader = authorizationReader;
        CandidateReader = candidateReader;
    }

    public async Task<Result<MountPageDto>> ExecuteAsync(
        ListMountsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new ListMountsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountPageDto>();
        }

        if (AuthorizationReader is null || CandidateReader is null)
        {
            return Result.Fail<MountPageDto>(CommonError.DependencyUnavailable());
        }

        var context = query.Space;
        var snapshotResult = await MountWorkflowReads.ReadSpaceAsync(AuthorizationReader, context, cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountPageDto>(snapshotResult.Errors);
        }

        var snapshot = snapshotResult.Value;
        var space = snapshot.Space;
        var canCreateMount = space.Status == SpaceStatus.Active
            && context.EffectivePermissions.Contains(SpacePermission.MountManage);
        var accessible = new List<(MountSummaryDto Item, string Cursor)>();
        var traversalCursor = query.Cursor;
        var traversedCursors = new HashSet<string>(StringComparer.Ordinal);
        if (traversalCursor is not null)
        {
            traversedCursors.Add(traversalCursor);
        }

        while (true)
        {
            var readResult = await CandidateReader.ReadAsync(
                space.Id,
                traversalCursor,
                query.Limit + 1,
                query.Sort,
                query.Descending,
                cancellationToken);
            if (readResult.IsFailed)
            {
                return Result.Fail<MountPageDto>(readResult.Errors);
            }

            var page = readResult.Value;
            if (page.Candidates.Count == 0)
            {
                if (page.HasMore)
                {
                    throw new InvalidOperationException(
                        "Mount list reader returned HasMore with an empty candidate batch.");
                }

                break;
            }

            var continuationCursor = page.Candidates[^1].CursorAfter;
            if (string.IsNullOrWhiteSpace(continuationCursor))
            {
                throw new InvalidOperationException("Mount list reader returned a blank continuation cursor.");
            }

            if (!traversedCursors.Add(continuationCursor))
            {
                throw new InvalidOperationException("Mount list reader repeated a continuation cursor.");
            }

            foreach (var candidate in page.Candidates)
            {
                var mount = candidate.Mount;
                if (mount.SpaceId != space.Id)
                {
                    continue;
                }

                var access = MountPermissionPolicy.Evaluate(snapshot, context.UserId, mount).Permissions;
                if (!access.Contains(SpacePermission.MountView))
                {
                    continue;
                }

                var item = new MountSummaryDto(
                    mount.PublicId,
                    mount.Name,
                    mount.Visibility,
                    mount.Status,
                    mount.CreatedAt,
                    mount.UpdatedAt,
                    MountAccessDto.From(access, space.Status, mount.Status));
                accessible.Add((item, candidate.CursorAfter));
                if (accessible.Count > query.Limit)
                {
                    break;
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
        return Result.Ok(new MountPageDto(items, nextCursor, canCreateMount));
    }
}
