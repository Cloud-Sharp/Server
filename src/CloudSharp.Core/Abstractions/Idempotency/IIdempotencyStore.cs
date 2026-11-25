using CloudSharp.Core.Common.Idempotency;
using FluentResults;

namespace CloudSharp.Core.Abstractions.Idempotency;

/// <summary>요청 선점 및 완료 결과의 영속 저장 포트다. 가용성 장애는 실패 결과로 반환한다.</summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// 요청 범위와 키에 해당하는 기록을 조회하거나, 짧은 트랜잭션에서 후보 시도를 원자적으로 선점한다.
    /// 기록이 없거나 완료 기록이 만료되면 선점할 수 있다. 진행 중 선점은 지문과 작업이 일치하고
    /// 선점이 만료되었으며 행 잠금을 즉시 확보한 경우에만 교체한다. 교체 전에 최신 상태를 다시 확인한다.
    /// 선점에 성공하면 후보 시도의 기록을, 이미 기록이 있으면 현재 기록을 반환한다.
    /// 만료된 완료 기록은 재생 결과로 반환하지 않는다.
    /// </summary>
    Task<Result<IdempotencyRecord>> ReserveOrGetAsync(
        IdempotencyReservation candidate,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>업무 트랜잭션이 끝날 때까지 해당 기록에 대한 행 잠금을 보유한다.</summary>
    Task<Result<IdempotencyRecord?>> LockAsync(
        IdempotencyScope scope,
        string key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 업무 트랜잭션에서 같은 시도의 진행 중 기록을 완료로 변경한다.
    /// 성공을 반환하기 전에 변경을 같은 연결의 트랜잭션에 반영한다.
    /// 현재 시도가 진행 중 상태와 일치하지 않으면 실패를 반환한다.
    /// </summary>
    Task<Result> CompleteAsync(
        IdempotencyRecord.Completed completion,
        CancellationToken cancellationToken = default);

    /// <summary>업무 트랜잭션 밖에서 해당 시도의 커밋된 완료 결과를 조회한다.</summary>
    Task<Result<IdempotencyRecord.Completed?>> FindCompletionAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 업무 트랜잭션 밖에서 같은 시도의 진행 중 선점만 해제한다.
    /// 해당 시도의 기록이 없거나 상태가 바뀌었으면 성공 값 <c>false</c>를 반환한다.
    /// </summary>
    Task<Result<bool>> ReleaseAsync(
        IdempotencyReservation reservation,
        CancellationToken cancellationToken = default);
}
