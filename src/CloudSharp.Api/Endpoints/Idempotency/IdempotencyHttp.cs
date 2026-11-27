using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudSharp.Api.Auth.CurrentUser;
using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Contracts.ProblemDetails;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Tokens;

namespace CloudSharp.Api.Endpoints.Idempotency;

internal static class IdempotencyHttp
{
    internal static RouteHandlerBuilder RequireIdempotency(
        this RouteHandlerBuilder builder,
        IdempotencyEndpointPolicy policy) =>
        builder.WithMetadata(policy).AddEndpointFilter(new IdempotencyEndpointFilter(policy));

    internal static T GetArgument<T>(EndpointFilterInvocationContext context) where T : class =>
        context.Arguments.OfType<T>().Single();

    internal static IdempotencyScope ForSession(EndpointFilterInvocationContext context, string canonicalPath)
    {
        var httpContext = context.HttpContext;
        var actor = httpContext.RequestServices.GetRequiredService<ICurrentActorAccessor>().CurrentActor
            ?? throw new InvalidOperationException("An authenticated actor is required for this idempotency policy.");

        return new IdempotencyScope(
            $"session:{actor.UserPublicId:D}",
            httpContext.Request.Method,
            canonicalPath);
    }

    internal static IdempotencyScope ForAnonymous(
        EndpointFilterInvocationContext context,
        string partition,
        string canonicalPath)
    {
        var httpContext = context.HttpContext;
        var digest = httpContext.RequestServices.GetRequiredService<ITokenHasher>()
            .Hash($"idempotency:actor:{partition}");
        return new IdempotencyScope($"anonymous:{digest}", httpContext.Request.Method, canonicalPath);
    }

    internal static string HashCapability(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    internal static string Fingerprint(HttpContext context, object? input)
    {
        var mediaType = MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            ? contentType.MediaType?.ToLowerInvariant()
            : null;
        var element = JsonSerializer.SerializeToElement(new { mediaType, input });
        return context.RequestServices.GetRequiredService<ITokenHasher>()
            .Hash($"idempotency:fingerprint:{CanonicalJson.Serialize(element)}");
    }

    internal static IResult Replay(HttpContext context, IdempotencyRecord.Completed record)
    {
        var policy = context.GetEndpoint()?.Metadata.GetMetadata<IdempotencyEndpointPolicy>();
        if (policy is null || policy.OperationId != record.Reservation.Request.OperationId ||
            policy.ResultVersion != record.ResultVersion)
        {
            throw new InvalidOperationException("The completed operation does not match this endpoint's idempotency policy.");
        }

        return policy.Replay(context, record);
    }

    internal static IResult SecretNotReplayable(string idPropertyName, Guid? publicId) =>
        new SecretReplayResult(idPropertyName, publicId);

    private sealed class SecretReplayResult(string idPropertyName, Guid? publicId) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var body = new Dictionary<string, object>
            {
                ["requestId"] = httpContext.TraceIdentifier,
                ["error"] = new ErrorBody(
                    "SECRET_RESPONSE_NOT_REPLAYABLE",
                    "The operation completed, but its original secret cannot be returned again.",
                    []),
            };

            if (publicId is { } id)
            {
                body[idPropertyName] = id;
            }

            return Results.Json(body, ResultHttpMapper.JsonOptions, statusCode: StatusCodes.Status409Conflict)
                .ExecuteAsync(httpContext);
        }
    }
}
