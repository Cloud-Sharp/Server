using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Invites.Mapping;
using CloudSharp.Api.Endpoints.Invites.Requests;
using CloudSharp.Api.Endpoints.Invites.Responses;
using CloudSharp.Api.Endpoints.Spaces;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Invites.Creations;
using CloudSharp.Core.UseCases.Invites.Details;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.Listings;
using CloudSharp.Core.UseCases.Invites.Revocations;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Invites;

internal static class InviteManagementEndpoints
{
    private const string InviteRouteKey = "invitePublicId";
    private static readonly object InvitePublicIdContextKey = new();

    internal static void MapInviteManagementEndpoints(this RouteGroupBuilder spaces)
    {
        var invites = spaces.MapGroup("/{spacePublicId}/invites");
        var invite = invites.MapGroup("/{invitePublicId}").AddEndpointFilter(ValidateInviteRouteAsync);
        var authorizeRead = new SpaceAuthorizationFilter(
            [SpacePermission.InvitesManage], [SpaceStatus.Active, SpaceStatus.Archived], maskPermissionDenied: false);
        var authorizeWrite = new SpaceAuthorizationFilter(
            [SpacePermission.InvitesManage], [SpaceStatus.Active], maskPermissionDenied: false);

        invites.MapGet("", ListInvitesAsync)
            .AddEndpointFilter(authorizeRead)
            .WithName("ListSpaceInvites")
            .WithSummary("Space의 활성 초대 목록을 조회합니다.")
            .WithDescription(InviteHttpContract.ManagementDescription)
            .Produces<SpaceInvitePageResponse>()
            .ProducesManagementErrors();
        invites.MapPost("", CreateInviteAsync)
            .AddEndpointFilter(authorizeWrite)
            .WithName("CreateSpaceInvite")
            .WithSummary("Space 초대 링크를 생성합니다.")
            .WithDescription(InviteHttpContract.ManagementDescription + " " + InviteHttpContract.IdempotencyDescription)
            .Produces<CreateSpaceInviteResponse>(StatusCodes.Status201Created)
            .ProducesManagementErrors()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status415UnsupportedMediaType);
        invite.MapGet("", GetInviteAsync)
            .AddEndpointFilter(authorizeRead)
            .WithName("GetSpaceInvite")
            .WithSummary("Space 초대의 저장 상태와 ETag를 조회합니다.")
            .WithDescription(InviteHttpContract.ManagementDescription)
            .Produces<SpaceInviteResponse>()
            .ProducesManagementErrors();
        invite.MapDelete("", RevokeInviteAsync)
            .AddEndpointFilter(authorizeWrite)
            .WithName("RevokeSpaceInvite")
            .WithSummary("Space 초대를 폐기합니다.")
            .WithDescription(InviteHttpContract.ManagementDescription)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesManagementErrors()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired);
    }

    private static RouteHandlerBuilder ProducesManagementErrors(this RouteHandlerBuilder endpoint) => endpoint
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> ListInvitesAsync(
        [AsParameters] ListSpaceInvitesRequest request,
        IUseCase<ListSpaceInvitesQuery, SpaceInvitePageDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = new ListSpaceInvitesQuery(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext),
            request.Cursor,
            request.Limit,
            request.Sort,
            Descending: request.Direction == "DESC");
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToSpaceHttpResult(page => Results.Ok(InviteManagementResponseMapper.ToResponse(page)));
    }

    private static async Task<IResult> CreateInviteAsync(
        CreateSpaceInviteRequest request,
        IUseCase<CreateSpaceInviteCommand, CreateSpaceInviteResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (InviteHttpContract.ValidateIdempotencyKey(httpContext.Request) is { } error)
        {
            return error;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var command = new CreateSpaceInviteCommand(context, request.ExpiresAt, request.MaxUses);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToSpaceHttpResult(created =>
        {
            httpContext.Response.Headers.ETag = ETagFormatter.Format(created.Version);
            var location = $"/api/v2/spaces/{context.SpacePublicId:D}/invites/{created.InvitePublicId:D}";
            return Results.Created(location, InviteManagementResponseMapper.ToResponse(created));
        });
    }

    private static async Task<IResult> GetInviteAsync(
        IUseCase<GetSpaceInviteQuery, SpaceInviteResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = new GetSpaceInviteQuery(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetInvitePublicId(httpContext));
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return ToManagementHttpResult(result, detail =>
        {
            httpContext.Response.Headers.ETag = ETagFormatter.Format(detail.Version);
            return Results.Ok(InviteManagementResponseMapper.ToResponse(detail.Invite));
        });
    }

    private static async Task<IResult> RevokeInviteAsync(
        IUseCase<RevokeSpaceInviteCommand, RevokeSpaceInviteResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new RevokeSpaceInviteCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetInvitePublicId(httpContext), version);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return ToManagementHttpResult(result, revoked =>
        {
            httpContext.Response.Headers.ETag = ETagFormatter.Format(revoked.Version);
            return Results.NoContent();
        });
    }

    private static ValueTask<object?> ValidateInviteRouteAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var rawPublicId = httpContext.Request.RouteValues[InviteRouteKey] as string;
        if (!PublicIdParser.TryParse(rawPublicId, out var publicId))
        {
            return ValueTask.FromResult<object?>(ErrorHttpResults.Validation(
                InviteRouteKey, ErrorCodes.SpaceInvite.PublicIdInvalid, "Invite public ID must be an RFC-variant UUIDv7."));
        }

        httpContext.Items[InvitePublicIdContextKey] = publicId;
        return next(context);
    }

    private static Guid GetInvitePublicId(HttpContext context) => (Guid)context.Items[InvitePublicIdContextKey]!;

    private static IResult ToManagementHttpResult<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsFailed && result.Errors.OfType<SpaceInviteError>().Any(error =>
                error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
                && Equals(code, ErrorCodes.SpaceInvite.NotFound)))
        {
            return SpaceResultHttpMapper.NotFound();
        }

        return result.ToSpaceHttpResult(onSuccess);
    }
}
