using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Abstractions.Auth;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.Policies.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Api.Endpoints.Spaces;

internal sealed class SpaceAuthorizationFilter : IEndpointFilter
{
    private const string RouteParameterName = "spacePublicId";
    private static readonly object AuthorizedContextKey = new();
    private readonly SpacePermission[] _requiredPermissions;
    private readonly SpaceStatus[] _allowedSpaceStatuses;
    private readonly bool _maskPermissionDenied;
    private readonly bool _requireOwner;

    internal SpaceAuthorizationFilter(
        IEnumerable<SpacePermission> requiredPermissions,
        IEnumerable<SpaceStatus> allowedSpaceStatuses,
        bool maskPermissionDenied,
        bool requireOwner = false)
    {
        ArgumentNullException.ThrowIfNull(requiredPermissions);
        ArgumentNullException.ThrowIfNull(allowedSpaceStatuses);
        _requiredPermissions = requiredPermissions.ToArray();
        _allowedSpaceStatuses = allowedSpaceStatuses.ToArray();
        if (_allowedSpaceStatuses.Length == 0)
        {
            throw new ArgumentException("At least one space status is required.", nameof(allowedSpaceStatuses));
        }

        _maskPermissionDenied = maskPermissionDenied;
        _requireOwner = requireOwner;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var rawPublicId = httpContext.Request.RouteValues[RouteParameterName] as string;
        if (!PublicIdParser.TryParse(rawPublicId, out var spacePublicId))
        {
            return InvalidSpacePublicId();
        }

        var actor = httpContext.RequestServices.GetRequiredService<ICurrentActorAccessor>().CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");
        var authorizationService = httpContext.RequestServices
            .GetRequiredService<ISpaceAuthorizationService>();
        var authorization = await authorizationService.AuthorizeAsync(
            new SpaceAuthorizationQuery(
                actor,
                spacePublicId,
                _requiredPermissions,
                _allowedSpaceStatuses),
            httpContext.RequestAborted);
        if (authorization.IsFailed)
        {
            return authorization.ToSpaceAuthorizationFailure(_maskPermissionDenied);
        }

        if (_requireOwner && authorization.Value.AuthorizationSource != SpaceAuthorizationSource.Owner)
        {
            return SpaceResultHttpMapper.PermissionDenied(_maskPermissionDenied);
        }

        httpContext.Items[AuthorizedContextKey] = authorization.Value;
        return await next(context);
    }

    internal static AuthorizedSpaceContext GetRequiredContext(HttpContext httpContext) =>
        httpContext.Items[AuthorizedContextKey] as AuthorizedSpaceContext
        ?? throw new InvalidOperationException("An authorized Space context is required.");

    internal static IResult InvalidSpacePublicId() => ErrorHttpResults.Validation(
        RouteParameterName,
        ErrorCodes.Space.PublicIdInvalid,
        "Space public ID must be an RFC-variant UUIDv7.");
}
