# errors

Error catalog and ProblemDetails examples. See .llm/api/common-contract.md for status mapping.

## Space 인가 내부 오류

아래 code는 `ErrorCodes.Auth`와 `SpaceAuthorizationError`가 정의한다. 서비스는 HTTP status를
결정하지 않으며, 외부 code·message·details와 masking은 후속 API 공통 mapper에서 변환한다.

| 내부 code | 원인 |
|---|---|
| `AUTH_SPACE_NOT_FOUND` | Space 미존재 |
| `AUTH_SPACE_ACCESS_DENIED` | 기본 Space 접근 자격 없음 |
| `AUTH_SPACE_STATE_NOT_ALLOWED` | 허용되지 않은 Space 상태. `ActualSpaceStatus`에 실제 상태 보존 |
| `AUTH_SPACE_PERMISSION_DENIED` | 요구 Permission 전체를 충족하지 못함 |

MCP actor는 기존 `AUTH_TOKEN_TYPE_NOT_ALLOWED`, 비정상 Session identity는 기존
`AUTH_TOKEN_INVALID`를 사용한다. reader 가용성 실패는 `DEPENDENCY_UNAVAILABLE`를 보존하고
인증·권한 거부로 바꾸지 않는다. 외부 마스킹 정책은 [권한 계약](../permissions/README.md)을 따른다.
