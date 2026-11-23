# Space 생성 멱등성 도입 시점

## 결정

SP-02의 생성 멱등성, 최초 응답 재생, 커밋 후 장애 복구는 후속 작업으로 둔다. 이번 단계에서는 기존 Space 생성 유즈케이스를 HTTP로 제공한다. `POST /api/v2/spaces`는 선택 헤더인 `Idempotency-Key`의 값을 무시한다. 같은 요청을 반복해도 각 요청은 새 Space와 기본 Role을 생성하고 `201`, `Location`, `ETag`를 반환한다. [현재 SP-02 계약](../api/space-api.md#sp-02-space-생성)

## 후속 계약

- 인증된 요청자, HTTP 메서드, 표준 경로를 범위로 키의 고유성을 보장한다. 같은 키에 다른 요청 지문이 들어오면 `409 IDEMPOTENCY_KEY_REUSED`, 같은 지문의 요청을 처리 중이면 `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`를 반환한다.
- 같은 키와 지문으로 완료된 요청을 24시간 이내 다시 보내면 최초 `201` 본문·`Location`·`ETag`를 재생한다. PostgreSQL에 Space와 기본 Role을 커밋한 직후 응답 기록이나 전송에 실패해도, 재시도에서 최초 결과를 복구하고 생성을 한 번만 반영할 수 있어야 한다. [공통 멱등성 계약](../../.llm/api/common-contract.md#9-idempotency-key)

## Outbox를 도입할 때

Transactional Outbox는 업무 변경과 이벤트를 같은 트랜잭션에서 커밋하고, 발행을 재시도할 수 있게 한다. 이벤트 소비자는 `EventId`로 중복 전달을 처리한다. HTTP 생성 재시도에는 위의 요청 키 고유성, 지문 판정, 응답 재생 계약을 적용한다. [Transactional Outbox 설계](../adr/use-case-logging-decorator.md#업무-이벤트와-transactional-outbox)

Outbox 행이 최초 응답 복구도 지원하도록 할지, 별도 생성 결과를 저장할지는 후속 설계에서 결정한다. Redis 사용 여부도 그때 정한다.
