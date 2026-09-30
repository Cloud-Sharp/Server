using FluentResults;
using FluentValidation;
using FluentValidation.Results;

namespace CloudSharp.Core.Common.Results;

/// <summary>
/// FluentValidation의 <see cref="ValidationResult"/>를 FluentResults의 <see cref="Result"/>로 변환하는 매퍼.
/// Core 계층 UseCase 내부에서 validation 실패를 Result로 통일할 때 사용한다.
/// </summary>
public static class FluentValidationResultMapper
{
    /// <summary>
    /// <see cref="ValidationResult"/>를 비제네릭 <see cref="Result"/>로 변환한다.
    /// 각 ValidationFailure는 ErrorCode, PropertyName 메타데이터를 포함하는 Error로 변환된다.
    /// AttemptedValue는 비밀번호 해시, token hash 등 민감값이 로그나 응답으로 전파되는 것을 막기 위해 포함하지 않는다.
    /// </summary>
    public static Result ToFailureResult(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            throw new InvalidOperationException(
                "A valid ValidationResult cannot be converted to a failure Result.");
        }

        var errors = validationResult.Errors.Select(ToError);
        return Result.Fail(errors);
    }

    /// <summary>
    /// <see cref="ValidationResult"/>를 <see cref="Result{T}"/>로 변환한다.
    /// 각 ValidationFailure는 ErrorCode, PropertyName 메타데이터를 포함하는 Error로 변환된다.
    /// AttemptedValue는 비밀번호 해시, token hash 등 민감값이 로그나 응답으로 전파되는 것을 막기 위해 포함하지 않는다.
    /// </summary>
    public static Result<T> ToFailureResult<T>(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            throw new InvalidOperationException(
                "A valid ValidationResult cannot be converted to a failure Result.");
        }

        var errors = validationResult.Errors.Select(ToError);
        return Result.Fail<T>(errors);
    }

    private static Error ToError(ValidationFailure failure) =>
        new Error(failure.ErrorMessage)
            .WithMetadata("ErrorCode", failure.ErrorCode)
            .WithMetadata("PropertyName", failure.PropertyName);
}