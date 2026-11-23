using CloudSharp.Api.Endpoints.Spaces.Mapping;
using CloudSharp.Core.Common.Errors;
using FluentResults;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Endpoints.Memberships.Mapping;

internal static class MembershipResultHttpMapper
{
    internal static IResult ToMembershipHttpResult<T>(
        this Result<T> result,
        Func<T, IResult> onSuccess,
        bool maskInvalidRole = false)
    {
        if (result.IsFailed && result.Errors.OfType<SpaceMembershipError>().Any(error =>
                !error.Metadata.ContainsKey("PropertyName")
                && error.Metadata.TryGetValue(CloudSharpError.ErrorCodeMetadataKey, out var code)
                && (Equals(code, ErrorCodes.SpaceMembership.NotFound)
                    || (maskInvalidRole && Equals(code, ErrorCodes.SpaceMembership.RoleInvalid)))))
        {
            return SpaceResultHttpMapper.NotFound();
        }

        return result.ToSpaceHttpResult(onSuccess);
    }
}
