using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Api.Endpoints.Auth.Mapping;
using CloudSharp.Api.Endpoints.Auth.Requests;
using CloudSharp.Api.Endpoints.Auth.Responses;
using CloudSharp.Api.Endpoints.Idempotency;
using CloudSharp.Api.Middleware;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Auth.Dtos;
using CloudSharp.Core.UseCases.Auth.Logins;
using CloudSharp.Core.UseCases.Auth.Registrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace CloudSharp.Api.Endpoints.Auth;

public static class AuthEndpoints
{
    private const string CacheControlNoStore = "no-store";
    private const string RegistrationPath = "/api/v2/auth/registrations";
    internal const string RegistrationOperationId = "register-account";
    internal const int RegistrationResultVersion = 1;

    private static readonly IdempotencyEndpointPolicy RegistrationIdempotency = IdempotencyEndpointPolicy.Create<User>(
        RegistrationOperationId,
        RegistrationResultVersion,
        context => IdempotencyHttp.ForAnonymous(
            context,
            IdempotencyHttp.GetArgument<RegistrationRequest>(context).Email.Trim().ToUpperInvariant(),
            RegistrationPath),
        context =>
        {
            var request = IdempotencyHttp.GetArgument<RegistrationRequest>(context);
            var displayName = request.DisplayName?.Trim();
            return new
            {
                Email = request.Email.Trim(),
                UserName = request.UserName?.Trim(),
                DisplayName = string.IsNullOrEmpty(displayName) ? null : displayName,
                request.Password,
            };
        },
        user => new IdempotencyRecovery.SecretNotReplayable(user.PublicId),
        (_, completion) => completion.Recovery is IdempotencyRecovery.SecretNotReplayable secret
            ? IdempotencyHttp.SecretNotReplayable("userPublicId", secret.ResourcePublicId)
            : throw new InvalidOperationException("Registration requires a secret response recovery record."));

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup("/api/v2/auth")
            .WithTags("Auth")
            .WithMetadata(NoStoreResponseMetadata.Instance);

        group.MapPost("/registrations", RegisterAsync)
            .AllowAnonymous()
            .RequireIdempotency(RegistrationIdempotency)
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
        IUseCase<RegistrationCommand, RegistrationResultDto> useCase,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        httpContext.Response.Headers["Cache-Control"] = CacheControlNoStore;

        var command = new RegistrationCommand(request.Email, request.UserName, request.DisplayName, request.Password);
        var result = await useCase.ExecuteAsync(command, cancellationToken);
        return result.ToHttpResult(value => Results.Created("/api/v2/me", AuthResponseMapper.ToResponse(value)));
    }

    internal static async Task<IResult> LoginAsync(
        LoginRequest request,
        IUseCase<LoginCommand, LoginResultDto> useCase,
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
