using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Memberships.Mapping;
using CloudSharp.Api.Endpoints.Memberships.Requests;
using CloudSharp.Api.Endpoints.Memberships.Responses;
using CloudSharp.Api.Endpoints.Spaces;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Domain.Memberships;
using CloudSharp.Core.Domain.Spaces;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Memberships.Activations;
using CloudSharp.Core.UseCases.Memberships.Details;
using CloudSharp.Core.UseCases.Memberships.Dtos;
using CloudSharp.Core.UseCases.Memberships.Leaves;
using CloudSharp.Core.UseCases.Memberships.Listings;
using CloudSharp.Core.UseCases.Memberships.Removals;
using CloudSharp.Core.UseCases.Memberships.RoleListings;
using CloudSharp.Core.UseCases.Memberships.RoleReplacements;
using CloudSharp.Core.UseCases.Memberships.Suspensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Memberships;

internal static class MembershipEndpoints
{
    private const string MembershipRouteKey = "membershipPublicId";
    private static readonly object MembershipPublicIdKey = new();

    internal static void MapMembershipEndpoints(this RouteGroupBuilder spaces)
    {
        var space = spaces.MapGroup("/{spacePublicId}");
        var authorizeRead = new SpaceAuthorizationFilter(
            [SpacePermission.MembersView],
            [SpaceStatus.Active, SpaceStatus.Archived],
            maskPermissionDenied: false);
        var reads = space.MapGroup("").AddEndpointFilter(authorizeRead);
        var member = space.MapGroup("/memberships/{membershipPublicId}")
            .AddEndpointFilter(ValidateMembershipRouteAsync);
        var changes = member.MapGroup("")
            .AddEndpointFilter(new SpaceAuthorizationFilter(
                [SpacePermission.MembersManage],
                [SpaceStatus.Active],
                maskPermissionDenied: false));

        reads.MapGet("/memberships", ListMembershipsAsync)
            .WithName("ListSpaceMemberships")
            .WithSummary("Space 멤버 목록을 조회합니다.")
            .Produces<SpaceMembershipPageResponse>()
            .ProducesMembershipErrors();
        member.MapGet("", GetMembershipAsync)
            .AddEndpointFilter(authorizeRead)
            .WithName("GetSpaceMembership")
            .WithSummary("Space 멤버 상세와 Membership ETag를 조회합니다.")
            .Produces<SpaceMembershipResponse>()
            .ProducesMembershipErrors();
        reads.MapGet("/roles", ListRolesAsync)
            .WithName("ListSpaceRoles")
            .WithSummary("Space의 활성 관리형 Role과 권한을 조회합니다.")
            .Produces<SpaceRoleResponse[]>()
            .ProducesMembershipErrors();

        changes.MapPut("/roles", ReplaceRolesAsync)
            .WithName("ReplaceSpaceMemberRoles")
            .WithSummary("Space 멤버의 Role 집합을 교체합니다.")
            .Produces<SpaceMembershipResponse>()
            .ProducesMutationErrors()
            .Produces<ErrorResponse>(StatusCodes.Status415UnsupportedMediaType);
        changes.MapPost("/suspend", SuspendMemberAsync)
            .WithName("SuspendSpaceMember")
            .WithSummary("활성 Space 멤버를 정지합니다.")
            .Produces<SpaceMembershipResponse>()
            .ProducesMutationErrors();
        changes.MapPost("/activate", ActivateMemberAsync)
            .WithName("ActivateSpaceMember")
            .WithSummary("정지된 Space 멤버를 활성화합니다.")
            .Produces<SpaceMembershipResponse>()
            .ProducesMutationErrors();
        changes.MapDelete("", RemoveMemberAsync)
            .WithName("RemoveSpaceMember")
            .WithSummary("Space 멤버를 제거합니다.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesMutationErrors();

        space.MapDelete("/membership", LeaveSpaceAsync)
            .WithName("LeaveSpace")
            .WithSummary("현재 사용자가 Space에서 탈퇴합니다.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesMembershipErrors()
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict);
    }

    private static RouteHandlerBuilder ProducesMembershipErrors(this RouteHandlerBuilder endpoint) => endpoint
        .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
        .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
        .Produces<ErrorResponse>(StatusCodes.Status403Forbidden)
        .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
        .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

    private static RouteHandlerBuilder ProducesMutationErrors(this RouteHandlerBuilder endpoint) => endpoint
        .ProducesMembershipErrors()
        .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
        .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed)
        .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired);

    private static async Task<IResult> ListMembershipsAsync(
        [AsParameters] ListSpaceMembershipsRequest request,
        IUseCase<ListSpaceMembershipsQuery, SpaceMembershipPageDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = new ListSpaceMembershipsQuery(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext),
            request.Cursor,
            request.Limit,
            request.Sort,
            Descending: request.Direction == "DESC");
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToSpaceHttpResult(page => Results.Ok(MembershipResponseMapper.ToResponse(page)));
    }

    private static async Task<IResult> GetMembershipAsync(
        IUseCase<GetSpaceMembershipQuery, SpaceMembershipResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var query = new GetSpaceMembershipQuery(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetMembershipPublicId(httpContext));
        var result = await useCase.ExecuteAsync(query, cancellationToken);
        return result.ToMembershipHttpResult(value => MembershipOk(httpContext, value));
    }

    private static async Task<IResult> ListRolesAsync(
        IUseCase<ListSpaceRolesQuery, IReadOnlyList<SpaceRoleDto>> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(
            new ListSpaceRolesQuery(SpaceAuthorizationFilter.GetRequiredContext(httpContext)), cancellationToken);
        return result.ToSpaceHttpResult(roles => Results.Ok(roles.Select(MembershipResponseMapper.ToResponse).ToArray()));
    }

    private static async Task<IResult> ReplaceRolesAsync(
        ReplaceSpaceMemberRolesRequest request,
        IUseCase<ReplaceSpaceMemberRolesCommand, SpaceMembershipResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        if (request.RolePublicIds is not { } rolePublicIds)
        {
            return ErrorHttpResults.Validation(
                "rolePublicIds",
                ErrorCodes.SpaceMembership.RoleInvalid,
                "Role public IDs must be an array of RFC-variant UUIDv7 values.");
        }

        var command = new ReplaceSpaceMemberRolesCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetMembershipPublicId(httpContext), version, rolePublicIds);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMembershipHttpResult(value => MembershipOk(httpContext, value), maskInvalidRole: true);
    }

    private static async Task<IResult> SuspendMemberAsync(
        IUseCase<SuspendSpaceMemberCommand, SpaceMembershipResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new SuspendSpaceMemberCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetMembershipPublicId(httpContext), version);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMembershipHttpResult(value => MembershipOk(httpContext, value));
    }

    private static async Task<IResult> ActivateMemberAsync(
        IUseCase<ActivateSpaceMemberCommand, SpaceMembershipResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new ActivateSpaceMemberCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetMembershipPublicId(httpContext), version);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMembershipHttpResult(value => MembershipOk(httpContext, value));
    }

    private static async Task<IResult> RemoveMemberAsync(
        IUseCase<RemoveSpaceMemberCommand, RemoveSpaceMemberResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!IfMatchETag.TryRead(httpContext.Request, out var version, out var error))
        {
            return error!;
        }

        var command = new RemoveSpaceMemberCommand(
            SpaceAuthorizationFilter.GetRequiredContext(httpContext), GetMembershipPublicId(httpContext), version);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToMembershipHttpResult(value =>
        {
            httpContext.Response.Headers.ETag = ETagFormatter.Format(value.Version);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> LeaveSpaceAsync(
        string spacePublicId,
        ICurrentActorAccessor currentActorAccessor,
        IUseCase<LeaveSpaceCommand, LeaveSpaceResultDto> useCase,
        CancellationToken cancellationToken)
    {
        if (!PublicIdParser.TryParse(spacePublicId, out var publicId))
        {
            return SpaceAuthorizationFilter.InvalidSpacePublicId();
        }

        var actor = currentActorAccessor.CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");
        var result = await useCase.ExecuteAsync(new LeaveSpaceCommand(actor, publicId), cancellationToken);
        return result.ToMembershipHttpResult(_ => Results.NoContent());
    }

    private static ValueTask<object?> ValidateMembershipRouteAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var rawPublicId = httpContext.Request.RouteValues[MembershipRouteKey] as string;
        if (!PublicIdParser.TryParse(rawPublicId, out var publicId))
        {
            return ValueTask.FromResult<object?>(ErrorHttpResults.Validation(
                MembershipRouteKey,
                ErrorCodes.SpaceMembership.PublicIdInvalid,
                "Membership public ID must be an RFC-variant UUIDv7."));
        }

        httpContext.Items[MembershipPublicIdKey] = publicId;
        return next(context);
    }

    private static Guid GetMembershipPublicId(HttpContext httpContext) =>
        (Guid)httpContext.Items[MembershipPublicIdKey]!;

    private static IResult MembershipOk(HttpContext httpContext, SpaceMembershipResultDto result)
    {
        httpContext.Response.Headers.ETag = ETagFormatter.Format(result.Version);
        return Results.Ok(MembershipResponseMapper.ToResponse(result.Membership));
    }
}
