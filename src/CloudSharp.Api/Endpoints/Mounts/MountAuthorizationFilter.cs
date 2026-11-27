using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Endpoints.Spaces;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Mounts;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.Endpoints.Mounts;

/// <summary>
/// endpoint가 선언한 Mount 범위 권한을 Discord식 덮어쓰기 규칙으로 평가한다. 성공하면
/// <see cref="AuthorizedMountContext"/>를 요청 범위에 저장하고, 유즈케이스는 이 context만 받는다.
/// 최종 MOUNT_VIEW가 없거나 Mount가 없으면 404, 허용하지 않는 Mount 상태는 409,
/// 요구 권한 부족은 <c>maskPermissionDenied</c>에 따라 403 또는 404다.
/// </summary>
internal sealed class MountAuthorizationFilter : IEndpointFilter
{
    private const string SpaceRouteParameterName = "spacePublicId";
    private const string MountRouteParameterName = "mountPublicId";
    private static readonly object AuthorizedContextKey = new();
    private static readonly MountStatus[] AllMountStatuses = Enum.GetValues<MountStatus>();
    private readonly SpacePermission[] _requiredPermissions;
    private readonly SpaceStatus[] _allowedSpaceStatuses;
    private readonly MountStatus[] _allowedMountStatuses;
    private readonly bool _maskPermissionDenied;

    /// <summary>
    /// <paramref name="requiredPermissions"/>는 Mount 범위 권한이어야 한다. 허용 Mount 상태를 생략하면
    /// 모든 Mount 상태를 허용하고 상태 전이 검사는 도메인에 맡긴다.
    /// </summary>
    internal MountAuthorizationFilter(
        IEnumerable<SpacePermission> requiredPermissions,
        IEnumerable<SpaceStatus> allowedSpaceStatuses,
        bool maskPermissionDenied,
        IEnumerable<MountStatus>? allowedMountStatuses = null)
    {
        ArgumentNullException.ThrowIfNull(requiredPermissions);
        ArgumentNullException.ThrowIfNull(allowedSpaceStatuses);
        _requiredPermissions = requiredPermissions.ToArray();
        _allowedSpaceStatuses = allowedSpaceStatuses.ToArray();
        _allowedMountStatuses = allowedMountStatuses?.ToArray() ?? AllMountStatuses;
        if (_requiredPermissions.Any(permission => !MountScopedPermissions.Contains(permission)))
        {
            throw new ArgumentException("Required permissions must be mount-scoped.", nameof(requiredPermissions));
        }

        if (_allowedSpaceStatuses.Length == 0)
        {
            throw new ArgumentException("At least one space status is required.", nameof(allowedSpaceStatuses));
        }

        if (_allowedMountStatuses.Length == 0)
        {
            throw new ArgumentException("At least one mount status is required.", nameof(allowedMountStatuses));
        }

        _maskPermissionDenied = maskPermissionDenied;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var routeValues = httpContext.Request.RouteValues;
        if (!PublicIdParser.TryParse(routeValues[SpaceRouteParameterName] as string, out var spacePublicId))
        {
            return SpaceAuthorizationFilter.InvalidSpacePublicId();
        }

        if (!PublicIdParser.TryParse(routeValues[MountRouteParameterName] as string, out var mountPublicId))
        {
            return InvalidMountPublicId();
        }

        var actor = httpContext.RequestServices.GetRequiredService<ICurrentActorAccessor>().CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");
        var authorizationService = httpContext.RequestServices.GetRequiredService<IMountAuthorizationService>();
        var authorization = await authorizationService.AuthorizeAsync(
            new MountAuthorizationQuery(
                actor,
                spacePublicId,
                mountPublicId,
                _requiredPermissions,
                _allowedSpaceStatuses,
                _allowedMountStatuses),
            httpContext.RequestAborted);
        if (authorization.IsFailed)
        {
            return authorization.ToMountAuthorizationFailure(_maskPermissionDenied);
        }

        httpContext.Items[AuthorizedContextKey] = authorization.Value;
        return await next(context);
    }

    internal static AuthorizedMountContext GetRequiredContext(HttpContext httpContext) =>
        httpContext.Items[AuthorizedContextKey] as AuthorizedMountContext
        ?? throw new InvalidOperationException("An authorized Mount context is required.");

    internal static IResult InvalidMountPublicId() => ErrorHttpResults.Validation(
        MountRouteParameterName,
        ErrorCodes.Mount.PublicIdInvalid,
        "Mount public ID must be an RFC-variant UUIDv7.");
}
