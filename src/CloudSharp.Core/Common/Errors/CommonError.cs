namespace CloudSharp.Core.Common.Errors;

/// <summary>
/// 영역 공통 오류. <see cref="CloudSharpError"/>를 상속해
/// <see cref="ErrorCodes.Common"/> 코드를 <see cref="CloudSharpError.ErrorCodeMetadataKey"/> 메타데이터로 노출한다.
/// </summary>
public sealed class CommonError : CloudSharpError
{
    private CommonError(ErrorDefinition definition, string message) : base(definition, message) { }

    public static CommonError DependencyUnavailable() =>
        new(new ErrorDefinition(ErrorCodes.Common.DependencyUnavailable), "A required dependency is unavailable.");

    public static CommonError PreconditionFailed() =>
        new(new ErrorDefinition(ErrorCodes.Common.PreconditionFailed), "Resource version does not match the request.");
}