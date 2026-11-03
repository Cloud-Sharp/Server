# Space Authorization

`SpaceAuthorizationService`는 Core의 `ISpaceAuthorizationService`를 구현한다. 인증된 Session만
지원하고 `ISpaceAuthorizationReader`의 최신 조회 결과를 `SessionSpaceAuthorizationPolicy`로 전달한다.
조회 결과를 캐시하거나 User·Membership·Role을 변경하지 않는다.

- MCP는 reader 호출 전에 `AUTH_TOKEN_TYPE_NOT_ALLOWED`로 거부한다.
- reader의 Result 실패와 예외·취소를 전파한다. 취소된 요청은 인가 성공으로 반환하지 않는다.
- DB adapter·DI 등록·API filter·HTTP 오류 변환·감사 저장은 이 구현에 포함하지 않는다.
- reader가 반환한 객체는 저장 상태 및 다른 요청과 분리된 도메인 snapshot이어야 한다.
- 단위 테스트는 가짜 reader를 사용하며 PostgreSQL·Redis를 실행하지 않는다.
