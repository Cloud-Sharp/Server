using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using FluentResults;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Endpoints.Spaces.Mapping;

internal static class SpaceResultHttpMapper
{
    private const string CursorInvalidCode = "CURSOR_INVALID";
    private const string PermissionDeniedCode = "PERMISSION_DENIED";
    private const string PropertyNameMetadataKey = "PropertyName";

    internal static IResult ToSpaceHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        if (result.IsSuccess)
        {
            return onSuccess(result.Value);
        }

        if (result.Errors.Any(error => GetCode(error) == ErrorCodes.Space.ListCursorInvalid))
        {
            return ErrorHttpResults.FromCode(CursorInvalidCode, "The cursor is invalid.");
        }

        var authorizationError = result.Errors.OfType<SpaceAuthorizationError>().FirstOrDefault();
        if (authorizationError is not null)
        {
            return MapAuthorizationError(authorizationError, maskPermissionDenied: false)
                ?? result.ToProtectedHttpResult(onSuccess);
        }

        var spaceError = result.Errors.OfType<SpaceError>()
            .FirstOrDefault(error => !error.Metadata.ContainsKey(PropertyNameMetadataKey));
        return MapSpaceError(spaceError) ?? result.ToProtectedHttpResult(onSuccess);
    }

    internal static IResult ToSpaceAuthorizationFailure(
        this Result<AuthorizedSpaceContext> result,
        bool maskPermissionDenied)
    {
        if (result.IsSuccess)
        {
            throw new ArgumentException("A successful authorization result is not a failure.", nameof(result));
        }

        var authorizationError = result.Errors.OfType<SpaceAuthorizationError>().FirstOrDefault();
        return MapAuthorizationError(authorizationError, maskPermissionDenied)
            ?? new FailureResult(result.Errors);
    }

    /// <summary>
    /// Mount 인가 실패를 Space 인가와 같은 규칙으로 변환한다. MOUNT_VIEW 부재·미존재는 RESOURCE_NOT_FOUND,
    /// 허용하지 않는 Mount 상태는 REQUEST_CONFLICT로 공통 매핑한다.
    /// </summary>
    internal static IResult ToMountAuthorizationFailure(
        this Result<AuthorizedMountContext> result,
        bool maskPermissionDenied)
    {
        if (result.IsSuccess)
        {
            throw new ArgumentException("A successful authorization result is not a failure.", nameof(result));
        }

        var authorizationError = result.Errors.OfType<SpaceAuthorizationError>().FirstOrDefault();
        return MapAuthorizationError(authorizationError, maskPermissionDenied)
            ?? new FailureResult(result.Errors);
    }

    private static IResult? MapSpaceError(SpaceError? error) => GetCode(error) switch
    {
        ErrorCodes.Space.InvalidState => ErrorHttpResults.FromCode(
            ErrorCodes.Space.InvalidState, error!.Message),
        ErrorCodes.Space.QuotaTooSmall => ErrorHttpResults.FromCode(
            ErrorCodes.Space.QuotaTooSmall, error!.Message),
        ErrorCodes.Space.TransferTargetInvalid => ErrorHttpResults.FromCode(
            ErrorCodes.Space.TransferTargetInvalid, error!.Message),
        _ => null,
    };

    private static IResult? MapAuthorizationError(
        SpaceAuthorizationError? error,
        bool maskPermissionDenied)
    {
        if (error is null)
        {
            return null;
        }

        if (error.ActualSpaceStatus is SpaceStatus.Deleting or SpaceStatus.Deleted)
        {
            return NotFound();
        }

        return GetCode(error) switch
        {
            ErrorCodes.Auth.SpaceNotFound or ErrorCodes.Auth.SpaceAccessDenied => NotFound(),
            ErrorCodes.Auth.SpacePermissionDenied => PermissionDenied(maskPermissionDenied),
            ErrorCodes.Auth.SpaceStateNotAllowed when error.ActualSpaceStatus == SpaceStatus.Archived =>
                ErrorHttpResults.FromCode(ErrorCodes.Space.Archived, "Space is archived."),
            ErrorCodes.Auth.SpaceStateNotAllowed => ErrorHttpResults.FromCode(
                ErrorCodes.Space.InvalidState, error.Message),
            _ => null,
        };
    }

    internal static IResult NotFound() => ErrorHttpResults.ResourceNotFound();

    internal static IResult PermissionDenied(bool maskPermissionDenied) =>
        maskPermissionDenied ? NotFound() : ErrorHttpResults.FromCode(PermissionDeniedCode, "Permission denied.");

    private static string? GetCode(IError? error) =>
        error is not null
        && error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var value)
            ? value as string
            : null;
}
