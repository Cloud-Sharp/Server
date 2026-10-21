using CloudSharp.Core.Common.Errors;
using FluentResults;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ProtectedResultHttpMapper
{
    internal static IResult ToProtectedHttpResult<T>(
        this Result<T> result,
        Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsFailed && result.Errors.Any(error => error is SessionError))
        {
            return ErrorHttpResults.FromCode(
                ErrorCodes.Auth.TokenInvalid,
                ErrorResponseWriter.MessageForCode(ErrorCodes.Auth.TokenInvalid));
        }

        return result.ToHttpResult(onSuccess);
    }
}
