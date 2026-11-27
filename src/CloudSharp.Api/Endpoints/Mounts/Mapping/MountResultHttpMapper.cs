using CloudSharp.Api.Contracts.Mapping;
using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Common.Errors;
using FluentResults;

namespace CloudSharp.Api.Endpoints.Mounts.Mapping;

internal static class MountResultHttpMapper
{
    internal static IResult ToMountHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsFailed && result.Errors.OfType<MountError>().Any(error =>
                Equals(error.Metadata[CloudSharpError.ErrorCodeMetadataKey], ErrorCodes.Mount.InvalidState)))
        {
            return ErrorHttpResults.FromCode(ErrorCodes.Common.RequestConflict,
                "The request conflicts with the current resource state.");
        }

        return result.ToSpaceHttpResult(onSuccess);
    }
}
