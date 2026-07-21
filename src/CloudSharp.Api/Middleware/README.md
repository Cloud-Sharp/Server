# Middleware

Request pipeline middleware (correlation id, error wrap, etc.).

## CorrelationIdMiddleware

`CorrelationIdMiddleware`는 요청마다 correlation ID를 확정하고 `HttpContext.TraceIdentifier`,
오류 응답의 `requestId`, 응답 헤더, 구조화 로그 scope에 동일하게 사용한다.
W3C `Activity.TraceId`는 별도의 분산 추적 ID로 수정하지 않는다.

### 헤더 계약

| 항목 | 값 |
|---|---|
| 요청 헤더 | `X-Correlation-ID` (선택) |
| 응답 헤더 | `X-Correlation-ID` (항상 반환) |
| 오류 응답 `requestId` | 해당 요청의 correlation ID와 동일 |

### 승계 규칙

단일 `X-Correlation-ID` 헤더가 다음을 모두 만족하면 원문을 승계한다.

- 값이 비어 있지 않다.
- 허용 문자: 영숫자(`a-z`, `A-Z`, `0-9`) 및 `-`, `.`, `_`, `:`. (좌우 공백은 trim)
- 길이가 128자 이하다.

누락, 빈 값, 중복 값, 길이 초과, 비허용 문자가 포함된 값은 400을 반환하지 않고
UUIDv7 기반 32자리 서버 생성 ID로 대체한다. 입력 헤더 원문은 별도로 기록하지 않는다.

### 파이프라인 위치

`UseHttpsRedirection`과 향후 인증·예외 처리보다 앞에 등록한다.
`ResultHttpMapper`는 `HttpContext.TraceIdentifier`를 읽으므로 미들웨어가 설정한
correlation ID가 오류 응답의 `requestId`에 자동으로 반영된다.
