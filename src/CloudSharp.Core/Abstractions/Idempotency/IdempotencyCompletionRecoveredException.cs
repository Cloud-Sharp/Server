using CloudSharp.Core.Common.Idempotency;

namespace CloudSharp.Core.Abstractions.Idempotency;

/// <summary>이미 커밋된 요청을 API가 저장된 완료 데이터에서 복원하도록 전달한다.</summary>
public sealed class IdempotencyCompletionRecoveredException : Exception
{
    public IdempotencyCompletionRecoveredException(IdempotencyRecord.Completed completion)
        : base("The idempotent operation has already committed.")
    {
        Completion = completion;
    }

    public IdempotencyRecord.Completed Completion { get; }
}
