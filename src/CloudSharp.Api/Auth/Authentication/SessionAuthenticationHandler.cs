using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Core.Common.Actors;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Tokens;
using CloudSharp.Core.Domain.Users;
using CloudSharp.Core.UseCases.Auth.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace CloudSharp.Api.Auth.Authentication;

internal sealed class SessionAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AuthenticateSessionUseCase _authenticateSessionUseCase;
    private readonly ITokenHasher _tokenHasher;
    private readonly ICurrentActorAccessor _currentActorAccessor;
    private string? _failureCode;

    public SessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthenticateSessionUseCase authenticateSessionUseCase,
        ITokenHasher tokenHasher,
        ICurrentActorAccessor currentActorAccessor)
        : base(options, logger, encoder)
    {
        _authenticateSessionUseCase = authenticateSessionUseCase;
        _tokenHasher = tokenHasher;
        _currentActorAccessor = currentActorAccessor;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        _failureCode = null;

        var authorization = Request.Headers.Authorization;
        if (authorization.Count == 0)
        {
            _failureCode = ErrorCodes.Auth.TokenRequired;
            return AuthenticateResult.NoResult();
        }

        if (authorization.Count != 1
            || !TryReadBearerToken(authorization[0], out var token))
        {
            return Failure(ErrorCodes.Auth.TokenInvalid);
        }

        if (token.Length > SessionAuthenticationDefaults.MaxBearerTokenLength)
        {
            return Failure(ErrorCodes.Auth.TokenInvalid);
        }

        if (token.StartsWith(TokenPrefixes.Get(TokenKind.Session), StringComparison.Ordinal))
        {
            return await AuthenticateSessionAsync(token);
        }

        if (token.StartsWith(TokenPrefixes.Get(TokenKind.McpCredential), StringComparison.Ordinal))
        {
            return Failure(ErrorCodes.Auth.TokenTypeNotAllowed);
        }

        return Failure(ErrorCodes.Auth.TokenInvalid);
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = "Bearer";

        var code = _failureCode ?? ErrorCodes.Auth.TokenRequired;
        var result = ErrorHttpResults.FromCode(code, ErrorResponseWriter.MessageForCode(code));
        await result.ExecuteAsync(Context);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        await ErrorResponseWriter.WriteAsync(
            Context,
            StatusCodes.Status403Forbidden,
            "PERMISSION_DENIED",
            "The authenticated actor is not allowed to perform this operation.",
            []);
    }

    private async Task<AuthenticateResult> AuthenticateSessionAsync(string token)
    {
        var tokenHash = _tokenHasher.Hash(token);
        var result = await _authenticateSessionUseCase.ExecuteAsync(
            new AuthenticateSessionQuery(tokenHash),
            Context.RequestAborted);
        if (result.IsFailed)
        {
            var dependencyFailure = result.Errors.Any(error =>
                error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
                && string.Equals(
                    code as string,
                    ErrorCodes.Common.DependencyUnavailable,
                    StringComparison.Ordinal));

            return Failure(dependencyFailure
                ? ErrorCodes.Common.DependencyUnavailable
                : ErrorCodes.Auth.TokenInvalid);
        }

        var authenticated = result.Value;
        var actor = CurrentActor.ForSession(
            authenticated.UserId,
            authenticated.User.UserPublicId,
            authenticated.User.SystemRole,
            authenticated.SessionId);

        _currentActorAccessor.Set(actor, tokenHash);

        var claims = new[]
        {
            new Claim(SessionClaimTypes.UserId, actor.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(SessionClaimTypes.UserPublicId, actor.UserPublicId.ToString("D")),
            new Claim(SessionClaimTypes.SessionId, actor.SessionId!.Value.ToString("D")),
            new Claim(SessionClaimTypes.ActorType, SessionAuthenticationDefaults.SessionActorType),
            new Claim(SessionClaimTypes.SystemRole, FormatSystemRole(actor.SystemRole)),
        };

        var identity = new ClaimsIdentity(claims, SessionAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SessionAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    private AuthenticateResult Failure(string code)
    {
        _failureCode = code;
        return AuthenticateResult.Fail(code);
    }

    private static string FormatSystemRole(SystemRole role) => role switch
    {
        SystemRole.User => "USER",
        SystemRole.SystemAdmin => "SYSTEM_ADMIN",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported system role."),
    };

    private static bool TryReadBearerToken(string? rawHeader, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(rawHeader)
            || !AuthenticationHeaderValue.TryParse(rawHeader, out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter)
            || header.Parameter.Contains(','))
        {
            return false;
        }

        token = header.Parameter;
        return true;
    }
}
