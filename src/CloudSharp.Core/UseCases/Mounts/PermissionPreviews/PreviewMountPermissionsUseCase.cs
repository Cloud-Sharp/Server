using CloudSharp.Core.Abstractions.Persistence;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Results;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.Policies.Authorization;
using CloudSharp.Core.UseCases.Mounts.Dtos;
using FluentResults;

namespace CloudSharp.Core.UseCases.Mounts.PermissionPreviews;

public sealed class PreviewMountPermissionsUseCase : IUseCase<PreviewMountPermissionsQuery, MountPermissionPreviewPageDto>
{
    private ISpaceAuthorizationReader AuthorizationReader { get; } = null!;
    private IMountReader MountReader { get; } = null!;
    private IMountPreviewUserReader UserReader { get; } = null!;

    public PreviewMountPermissionsUseCase() { }

    public PreviewMountPermissionsUseCase(
        ISpaceAuthorizationReader authorizationReader,
        IMountReader mountReader,
        IMountPreviewUserReader userReader)
    {
        AuthorizationReader = authorizationReader;
        MountReader = mountReader;
        UserReader = userReader;
    }

    public async Task<Result<MountPermissionPreviewPageDto>> ExecuteAsync(
        PreviewMountPermissionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = new PreviewMountPermissionsQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            return validation.ToFailureResult<MountPermissionPreviewPageDto>();
        }

        if (AuthorizationReader is null || MountReader is null || UserReader is null)
        {
            return Result.Fail<MountPermissionPreviewPageDto>(CommonError.DependencyUnavailable());
        }

        var context = query.Mount;
        var snapshotResult = await MountWorkflowReads.ReadSpaceAsync(
            AuthorizationReader, context.Space, cancellationToken);
        if (snapshotResult.IsFailed)
        {
            return Result.Fail<MountPermissionPreviewPageDto>(snapshotResult.Errors);
        }

        var space = snapshotResult.Value.Space;
        var mountResult = await MountReader.ReadAsync(space.Id, context.MountPublicId, cancellationToken);
        if (mountResult.IsFailed)
        {
            return Result.Fail<MountPermissionPreviewPageDto>(mountResult.Errors);
        }

        var mount = mountResult.Value;
        if (mount is null || mount.Id != context.MountId || mount.SpaceId != space.Id)
        {
            return Result.Fail<MountPermissionPreviewPageDto>(CommonError.ResourceNotFound());
        }

        return query.UserPublicId is { } userPublicId
            ? await PreviewUserAsync(query, space, mount, userPublicId, cancellationToken)
            : await PreviewCandidatesAsync(query, space, mount, cancellationToken);
    }

    private async Task<Result<MountPermissionPreviewPageDto>> PreviewUserAsync(
        PreviewMountPermissionsQuery query,
        Space space,
        Mount mount,
        Guid userPublicId,
        CancellationToken cancellationToken)
    {
        var subjectResult = await UserReader.ReadUserAsync(space.Id, userPublicId, cancellationToken);
        if (subjectResult.IsFailed)
        {
            return Result.Fail<MountPermissionPreviewPageDto>(subjectResult.Errors);
        }

        var subject = subjectResult.Value;
        if (subject is null
            || subject.User.Status != UserStatus.Active
            || !MountPermissionPolicy.HasSpaceAccess(space, subject.User.Id, subject.Membership))
        {
            return Result.Fail<MountPermissionPreviewPageDto>(CommonError.ResourceNotFound());
        }

        var current = EvaluateSubject(space, mount, subject);
        var preview = query.Visibility.HasValue
            ? EvaluateSubject(space, mount, subject, query.Visibility)
            : current;
        var item = ToPreviewDto(subject.User.PublicId, current, preview);
        return Result.Ok(new MountPermissionPreviewPageDto(new[] { item }, null));
    }

    private async Task<Result<MountPermissionPreviewPageDto>> PreviewCandidatesAsync(
        PreviewMountPermissionsQuery query,
        Space space,
        Mount mount,
        CancellationToken cancellationToken)
    {
        var changed = new List<(MountPermissionPreviewDto Item, string Cursor)>();
        var traversalCursor = query.Cursor;
        var traversedCursors = new HashSet<string>(StringComparer.Ordinal);
        if (traversalCursor is not null)
        {
            traversedCursors.Add(traversalCursor);
        }

        while (true)
        {
            var readResult = await UserReader.ReadCandidatesAsync(
                space.Id,
                traversalCursor,
                query.Limit + 1,
                cancellationToken);
            if (readResult.IsFailed)
            {
                return Result.Fail<MountPermissionPreviewPageDto>(readResult.Errors);
            }

            var page = readResult.Value;
            if (page.Candidates.Count == 0)
            {
                if (page.HasMore)
                {
                    throw new InvalidOperationException(
                        "Mount preview reader returned HasMore with an empty candidate batch.");
                }

                break;
            }

            var continuationCursor = page.Candidates[^1].CursorAfter;
            if (string.IsNullOrWhiteSpace(continuationCursor))
            {
                throw new InvalidOperationException("Mount preview reader returned a blank continuation cursor.");
            }

            if (!traversedCursors.Add(continuationCursor))
            {
                throw new InvalidOperationException("Mount preview reader repeated a continuation cursor.");
            }

            foreach (var candidate in page.Candidates)
            {
                var subject = candidate.Subject;
                if (subject.User.Status != UserStatus.Active
                    || !MountPermissionPolicy.HasSpaceAccess(space, subject.User.Id, subject.Membership))
                {
                    continue;
                }

                var current = EvaluateSubject(space, mount, subject);
                var preview = EvaluateSubject(space, mount, subject, query.Visibility);
                if (current.Permissions.SetEquals(preview.Permissions))
                {
                    continue;
                }

                changed.Add((ToPreviewDto(subject.User.PublicId, current, preview), candidate.CursorAfter));
                if (changed.Count > query.Limit)
                {
                    break;
                }
            }

            if (changed.Count > query.Limit || !page.HasMore)
            {
                break;
            }

            traversalCursor = continuationCursor;
        }

        var items = changed.Take(query.Limit).Select(entry => entry.Item).ToArray();
        var nextCursor = changed.Count > query.Limit
            ? changed[query.Limit - 1].Cursor
            : null;
        return Result.Ok(new MountPermissionPreviewPageDto(items, nextCursor));
    }

    /// <summary>myAccess와 같은 기준으로 현재 Space·Mount 상태에서 행사할 수 있는 권한을 반환한다.</summary>
    private static MountPermissionEvaluation EvaluateSubject(
        Space space,
        Mount mount,
        MountPreviewUserSnapshot subject,
        MountVisibility? visibility = null)
    {
        var evaluation = MountPermissionPolicy.Evaluate(
            space,
            subject.User.Id,
            subject.Membership,
            subject.Roles,
            mount,
            visibility);
        return evaluation with
        {
            Permissions = MountPermissionPolicy.ExercisablePermissions(
                evaluation.Permissions, space.Status, mount.Status),
        };
    }

    private static MountPermissionPreviewDto ToPreviewDto(
        Guid userPublicId,
        MountPermissionEvaluation current,
        MountPermissionEvaluation preview) =>
        new(
            userPublicId,
            current.Permissions,
            preview.Permissions,
            current.Sources.Select(MountPermissionSourceDto.From).ToArray(),
            preview.Sources.Select(MountPermissionSourceDto.From).ToArray());
}
