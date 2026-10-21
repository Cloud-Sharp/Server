using CloudSharp.Api.Auth.Authentication;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Users.Mapping;
using CloudSharp.Api.Endpoints.Users.Requests;
using CloudSharp.Api.Endpoints.Users.Responses;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.UseCases.Auth.PasswordChanges;
using CloudSharp.Core.UseCases.Users.Profiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Users;

public static class UserEndpoints
{
    private const string CacheControlNoStore = "no-store";

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v2/me")
            .WithTags("Users")
            .WithMetadata(NoStoreResponseMetadata.Instance)
            .RequireAuthorization(SessionAuthenticationDefaults.UserSessionOnlyPolicy);

        group.MapGet("", GetMeAsync)
            .WithName("GetMyProfile")
            .WithSummary("현재 사용자의 프로필과 ETag를 조회합니다.")
            .Produces<UserProfileResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json");

        group.MapPut("/password", ChangePasswordAsync)
            .WithName("ChangeMyPassword")
            .WithSummary("현재 사용자의 비밀번호를 변경합니다.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status412PreconditionFailed, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status428PreconditionRequired, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json");

        return endpoints;
    }

    internal static async Task<IResult> GetMeAsync(
        GetProfileUseCase useCase,
        ICurrentActorAccessor currentActorAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["Cache-Control"] = CacheControlNoStore;
        var actor = currentActorAccessor.CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");

        var result = await useCase.ExecuteAsync(new GetProfileQuery(actor.UserId), cancellationToken);
        return result.ToProtectedHttpResult(value =>
        {
            httpContext.Response.Headers["ETag"] = ETagFormatter.Format(value.Version);
            return Results.Ok(UserResponseMapper.ToResponse(value.User));
        });
    }

    internal static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        PasswordChangeUseCase useCase,
        ICurrentActorAccessor currentActorAccessor,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["Cache-Control"] = CacheControlNoStore;

        if (!IfMatchETag.TryRead(httpContext.Request, out var expectedVersion, out var error))
        {
            return error!;
        }

        var actor = currentActorAccessor.CurrentActor
            ?? throw new InvalidOperationException("An authenticated Session actor is required.");
        var sessionId = actor.SessionId
            ?? throw new InvalidOperationException("A Session actor must have a session id.");
        var tokenHash = currentActorAccessor.SessionTokenHash
            ?? throw new InvalidOperationException("A Session actor must have a token hash.");

        var command = new PasswordChangeCommand(
            actor.UserId,
            sessionId,
            tokenHash,
            expectedVersion,
            request.CurrentPassword,
            request.NewPassword);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToProtectedHttpResult(value =>
        {
            httpContext.Response.Headers["ETag"] = ETagFormatter.Format(value.Version);
            return Results.NoContent();
        });
    }
}
