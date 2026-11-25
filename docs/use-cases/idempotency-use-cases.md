# Core 요청 멱등성 유즈케이스 설계

Core의 `BeginIdempotentRequestUseCase`와 `ReleaseIdempotencyReservationUseCase`는 API 필터에 선점 계약을 제공한다. 시작, 해제, 업무 유즈케이스는 기존 `IUseCase<TInput, TOutput>` 계약을 사용한다. [공통 구조 기획](../http-request-idempotency-plan.md) · [API 필터 설계](../api/http-idempotency-filter-design.md) · [Core 유즈케이스 계약](../../src/CloudSharp.Core/UseCases/IUseCase.cs)

| Core 진입점 | 호출자 | 맡는 일 |
|---|---|---|
| `BeginIdempotentRequestUseCase` | 인증·인가를 통과한 API 필터 | 실행 시도를 선점하거나 기존 완료 결과를 반환한다. |
| `ReleaseIdempotencyReservationUseCase` | 트랜잭션 미시작 또는 최종 롤백을 확인한 API 필터 | 현재 시도에 속한 진행 중 선점만 해제한다. |

## 요청 시작

**입력**은 정규화된 요청 범위, 검증된 키, 요청 지문, 안정적인 작업 식별자다. 요청 범위는 API가 요청자·메서드·표준 경로에서 계산한다. 작업 식별자는 복구 결과를 해석하는 데 사용한다.

| 저장된 상태 | Core 결과 |
|---|---|
| 기록이 없거나 완료 기록의 보존 기간이 지났다. | 짧은 선점 트랜잭션에서 원자적으로 선점하고 실행 시도 ID를 반환한다. |
| 유효한 기록과 요청 지문이 다르다. | `IDEMPOTENCY_KEY_REUSED`를 반환한다. |
| 지문이 같고 다른 시도가 처리 중이다. | `IDEMPOTENCY_REQUEST_IN_PROGRESS`를 반환한다. |
| 지문이 같고 완료 결과가 유효하다. | 작업 식별자, 결과 형식 버전, 복구에 필요한 데이터가 담긴 완료 기록을 반환한다. |
| 판정에 필요한 저장소에 접근할 수 없다. | `DEPENDENCY_UNAVAILABLE`을 반환한다. |

같은 지문의 진행 중 선점이 만료되면 잠금을 즉시 확보한 경우에만 최신 상태를 다시 확인하고 실행 시도 ID를 교체한다. 기존 업무 트랜잭션이 행을 잠그고 있으면 처리 중 결과를 반환한다. 완료 기록 조회는 이 유즈케이스 안에서 선점 여부와 함께 판단한다. [PostgreSQL 선점과 복구](../http-request-idempotency-plan.md#postgresql-저장과-복구)

## 업무 실행과 완료 기록

업무 유즈케이스는 기존 `ExecuteAsync`에서 `ITransactionExecutor.ExecuteAsync`를 호출한다. API 필터는 실행 시도와 복구 정책을 요청 범위 컨텍스트에 둔다. `EfCoreTransactionExecutor`는 등록된 콜백 결과 타입의 업무 트랜잭션에 이 정책을 적용한다. 일회성 비밀을 발급하는 작업의 복구 정책은 완료 사실과 공개 가능한 식별자를 저장한다.

1. 실행기가 업무 트랜잭션을 열고 선점 행을 잠근 뒤 현재 시도 ID와 지문을 확인한다. 실행 권한을 잃은 시도에는 충돌을 반환한다.
2. 같은 시도의 커밋된 완료 기록이 이미 있으면 API 필터로 전달한다. 이 확인은 DB 실행 전략이 재시도할 때도 업무 콜백보다 먼저 수행한다.
3. 선점이 유효하면 기존 업무 콜백을 실행하고 성공 결과에서 복구 데이터를 뽑는다. 행 잠금을 커밋 또는 롤백까지 유지하고, 업무 변경과 완료 기록을 같은 트랜잭션에서 커밋한다. 완료 기록에 실패하면 업무 변경도 롤백한다.

API 필터는 복구된 기록과 일반 재요청에 같은 응답 변환기를 사용한다. [Core 트랜잭션 계약](../../src/CloudSharp.Core/Abstractions/Transactions/README.md#실패와-취소) · [필터의 실행 시도 처리](../api/http-idempotency-filter-design.md#실행-시도-전달과-종료)

## 선점 해제

`ReleaseIdempotencyReservationUseCase`는 요청 범위·키·실행 시도 ID를 받는다. 저장소에서 같은 시도의 `IN_PROGRESS` 행임을 확인한 뒤 조건부로 해제한다. 완료된 행, 이미 해제된 행, 다른 시도가 인수한 행은 해제할 대상이 없다는 결과로 처리한다.

API 필터는 핸들러가 종료된 뒤 업무 트랜잭션이 시작되지 않았거나 최종 롤백이 확인된 경우 이 유즈케이스를 호출한다. 유즈케이스가 롤백 후 다시 실행하는 동안에는 같은 선점을 유지한다. 커밋 여부가 불명확하면 완료 기록부터 확인한다. 해제를 시도할 수 없었던 선점은 만료 규칙으로 회수한다.

## Core 포트와 검증 조건

Core 저장소 포트는 시작 판정과 선점, 업무 트랜잭션의 선점 확인과 완료 기록, 조건부 해제를 제공한다. PostgreSQL 행 잠금, 고유 제약, 복구 데이터 직렬화는 Infrastructure가 구현한다. [Core 레이어 규칙](../../src/CloudSharp.Core/README.md#rules-enforced-by-architecturetests)

- 같은 범위와 키의 동시 요청에 실행 시도 ID를 하나만 부여한다. 충돌과 완료 기록은 업무 콜백에 진입하기 전에 판정한다.
- 완료 기록 저장 실패가 업무 변경을 함께 롤백하고, 커밋 직후 응답 유실은 최초 결과로 복구되는지 검증한다.
- 이전 시도가 새 시도의 선점을 해제하거나 업무를 커밋할 수 없는지 검증한다.
- 새 업무 유즈케이스는 기존 트랜잭션 실행 계약을 사용하고 API 라우트의 결과 타입과 복구 정책을 등록한다.
