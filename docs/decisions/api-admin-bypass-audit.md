# 관리자 우회 감사

## 결정

관리자 우회는 해당 요청의 영속 감사 경로가 연결된 엔드포인트에서 활성화한다. [인가 감사 계약](../../.llm/auth/authorization.md#11-auditevent)

## 관리자 우회 감사

- 실제로 `SYSTEM_ADMIN_BYPASS`를 사용한 요청마다 행위자와 대상 리소스의 PublicId, 작업, 결과, correlation ID를 기록한다. 대상이 삭제된 뒤에도 식별할 수 있도록 스냅샷을 보관한다. 토큰과 해시, 비밀번호, `Authorization` 헤더, 파일 내용은 기록 대상에서 제외한다. [AuditEvent 설계](../../.llm/domains/audit-event.md)
- 우회로 민감한 변경을 수행할 때는 업무 변경과 감사 이벤트를 같은 트랜잭션에 저장한다. 감사 이벤트를 저장할 수 없으면 변경을 롤백한다. 우회로 조회한 경우에는 보호된 데이터를 반환하기 전에 감사 이벤트를 영속화한다. Outbox 소비자는 감사 기록을 `audit_events`에 추가하고 재처리 시 `source_event_id`와 `event_type`으로 중복 저장을 방지한다. [감사 실패 처리](../../.llm/auth/security-testing.md#4-장애-처리) · [감사 저장소 계약](../../.llm/database/integration-operations.md#audit_events)

## Transactional Outbox

Transactional Outbox는 업무 변경과 이벤트를 함께 커밋하고 발행을 재시도할 수 있게 한다. 이벤트 소비자는 `EventId`로 중복 전달을 처리한다. [Transactional Outbox 설계](../adr/use-case-logging-decorator.md#업무-이벤트와-transactional-outbox)

## 현재 적용 사례

Space Session API에서 `SYSTEM_ADMIN`도 OWNER 또는 활성 Membership 권한으로 접근한다. [Space API](../api/space-api.md)
