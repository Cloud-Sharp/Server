# Authorization

SystemRole x SpacePermission rules. See .llm/auth/authorization.md

## SpacePermissionPolicy

`SpacePermissionPolicy`는 호출자가 조회한 Space·사용자 ID·Membership·현재 Role snapshot만 받아
유효 Permission 집합을 계산하는 순수 정적 정책이다.

- OWNER는 Membership 없이도 현재 정의된 모든 `SpacePermission`을 갖고, 그 외 사용자는 해당 사용자·해당
  Space의 ACTIVE Membership에 할당된 비삭제 Role Permission의 합집합만 갖는다.
- `HasAll`는 요구 Permission의 전체 포함만 검사하며 미정의 enum 값은 없는 권한으로 취급한다.
- Space 존재·상태 검사와 Session 관리자 우회는 `SessionSpaceAuthorizationPolicy`, 최신 snapshot 조회는
  `ISpaceAuthorizationService`, HTTP 응답·마스킹은 API 경계의 책임이다. MCP 교집합 연결은 후속 작업이다.
- API 필터가 quota 변경·Space 삭제·소유권 이전의 OWNER 조건을 확인한다. 쓰기 트랜잭션은 최신 Space의 소유자를 다시 확인한다.
- 결과는 매 호출 복사한 FrozenSet이며 사용자별 캐시를 두지 않는다(인가 설계 §12 — 최신 권한 원칙).

## Session 인가 서비스

- Core `ISpaceAuthorizationService.AuthorizeAsync`는 `SpaceAuthorizationQuery`를 받아
  `Result<AuthorizedSpaceContext>`를 반환한다. Query는 검증한 요구 Permission·허용 Space 상태를
  불변 집합으로 복사한다. 문서의 개념적 Request는 실제 코드에서 Query로 명명한다.
- Infrastructure `SpaceAuthorizationService`는 Session actor를 검증한 뒤 매 호출
  `ISpaceAuthorizationReader`에서 새 `SpaceAuthorizationSnapshot`을 읽고 Core 정책에 전달한다.
  reader 실패와 취소를 전파하며 MCP는 조회 전에 거부한다.
- `SessionSpaceAuthorizationPolicy`는 미존재 → 기본 접근 자격 → 허용 Space 상태 → 요구 Permission
  순서로 판단한다. 기본 접근 자격은 OWNER 또는 ACTIVE Membership이며 빈 요구 집합에도 적용된다.
  SYSTEM_ADMIN 우회는 기본적으로 꺼져 있고, DELETING·DELETED에는 우회가 허용되지 않는다.
- 성공 context는 불변 Permission과 `SystemAdminBypass`·`Owner`·`Membership` Source를 보관한다.
  관리자·OWNER 경로의 MembershipId는 null이다. 요청 밖에 캐시하지 않는다.
- `SpaceAuthorizationError`는 미존재·접근 거부·상태 거부·Permission 부족을 내부 code로 구분하고
  상태 오류의 `ActualSpaceStatus`를 보존한다. 외부 403/404 표현이나 감사 저장을 수행하지 않는다.
- API 필터는 상세 경로의 작업 권한을 판정하고, Space 목록의 접근 판정은 API 목록 정책이 담당한다.
  Core 유즈케이스는 인가 결과를 사용해 업무 상태와 대상 리소스의 관계를 검증한다. MCP 연결은 후속 범위다.
  본인 탈퇴의 SUSPENDED·LEFT 예외와 Upload·작업 요청자 조건은 전용 UseCase가 검증한다.
