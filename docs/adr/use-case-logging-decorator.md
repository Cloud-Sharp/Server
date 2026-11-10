# 유즈케이스 로깅 데코레이터 설계

## 목적과 범위

유즈케이스가 실행될 때 이름, 소요 시간, 결과를 공통으로 기록한다. 각 유즈케이스는 업무 처리에 집중하고, 로깅은 호출을 감싸는 데코레이터가 담당한다. 첫 단계는 데코레이터 단독 검증까지이며 Auth 연결과 Space 스태시 복원은 후속 단계에서 진행한다.

## 실행 계약

- Core에 `IUseCase<in TInput, TOutput>`을 두고 `Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken = default)`를 선언한다.
- API의 `Observability`에 `LoggingUseCaseDecorator<TInput, TOutput>`을 둔다. 데코레이터는 원래 입력과 취소 토큰으로 내부 유즈케이스를 한 번 호출한다.
- 반환된 `Result`는 같은 인스턴스로 전달한다. 취소와 예외는 그대로 전파한다. 로깅 공급자가 실패해도 호출자가 받는 결과와 예외는 보존한다.

## 로그 정책

호출이 종료되면 Debug 수준의 구조화 로그를 한 건 기록한다. 공통 필드는 유즈케이스 이름, 경과 시간, 실행 결과, 가능한 경우 `Activity`의 trace ID다. correlation ID는 기존 로그 scope를 사용한다.

| 실행 결과 | 기록할 추가 정보 |
|---|---|
| 성공 | 성공 상태 |
| `Result.Fail` | 실패 상태와 `ErrorCode` 메타데이터의 문자열 코드 |
| 취소 | 취소 상태 |
| 예외 | 예외 종료 상태와 예외 타입명 |

로그는 정해진 필드만 사용한다. 입력·응답 본문, 토큰, 비밀번호, 오류 메시지는 기록 대상에서 제외한다. Debug가 비활성화되면 로그 필드 수집을 건너뛴다.

## 업무 이벤트와 Transactional Outbox

데코레이터의 완료 로그는 유즈케이스 호출 결과를 관측한다. 발행할 업무 이벤트는 실제 상태 변경을 아는 유즈케이스가 결정한다. 예를 들어 소유권 이전이 일어나면 Space 소유자 변경, 양쪽 Membership 변경, `SpaceOwnershipTransferred` 이벤트를 함께 저장한다. 현재 소유자를 다시 지정한 요청도 완료 로그에는 성공으로 기록된다.

1. 유즈케이스는 `ITransactionExecutor`가 연 PostgreSQL 트랜잭션 안에서 업무 변경과 `PENDING` Outbox 레코드를 등록하고 저장한다. 커밋하면 둘 다 반영되고, 롤백하면 둘 다 취소된다. Outbox 저장 실패는 업무 변경과 같은 트랜잭션의 실패로 처리한다.
2. Outbox relay는 커밋된 미발행 레코드를 lease로 선점하고 Redis Streams에 발행한다. 발행 후 Stream 메시지 ID와 `PUBLISHED` 상태를 저장한다. 발행 실패나 lease 만료로 다시 처리할 수 있는 레코드는 `PENDING`으로 돌리고, 발행 재시도 한도를 소진하면 `DEAD`로 기록한다.
3. Redis 발행 직후 `PUBLISHED` 기록 전에 중단되면 같은 이벤트가 다시 발행될 수 있다. relay는 원래 `EventId`를 유지하고, 소비자는 이 값으로 중복을 제거해 업무 처리를 멱등하게 수행한다.

이벤트 payload는 이벤트 타입과 버전에 해당하는 스키마를 따른다. 외부 소비자에게는 PublicId를 사용하며 내부 숫자 ID와 민감정보는 외부 payload에서 제외한다. 유즈케이스 성공 로그는 호출 결과를 나타내고, Redis 발행 완료는 Outbox의 `PUBLISHED` 상태로 추적한다.

발행 경로는 [프로젝트 개요의 비동기 작업 설계](../project-overview.md#74-비동기-작업과-실시간-알림)와 [이벤트 계약](../../contracts/events/README.md)을 따른다. 상태 전이와 lease 규칙은 로컬 설계의 `.llm/domains/outbox-event.md`에 정의되어 있다.

## 구현 후 검증 기준

단위 테스트로 내부 호출이 한 번만 일어나는지, 원래 취소 토큰이 전달되는지, 성공·실패 결과와 예외가 보존되는지 확인한다. 구조화 로그의 결과 분류, 민감정보 제외, Debug 비활성화, 로깅 공급자 실패도 검증한다.

Outbox 구현 단계에서는 업무 변경과 이벤트의 커밋·롤백 원자성, lease 만료 복구, 재시도 한도 소진 후 `DEAD` 전환을 통합 테스트로 검증한다. Redis 발행 후 상태 기록에 실패하는 상황을 재현해 같은 `EventId`가 중복 전달되어도 소비자의 업무 처리가 한 번만 반영되는지 확인한다.
