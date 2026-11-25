using CloudSharp.Core.Common.Idempotency;
using CloudSharp.Core.Common.Time;

namespace CloudSharp.Core.Abstractions.Idempotency;

/// <summary>요청에서 실행할 업무 트랜잭션에 선점과 복구 정책을 연결한다.</summary>
public interface IIdempotencyTransactionContext
{
    bool CanReleaseReservation { get; }

    IDisposable Activate(
        IdempotencyReservation reservation,
        int resultVersion,
        Type transactionResultType,
        Func<object?, IdempotencyRecovery> createRecovery,
        IIdempotencyStore store,
        IClock clock);
}
