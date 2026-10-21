using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Auth.Mapping;
using CloudSharp.Api.Endpoints.Auth.Requests;
using CloudSharp.Api.Endpoints.Auth.Responses;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.Registrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Auth;

public static class AuthEndpoints
{
    private const string CacheControlNoStore = "no-store";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v2/auth")
            .WithTags("Auth")
            .WithMetadata(NoStoreResponseMetadata.Instance);

        group.MapPost("/registrations", RegisterAsync)
            .AllowAnonymous()
            .WithName("RegisterAccount")
            .WithSummary("계정을 등록하고 Session을 발급합니다.")
            .Produces<AuthSessionResponse>(StatusCodes.Status201Created)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status409Conflict, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json");

        group.MapPost("/sessions", LoginAsync)
            .AllowAnonymous()
            .WithName("CreateSession")
            .WithSummary("이메일과 비밀번호로 Session을 발급합니다.")
            .Produces<AuthSessionResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status401Unauthorized, "application/json")
            .Produces<ErrorResponse>(StatusCodes.Status503ServiceUnavailable, "application/json");

        return endpoints;
    }

    internal static async Task<IResult> RegisterAsync(
        RegistrationRequest request,
        RegistrationUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["Cache-Control"] = CacheControlNoStore;

        var command = new RegistrationCommand(
            request.Email,
            request.UserName,
            request.DisplayName,
            request.Password);

        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToHttpResult(value =>
            Results.Created("/api/v2/me", AuthResponseMapper.ToResponse(value)));
    }

    internal static async Task<IResult> LoginAsync(
        LoginRequest request,
        LoginUseCase useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["Cache-Control"] = CacheControlNoStore;

        var command = new LoginCommand(request.LoginId, request.Password);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToHttpResult(value =>
            Results.Ok(AuthResponseMapper.ToResponse(value)));
    }
}
