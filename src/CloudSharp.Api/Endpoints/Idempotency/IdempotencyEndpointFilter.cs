using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Core.Abstractions.Idempotency;
using CloudSharp.Core.Common.Errors;
using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Time;
using CloudSharp.Core.UseCases;
using CloudSharp.Core.UseCases.Idempotency.Beginnings;
using CloudSharp.Core.UseCases.Idempotency.Dtos;
using CloudSharp.Core.UseCases.Idempotency.Releases;

namespace CloudSharp.Api.Endpoints.Idempotency;

internal sealed class IdempotencyEndpointFilter(IdempotencyEndpointPolicy policy) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        if (httpContext.Response.StatusCode >= StatusCodes.Status400BadRequest)
        {
            return await next(context);
        }

        var keys = httpContext.Request.Headers[IdempotencyEndpointPolicy.HeaderName];
        if (keys.Count != 1 || keys[0] is not
            { Length: >= IdempotencyEndpointPolicy.MinimumKeyLength and <= IdempotencyEndpointPolicy.MaximumKeyLength } key
            || !key.All(static character => character is >= ' ' and <= '~'))
        {
            return ErrorHttpResults.Validation(
                "idempotencyKey",
                ErrorCodes.Idempotency.KeyInvalid,
                "Idempotency-Key must contain a single value of 16 to 128 ASCII characters.");
        }

        var request = new IdempotencyRequestIdentity(
            policy.GetScope(context),
            key,
            IdempotencyHttp.Fingerprint(httpContext, policy.GetFingerprintInput(context)),
            policy.OperationId);
        var begin = httpContext.RequestServices
            .GetRequiredService<IUseCase<BeginIdempotentRequestCommand, BeginIdempotentRequestResultDto>>();
        var decision = await begin.ExecuteAsync(new BeginIdempotentRequestCommand(request), httpContext.RequestAborted);
        if (decision.IsFailed)
        {
            return new FailureResult(decision.Errors);
        }

        if (decision.Value is BeginIdempotentRequestResultDto.Completed completed)
        {
            return IdempotencyHttp.Replay(httpContext, completed.Record);
        }

        if (decision.Value is not BeginIdempotentRequestResultDto.Started started)
        {
            throw new InvalidOperationException("The request start returned an unknown idempotency decision.");
        }

        var services = httpContext.RequestServices;
        var transactionContext = services.GetRequiredService<IIdempotencyTransactionContext>();
        var activation = transactionContext.Activate(
            started.Reservation,
            policy.ResultVersion,
            policy.TransactionResultType,
            policy.CreateRecovery,
            services.GetRequiredService<IIdempotencyStore>(),
            services.GetRequiredService<IClock>());
        try
        {
            return await next(context);
        }
        catch (IdempotencyCompletionRecoveredException exception) when (exception.Completion.Reservation == started.Reservation)
        {
            return IdempotencyHttp.Replay(httpContext, exception.Completion);
        }
        finally
        {
            var canRelease = transactionContext.CanReleaseReservation;
            activation.Dispose();
            if (canRelease)
            {
                var release = services
                    .GetRequiredService<IUseCase<ReleaseIdempotencyReservationCommand, bool>>();
                await release.ExecuteAsync(new ReleaseIdempotencyReservationCommand(started.Reservation));
            }
        }
    }
}
