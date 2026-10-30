# Authorization

SystemRole x SpacePermission rules. See .llm/auth/authorization.md

## SpacePermissionPolicy

`SpacePermissionPolicy`는 호출자가 조회한 Space·사용자 ID·Membership·현재 Role snapshot만 받아
유효 Permission 집합을 계산하는 순수 정적 정책이다.

- OWNER는 Membership 없이도 현재 정의된 모든 `SpacePermission`을 갖고, 그 외 사용자는 해당 사용자·해당
  Space의 ACTIVE Membership에 할당된 비삭제 Role Permission의 합집합만 갖는다.
- `HasAll`는 요구 Permission의 전체 포함만 검사하며 미정의 enum 값은 없는 권한으로 취급한다.
- DB 조회, Space 존재·상태 검사, Session SystemRole 관리자 우회, MCP scope·allowlist 교집합,
  최신 권한 재조회, HTTP 응답 결정은 후속 `ISpaceAuthorizationService`(인가 설계 §5~6, §8)의 책임이다.
- quota 변경·Space 삭제·소유권 이전 같은 작업의 OWNER 검증은 이 정책이 아니라 해당 UseCase의 기존 규칙을
  그대로 사용한다.
- 결과는 매 호출 복사한 FrozenSet이며 사용자별 캐시를 두지 않는다(인가 설계 §12 — 최신 권한 원칙).
