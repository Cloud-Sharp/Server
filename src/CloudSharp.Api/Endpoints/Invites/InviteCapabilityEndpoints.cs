using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Idempotency;
using CloudSharp.Api.Endpoints.Invites.Responses;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Idempotency.Execution;
using CloudSharp.Core.UseCases.Invites.Acceptances;
using CloudSharp.Core.UseCases.Invites.Dtos;
using CloudSharp.Core.UseCases.Invites.PublicInfo;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Invites;

internal static class InviteCapabilityEndpoints
{
    internal static IEndpointRouteBuilder MapInviteCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(InviteCapabilityProtectionMiddleware.PublicRoute, GetPublicInviteAsync)
            .AllowAnonymous()
            .WithName("GetPublicSpaceInvite")
            .WithSummary("초대 링크로 공개 가입 정보를 조회합니다.")
            .Produces<PublicSpaceInviteResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost(InviteCapabilityProtectionMiddleware.AcceptRoute, AcceptInviteAsync)
            .RequireAuthorization(SessionAuthenticationDefaults.UserSessionOnlyPolicy)
            .RequireIdempotency(InviteIdempotency.AcceptPolicy)
            .WithName("AcceptSpaceInvite")
            .WithSummary("초대 링크로 Space에 가입합니다.")
            .Produces<AcceptSpaceInviteResponse>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static async Task<IResult> GetPublicInviteAsync(
        string inviteToken,
        IUseCase<GetPublicSpaceInviteQuery, PublicSpaceInviteDto> useCase,
        CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(new GetPublicSpaceInviteQuery(inviteToken), cancellationToken);
        return ToCapabilityResult(result, invite => Results.Ok(new PublicSpaceInviteResponse(
            invite.SpaceName, invite.RoleName, invite.ExpiresAt)));
    }

    private static async Task<IResult> AcceptInviteAsync(
        string inviteToken,
        HttpContext httpContext,
        ICurrentActorAccessor currentActorAccessor,
        AcceptSpaceInviteUseCase useCase,
        IdempotentRequestExecutor executor,
        CancellationToken cancellationToken)
    {
        var actor = currentActorAccessor.CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");
        var command = new AcceptSpaceInviteCommand(actor, inviteToken);
        var result = await IdempotencyHttp.ExecuteLoggedAsync<AcceptSpaceInviteCommand, AcceptSpaceInviteResultDto, AcceptSpaceInviteUseCase>(
            httpContext,
            ct => useCase.ExecuteIdempotentAsync(command, executor, IdempotencyHttp.TakeReservation(httpContext),
                InviteIdempotency.AcceptPolicy.ResultVersion, ct),
            cancellationToken);
        return ToCapabilityResult(result, execution => execution switch
        {
            IdempotentExecutionResult<AcceptSpaceInviteResultDto>.Executed executed => InviteIdempotency.Accepted(executed.Value),
            IdempotentExecutionResult<AcceptSpaceInviteResultDto>.Recovered recovered =>
                IdempotencyHttp.Replay(httpContext, recovered.Completion),
            _ => throw new InvalidOperationException("Unknown idempotent execution result."),
        });
    }

    private static IResult ToCapabilityResult<T>(Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsFailed && result.Errors.Any(error =>
                error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
                && code is ErrorCodes.SpaceInvite.NotFound
                    or ErrorCodes.SpaceInvite.TokenInvalid
                    or ErrorCodes.SpaceInvite.Revoked
                    or ErrorCodes.SpaceInvite.Expired
                    or ErrorCodes.SpaceInvite.UsageExhausted
                    or ErrorCodes.SpaceMembership.RoleInvalid))
        {
            return SpaceResultHttpMapper.NotFound();
        }

        return result.ToSpaceHttpResult(onSuccess);
    }
}
