using FluentResults;

namespace CloudSharp.Core.Common.Errors;

public abstract class CloudSharpError : Error
{
    public const string ErrorCodeMetadataKey = "ErrorCode";

    protected CloudSharpError(ErrorDefinition errorDefinition, string message)
        : base(message)
    {
        Metadata[ErrorCodeMetadataKey] = errorDefinition.Code;
    }
}
