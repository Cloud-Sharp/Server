using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Api.Endpoints.Spaces.Requests;
using CloudSharp.Api.Endpoints.Spaces.Responses;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Spaces.Archives;
using CloudSharp.Core.UseCases.Spaces.Creations;
using CloudSharp.Core.UseCases.Spaces.DeletionRequests;
using CloudSharp.Core.UseCases.Spaces.Details;
using CloudSharp.Core.UseCases.Spaces.Dtos;
using CloudSharp.Core.UseCases.Spaces.Listings;
using CloudSharp.Core.UseCases.Spaces.MetadataUpdates;
using CloudSharp.Core.UseCases.Spaces.OwnershipTransfers;
using CloudSharp.Core.UseCases.Spaces.QuotaChanges;
using CloudSharp.Core.UseCases.Spaces.QuotaReads;
using CloudSharp.Core.UseCases.Spaces.Reactivations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Spaces;

public static class SpaceEndpoints
{
    private const string JsonMediaType = "application/json";
    private static readonly SpaceStatus[] ReadStatuses = [SpaceStatus.Active, SpaceStatus.Archived];
    private static readonly SpaceStatus[] ActiveOnly = [SpaceStatus.Active];
    private static readonly SpaceStatus[] ArchivedOnly = [SpaceStatus.Archived];

    public static IEndpointRouteBuilder MapSpaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v2/spaces")
            .WithTags("Spaces")
            .WithMetadata(NoStoreResponseMetadata.Instance)
            .RequireAuthorization(SessionAuthenticationDefaults.UserSessionOnlyPolicy);

