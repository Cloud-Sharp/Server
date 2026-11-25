# HTTP 요청 멱등성 필터 설계

Minimal API의 `IEndpointFilter`를 라우트 단위로 등록한다. 필터는 HTTP 요청을 멱등성 실행 결정으로 바꾸고, 완료된 요청은 저장된 업무 결과로 응답을 구성한다. PostgreSQL 선점·완료 기록과 업무 트랜잭션은 공통 실행 흐름이 담당한다. [공통 구조 기획](../http-request-idempotency-plan.md)

## 라우트 등록

`RequireIdempotency(policy)` 확장 메서드가 엔드포인트에 공통 필터와 멱등성 메타데이터를 붙인다. 라우트는 작업 정책을 한 번 등록하고, 필터는 요청 범위의 Core 시작 처리 서비스를 `HttpContext.RequestServices`에서 가져온다. 등록한 정책은 불변으로 유지한다. [기존 인가 필터](../../src/CloudSharp.Api/Endpoints/Spaces/SpaceAuthorizationFilter.cs)

```csharp
app.MapPost("/api/v2/example-jobs", SubmitExampleJobAsync)
    .RequireAuthorization(SessionAuthenticationDefaults.UserSessionOnlyPolicy)
    .AddEndpointFilter(AuthorizeExampleJobAsync)
    .RequireIdempotency(exampleJobIdempotency);
```

| 정책 항목 | 계약 |
|---|---|
| 작업 식별자와 결과 버전 | 저장된 복구 결과를 현재 라우트에 연결하고, 지원하는 결과 버전과 응답 변환기를 지정한다. |
| 요청 범위 | 기본은 인증된 요청자·메서드·표준 경로다. 구체적인 대상 경로 값을 포함하고 비밀 토큰 구간은 해시로 식별한다. 익명 작업은 요청자 범위를 계산하는 정책을 명시한다. |
| 지문 입력 | 바인딩된 DTO에서 업무 입력을 명시적으로 고른다. 미디어 유형, 의미 있는 쿼리 및 조건 헤더도 입력에 포함한다. 업무상 차이가 있는 필드 생략과 `null`을 유지한다. 본문 없는 API는 빈 본문을 사용한다. |
| 응답 재생 | 복구 결과를 받아 최초 상태 코드, 본문, 필요한 `Location`·`ETag`를 구성한다. 일회성 비밀 응답은 공개 가능한 식별자와 재생 제한 오류를 구성한다. |

지문은 고정된 속성 순서로 직렬화한 구조화 입력에서 계산한다. 배열은 업무상 순서를 유지하고, 순서가 의미 없는 집합은 투영 단계에서 정렬한다. 저장소에는 계산한 지문만 넘긴다. OpenAPI는 같은 라우트 메타데이터에서 필수 `Idempotency-Key` 헤더를 선언한다. [ASP.NET Core OpenAPI 메타데이터](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/include-metadata?view=aspnetcore-10.0)

## 요청 처리 순서

1. 인증 미들웨어와 라우트 인가를 통과한다. 대상 리소스의 현재 접근 권한을 확인하는 엔드포인트 필터는 `RequireIdempotency`보다 먼저 등록한다. 바인딩과 형식 검증에 실패한 요청은 기존 오류 처리로 보낸다. 만료, 재고, 사용 슬롯 등 첫 실행의 업무 상태 검증은 재생 판단 이후 유즈케이스에서 수행한다. [API 파이프라인](../../src/CloudSharp.Api/Program.cs) · [Minimal API 필터 순서와 바인딩](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/min-api-filters?view=aspnetcore-10.0)
2. 필터가 `Idempotency-Key`를 읽는다. 출력 가능한 ASCII 16~128자의 단일 값을 요구하고 값의 대소문자와 내용을 유지한다. 누락·복수 값·형식 오류는 공통 오류 형식의 `400 REQUEST_VALIDATION_FAILED`와 필드 상세 `idempotencyKey` / `INVALID_IDEMPOTENCY_KEY`로 응답한다. [현재 헤더 검증 계약](../../src/CloudSharp.Api/Endpoints/Invites/InviteHttpContract.cs)
3. 요청 범위와 지문을 구성해 Core의 시작 처리에 전달한다. 완료 결과의 조회와 선점의 원자성은 같은 시작 처리에서 보장한다.

