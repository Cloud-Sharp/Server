# API 요청 멱등성과 관리자 우회 감사

## 결정

HTTP 요청 멱등성, 최초 응답 재생, 커밋 후 복구와 관리자 우회에 대한 영속 감사는 여러 도메인에 공통으로 도입한다. 멱등성은 생성·비동기 접수 등 API 계약이 `Idempotency-Key`를 요구하는 작업에 적용한다. 관리자 우회는 해당 요청의 영속 감사 경로가 연결된 엔드포인트에서 활성화한다. [공통 API 계약](../../.llm/api/common-contract.md#9-idempotency-key) · [인가 감사 계약](../../.llm/auth/authorization.md#11-auditevent)

## HTTP 요청 멱등성

- 요청자, HTTP 메서드, 표준 경로를 범위로 키의 고유성을 보장한다. 요청 본문, 미디어 유형, 의미 있는 쿼리를 정규화해 지문을 계산한다. 같은 키에 다른 요청 지문이 들어오면 `409 IDEMPOTENCY_KEY_REUSED`, 같은 지문의 요청을 처리 중이면 `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`를 반환한다.
- 완료된 요청을 같은 키와 지문으로 24시간 이내에 다시 보내면 최초 상태 코드, 본문 및 해당 응답의 `Location`·`ETag`를 재생한다. 업무 변경을 커밋한 직후 응답 기록이나 전송이 실패해도 재시도에서 최초 결과를 복구해 변경을 한 번만 반영한다.
- 토큰처럼 비밀값을 한 번만 반환하는 API에는 `409 SECRET_RESPONSE_NOT_REPLAYABLE` 계약을 적용한다. 재생용 저장 데이터에서는 비밀값을 제외한다. [응답 재생과 비밀값 처리](../../.llm/api/common-contract.md#9-idempotency-key)

## 관리자 우회 감사

- 실제로 `SYSTEM_ADMIN_BYPASS`를 사용한 요청마다 행위자와 대상 리소스의 PublicId, 작업, 결과, correlation ID를 기록한다. 대상이 삭제된 뒤에도 식별할 수 있도록 스냅샷을 보관한다. 토큰과 해시, 비밀번호, `Authorization` 헤더, 파일 내용은 기록 대상에서 제외한다. [AuditEvent 설계](../../.llm/domains/audit-event.md)
- 우회로 민감한 변경을 수행할 때는 업무 변경과 감사 이벤트를 같은 트랜잭션에 저장한다. 감사 이벤트를 저장할 수 없으면 변경을 롤백한다. 우회로 조회한 경우에는 보호된 데이터를 반환하기 전에 감사 이벤트를 영속화한다. Outbox 소비자는 감사 기록을 `audit_events`에 추가하고 재처리 시 `source_event_id`와 `event_type`으로 중복 저장을 방지한다. [감사 실패 처리](../../.llm/auth/security-testing.md#4-장애-처리) · [감사 저장소 계약](../../.llm/database/integration-operations.md#audit_events)

## Outbox와 응답 복구

Transactional Outbox는 업무 변경과 이벤트를 함께 커밋하고 발행을 재시도할 수 있게 한다. 이벤트 소비자는 `EventId`로 중복 전달을 처리한다. HTTP 재시도에도 요청 키 고유성, 지문 판정, 최초 응답 재생 계약을 적용한다. [Transactional Outbox 설계](../adr/use-case-logging-decorator.md#업무-이벤트와-transactional-outbox)

HTTP 멱등성 기록은 PostgreSQL 전용 테이블에서 24시간 보존한다. 업무 변경과 완료 기록을 같은 트랜잭션에 저장한다. 완료 결과 조회의 성능 문제가 확인되면 Redis를 조회 캐시로 추가할 수 있다. [HTTP 요청 멱등성 공통 구조 기획](../http-request-idempotency-plan.md)

## 현재 적용 사례

`POST /api/v2/spaces`는 현재 선택 헤더인 `Idempotency-Key`를 무시하고 유효한 요청마다 새 Space를 생성한다. Space Session API에서 `SYSTEM_ADMIN`도 OWNER 또는 활성 Membership 권한으로 접근한다. 두 기능의 도입 상태는 해당 API의 현재 구현 범위에서 확인한다. [Space API](../api/space-api.md)