        group.MapGet("", ListSpacesAsync)
            .WithName("ListSpaces")
            .WithSummary("현재 Session이 접근할 수 있는 Space 목록을 조회합니다.")
            .Produces<SpacePageResponse>(StatusCodes.Status200OK, JsonMediaType)
            .ProducesValidationProblem()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPost("", CreateSpaceAsync)
            .WithName("CreateSpace")
            .WithSummary("현재 Session 소유의 Space와 관리형 Role을 생성합니다.")
            .Produces<SpaceResponse>(StatusCodes.Status201Created, JsonMediaType)
            .ProducesValidationProblem()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapGet("/{spacePublicId}", GetSpaceAsync)
            .WithName("GetSpace")
            .WithSummary("Space 상세와 현재 ETag를 조회합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.SpaceView], ReadStatuses, maskPermissionDenied: true))
            .Produces<SpaceResponse>(StatusCodes.Status200OK, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPatch("/{spacePublicId}", UpdateSpaceAsync)
            .WithName("UpdateSpace")
            .WithSummary("Space 이름과 설명을 부분 수정합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.SpaceUpdate], ActiveOnly, maskPermissionDenied: false))
            .Produces<SpaceResponse>(StatusCodes.Status200OK, JsonMediaType)
            .ProducesValidationProblem()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPost("/{spacePublicId}/archive", ArchiveSpaceAsync)
            .WithName("ArchiveSpace")
            .WithSummary("활성 Space를 보관합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.SpaceUpdate], ActiveOnly, maskPermissionDenied: false))
            .Produces<SpaceResponse>(StatusCodes.Status200OK, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPost("/{spacePublicId}/reactivate", ReactivateSpaceAsync)
            .WithName("ReactivateSpace")
            .WithSummary("보관된 Space를 다시 활성화합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.SpaceUpdate], ArchivedOnly, maskPermissionDenied: false))
            .Produces<SpaceResponse>(StatusCodes.Status200OK, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapDelete("/{spacePublicId}", DeleteSpaceAsync)
            .WithName("RequestSpaceDeletion")
            .WithSummary("Space 삭제를 요청하고 DELETING 상태로 전환합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [], ReadStatuses, maskPermissionDenied: false))
            .Produces<SpaceDeletionAcceptedResponse>(StatusCodes.Status202Accepted, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapGet("/{spacePublicId}/quota", GetSpaceQuotaAsync)
            .WithName("GetSpaceQuota")
            .WithSummary("Space quota와 현재 Space ETag를 조회합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.SpaceView], ReadStatuses, maskPermissionDenied: true))
            .Produces<SpaceQuotaResponse>(StatusCodes.Status200OK, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPatch("/{spacePublicId}/quota", ChangeSpaceQuotaAsync)
            .WithName("ChangeSpaceQuota")
            .WithSummary("OWNER가 Space quota를 변경합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [], ActiveOnly, maskPermissionDenied: false))
            .Produces<SpaceQuotaResponse>(StatusCodes.Status200OK, JsonMediaType)
            .ProducesValidationProblem()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        group.MapPost("/{spacePublicId}/ownership-transfers", TransferSpaceOwnershipAsync)
            .WithName("TransferSpaceOwnership")
            .WithSummary("OWNER가 활성 Membership 사용자에게 Space 소유권을 이전합니다.")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [], ActiveOnly, maskPermissionDenied: false))
            .Produces<SpaceResponse>(StatusCodes.Status200OK, JsonMediaType)
            .ProducesValidationProblem()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status403Forbidden, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, JsonMediaType)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, JsonMediaType);

        return endpoints;
    }

    private static async Task<IResult> ListSpacesAsync(
        [AsParameters] ListSpacesRequest request,
        IUseCase<ListSpacesQuery, SpacePageDto> useCase,
        ICurrentActorAccessor currentActorAccessor,
        CancellationToken cancellationToken)
    {
        var actor = GetRequiredActor(currentActorAccessor);
        var query = new ListSpacesQuery(
            actor,
            request.Cursor,
            request.Limit,
            request.Sort,
            string.Equals(request.Direction, "ASC", StringComparison.Ordinal),
            AllowSystemAdminBypass: false);
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToSpaceHttpResult(value => Results.Ok(SpaceResponseMapper.ToResponse(value)));
    }

    private static async Task<IResult> CreateSpaceAsync(
        CreateSpaceRequest request,
        CreateSpaceUseCase useCase,
        ICurrentActorAccessor currentActorAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var actor = GetRequiredActor(currentActorAccessor);
        var command = new CreateSpaceCommand(
            actor,
            request.Name,
            request.Description,
            request.AllowedBytes);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToSpaceHttpResult(value =>
        {
            var response = SpaceResponseMapper.ToResponse(value.Space);
            SetETag(httpContext, value.Version);
            return Results.Created(SpacePath(response.SpacePublicId), response);
        });
    }

    private static async Task<IResult> GetSpaceAsync(
        IUseCase<GetSpaceQuery, SpaceResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var result = await useCase.ExecuteAsync(new GetSpaceQuery(context), cancellationToken);
        return result.ToSpaceHttpResult(value => SpaceOk(httpContext, value));
    }

    private static async Task<IResult> UpdateSpaceAsync(
        UpdateSpaceRequest request,
        IUseCase<UpdateSpaceCommand, SpaceResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var description = request.HasDescription
            ? new SpaceDescriptionChange(request.Description)
            : null;
        var command = new UpdateSpaceCommand(context, expectedVersion, request.Name, description);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToSpaceHttpResult(value => SpaceOk(httpContext, value));
    }

    private static async Task<IResult> ArchiveSpaceAsync(
        IUseCase<ArchiveSpaceCommand, SpaceResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var result = await useCase.ExecuteAsync(
            new ArchiveSpaceCommand(context, expectedVersion), cancellationToken);
        return result.ToSpaceHttpResult(value => SpaceOk(httpContext, value));
    }

    private static async Task<IResult> ReactivateSpaceAsync(
        IUseCase<ReactivateSpaceCommand, SpaceResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var result = await useCase.ExecuteAsync(
            new ReactivateSpaceCommand(context, expectedVersion), cancellationToken);
        return result.ToSpaceHttpResult(value => SpaceOk(httpContext, value));
    }

    private static async Task<IResult> DeleteSpaceAsync(
        IUseCase<RequestSpaceDeletionCommand, SpaceDeletionAcceptedDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var result = await useCase.ExecuteAsync(
            new RequestSpaceDeletionCommand(context, expectedVersion), cancellationToken);
        return result.ToSpaceHttpResult(value =>
        {
            SetETag(httpContext, value.Version);
            return Results.Accepted(value: SpaceResponseMapper.ToResponse(value));
        });
    }

    private static async Task<IResult> GetSpaceQuotaAsync(
        IUseCase<GetSpaceQuotaQuery, SpaceQuotaDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var result = await useCase.ExecuteAsync(new GetSpaceQuotaQuery(context), cancellationToken);
        return result.ToSpaceHttpResult(value => QuotaOk(httpContext, value));
    }

    private static async Task<IResult> ChangeSpaceQuotaAsync(
        ChangeSpaceQuotaRequest request,
        IUseCase<ChangeSpaceQuotaCommand, SpaceQuotaDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var command = new ChangeSpaceQuotaCommand(context, expectedVersion, request.AllowedBytes);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToSpaceHttpResult(value => QuotaOk(httpContext, value));
    }

    private static async Task<IResult> TransferSpaceOwnershipAsync(
        TransferSpaceOwnershipRequest request,
        IUseCase<TransferSpaceOwnershipCommand, SpaceResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var context = SpaceAuthorizationFilter.GetRequiredContext(httpContext);
        var command = new TransferSpaceOwnershipCommand(
            context, expectedVersion, request.NewOwnerUserPublicId);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToSpaceHttpResult(value => SpaceOk(httpContext, value));
    }

    private static CurrentActor GetRequiredActor(ICurrentActorAccessor currentActorAccessor) =>
        currentActorAccessor.CurrentActor
        ?? throw new InvalidOperationException("An authenticated Session actor is required.");

    private static IResult SpaceOk(HttpContext httpContext, SpaceResultDto result)
    {
        SetETag(httpContext, result.Version);
        return Results.Ok(SpaceResponseMapper.ToResponse(result.Space));
    }

    private static IResult QuotaOk(HttpContext httpContext, SpaceQuotaDto quota)
    {
        SetETag(httpContext, quota.Version);
        return Results.Ok(SpaceResponseMapper.ToResponse(quota));
    }

    private static void SetETag(HttpContext httpContext, long version) =>
        httpContext.Response.Headers["ETag"] = ETagFormatter.Format(version);

    private static string SpacePath(Guid spacePublicId) => $"/api/v2/spaces/{spacePublicId:D}";
}
