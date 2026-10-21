using CloudSharp.Core.Common.Errors;
using FluentResults;
using Microsoft.AspNetCore.Http;

namespace CloudSharp.Api.Contracts.Mapping;

internal static class ErrorHttpResults
{
    internal const string InvalidDetailCode = "INVALID";

    internal static IResult FromCode(string code, string message) =>
        new FailureResult(new[]
        {
            new Error(message).WithMetadata(CloudSharpError.ErrorCodeMetadataKey, code),
        });

    internal static IResult Validation(string field, string detailCode, string message) =>
        new FailureResult(new[]
        {
            new Error(message)
                .WithMetadata(CloudSharpError.ErrorCodeMetadataKey, detailCode)
                .WithMetadata("PropertyName", field),
        });
}
