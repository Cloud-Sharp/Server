# Auth API 통합 테스트 시나리오

Auth·내 계정 endpoint를 검증할 때만 읽는다. `.llm/api/auth-users.md`의 **현재 구현 범위**와 실제 코드를 먼저 확인하고, 그 작업에서 요청한 시나리오를 선택한다. 이 문서는 후속 기능의 구현을 승인하지 않는다.

## 현재 네 endpoint의 검증 후보

| 경계 | 확인할 결과 |
|---|---|
| `POST /api/v2/auth/registrations` | 201과 `Location: /api/v2/me`, `accessToken`·Bearer·만료·PublicId 사용자 정보; 새 scope에서 User 저장 및 Redis 세션 확인 |
| 중복 이메일·사용자명 | 409와 해당 `USER_*_CONFLICT`; 성공 응답이 아니며 새로운 User·Session이 생기지 않음 |
| `POST /api/v2/auth/sessions` | 정상 200; 없는 사용자·정지·삭제·잘못된 비밀번호·의미상 잘못된 입력은 동일한 401 `AUTH_INVALID_CREDENTIALS` |
| `GET /api/v2/me` | 실제 발급 Session으로 200과 `"v{version}"` ETag; 다른 사용자의 정보나 내부 ID·보안 필드 비노출 |
| `PUT /api/v2/me/password` | 최신 If-Match와 현재 비밀번호로 204·증가한 ETag, 실제 비밀번호 변경; 현재 Session 유지·다른 Session 거부 |
| 조건부 변경 실패 | If-Match 누락 428, 잘못된 형식·wildcard·복수 값 400, stale version 412; 저장값·세션 상태가 의도치 않게 바뀌지 않음 |
| handler 이전 실패 | malformed JSON·unknown field·잘못된 자료형·자동 DataAnnotations 검증이 표준 `REQUEST_VALIDATION_FAILED` 본문으로 반환됨 |
| 인증 경계 | token 누락·중복 Bearer header·위조·만료·다른 prefix·SecurityVersion 변경·비활성 사용자 거부; 상세 계정 상태를 외부에 누출하지 않음 |
| HTTP 응답 계약 | 성공·인증 실패·binding/validation 실패 모두 `Cache-Control: no-store`; JSON content type·camelCase·문자열 enum, correlation header와 `requestId` 일치 |
| PostgreSQL·Redis 장애 | 정상 로그인·보호 요청 확인 후 전용 dependency를 중단하여 503, 표준 오류 본문, 보호 리소스 접근 불가 확인; 시작 자체 실패를 요청 중 장애 검증으로 간주하지 않음 |

## 시나리오별 주의

- 보호 요청용 계정과 token은 실제 가입·로그인으로 만든다. 만료·손상·비활성 상태를 재현할 때만 setup helper로 테스트 소유 DB·Redis 상태를 바꾸고, 검증 요청은 끝까지 HTTP를 통과시킨다.
- raw JSON 실패 테스트는 `PostAsJsonAsync`가 생성할 수 없는 malformed body를 `StringContent` 등으로 직접 보낸다. 의도적으로 잘못된 JSON에 성공 DTO serializer를 사용해 입력을 정상화하지 않는다.
- 비밀번호 변경은 동일 사용자의 두 Session을 발급해 유지·폐기를 구분한다. 성공 응답 본문이 비어 있는지, 새 비밀번호로 로그인 가능한지, 이전 비밀번호는 거부되는지 확인한다. 실패 메시지에 비밀번호·token을 넣지 않는다.
- 새 scope의 DB 관찰은 `UserPublicId`를 기준으로 한다. 내부 PK가 테스트의 저장소 관찰에 필요한 경우에도 HTTP 응답의 ID 계약과 혼동하지 않는다. Session hash 조회는 실제 `ITokenHasher`로 수행하고 hash를 출력하지 않는다.
- 세션 payload 파싱 불가 값뿐 아니라 빈 GUID·보안 버전 0·시간 순서 역전처럼 복원 불변식을 위반하는 값도 요청한 손상 테스트에 포함한다.
- PostgreSQL enum mapping은 production DI를 유지한다. 별도 raw Npgsql connection을 만들 때도 enum·schema 생성 이후의 타입 로딩 순서를 점검한다.
- OpenAPI 계약 검증을 요청받으면 해당 전용 factory에서만 문서 노출 환경을 켜고, 실패 스키마가 `ErrorResponse`와 `application/json`인지 확인한다. 기본 Production factory를 Development로 바꿔 실제 오류 처리를 가리지 않는다.

## 후속 기능과 구분

현재 문서에서 후속으로 둔 가입 멱등성·Redis rate limit·세션 활동 갱신·로그아웃·프로필 PATCH·MCP·영속 AuditEvent는 기존 네 API 테스트를 작성한다는 이유로 구현하지 않는다. 관련 테스트를 요청받으면 먼저 실제 구현 여부를 확인하고 미구현 계약을 명시한다. 같은 Idempotency-Key 재요청이나 활동 시 만료 연장을 지금의 성공 동작으로 기대하지 않는다.
