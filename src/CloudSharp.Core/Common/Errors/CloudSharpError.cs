using FluentResults;

namespace CloudSharp.Core.Common.Errors;

public abstract class CloudSharpError : Error
{
    public const string ErrorCodeMetadataKey = "ErrorCode";
    public const string StatusCodeMetadataKey = "StatusCode";

    protected CloudSharpError(ErrorDefinition errorDefinition, string message)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = errorDefinition.Code;
        Metadata[StatusCodeMetadataKey] = errorDefinition.StatusCode;
    }
}