| Core의 시작 결과 | 필터 동작 |
|---|---|
| 새 실행 시도 | 실행 시도 정보를 요청 기능에 담고 `next`를 한 번 호출한다. |
| 완료 기록 | 라우트의 응답 재생 함수로 응답한다. |
| 같은 지문의 처리 중 요청 | `409 IDEMPOTENCY_REQUEST_IN_PROGRESS`를 반환한다. |
| 같은 범위와 키에 다른 지문 | `409 IDEMPOTENCY_KEY_REUSED`를 반환한다. |
| 저장소에서 판정할 수 없음 | `503 DEPENDENCY_UNAVAILABLE`을 반환한다. |

재생 응답은 작업 정책이 허용한 헤더만 복원한다. 오류 본문과 상관관계 헤더는 현재 요청의 ID를 사용한다. 지원하지 않는 결과 버전이거나 저장 결과를 복원할 수 없으면 공통 서버 오류로 처리하고 업무를 다시 실행하지 않는다. [오류 응답 처리](../../src/CloudSharp.Api/Middleware/ErrorHandlingMiddleware.cs)

## 실행 시도 전달과 종료

시작·업무 실행·선점 해제는 Core 계약을 따른다. [Core 요청 멱등성 유즈케이스](../use-cases/idempotency-use-cases.md)

- 새 시도는 `HttpContext.Features`의 요청 단위 기능에 둔다. 핸들러가 공통 API 도우미를 통해 시도를 인수하고 Core의 실행 흐름에 명시적으로 전달한다. Core는 전달받은 시도를 검증하고 업무 변경과 완료 기록을 같은 트랜잭션에 커밋한다. [Core 트랜잭션 계약](../../src/CloudSharp.Core/Abstractions/Transactions/README.md#실패와-취소)
- 핸들러가 시도를 인수하기 전에 종료되면 API가 해당 시도 ID에 한해 선점을 해제한다. 인수 이후에는 Core가 미커밋을 확인한 경우에만 해제한다. 커밋 결과가 불명확하면 영속 기록을 먼저 확인하고, 해제에 실패한 선점은 만료 규칙에 따라 회수한다.
- Core가 트랜잭션 재시도 중 이미 완료된 기록을 발견하면 복구 결과를 반환한다. 핸들러는 이 결과에 필터와 같은 응답 재생 함수를 사용한다. 최초 성공 응답은 업무 트랜잭션의 커밋이 확인된 뒤 반환한다.

## 검증 시나리오

- 라우트에 정책을 추가하면 헤더 검증과 OpenAPI 표기가 함께 활성화된다. 적용을 선언하지 않은 라우트는 기존 흐름으로 처리한다.
- 형식이 잘못된 요청과 인가에 실패한 요청은 선점이나 재생에 진입하지 않는다.
- 새 시도만 핸들러를 호출한다. 완료·진행 중·지문 충돌·저장소 오류는 각각 정의한 응답을 반환한다.
- 핸들러가 시도를 인수하기 전에 종료되면 해당 선점만 해제한다. 인수 이후 발생한 예외에서는 API가 임의로 선점을 해제하지 않는다.
- 바인딩된 입력의 JSON 속성 순서가 달라도 같은 지문을 계산한다. 실제 대상이나 업무 입력이 달라지면 범위 또는 지문이 달라진다.
- 최초 결과 재생과 트랜잭션 재시도 중 발견한 완료 결과가 같은 응답 변환을 사용한다. 일회성 비밀과 허용 목록 밖의 응답 헤더는 재생 결과에서 제외한다.
