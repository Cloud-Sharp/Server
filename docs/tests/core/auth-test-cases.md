# Auth 테스트 케이스

[Auth 유즈케이스](../../use-cases/auth-use-cases.md)에 해당하는 계정·Session 모델, 공통 Session 발급·인증, 등록·로그인·내 계정의 Core 유닛 테스트를 정리한다.

## 공통 검증

- 유즈케이스는 `ExecuteAsync`, Session 발급은 `IssueAsync`를 호출한다. 실제 도메인 객체, 고정 UTC 시계, 저장소와 트랜잭션 테스트 더블로 결과 DTO, Core `ErrorCode`, 호출 순서와 실패 후 상태를 확인한다.
- 비밀번호 검증·해시 계산에는 제출한 값의 공백이 보존된다. 계정과 Session 저장 경계에는 각각 비밀번호 해시와 토큰 해시를 전달한다. 발급 성공 DTO는 토큰 원문을 반환하며 `ToString()`에서 비밀값 마스킹을 확인한다.
- HTTP 상태·헤더, 실제 PostgreSQL·Redis의 저장 및 원자성은 [Auth 통합 테스트](../../../tests/CloudSharp.Api.IntegrationTests/README.md)에서 확인한다. AU-03 현재 Session 로그아웃, AU-04 전체 로그아웃, US-02 내 프로필 수정은 [유즈케이스의 후속 계약](../../use-cases/auth-use-cases.md#구현-현황)이다. 대응하는 Core 도메인 행위는 아래에 포함한다.

## User 도메인 모델

[UserTests](../../../tests/CloudSharp.Core.Tests/Domain/Users/UserTests.cs) · [EmailAddressTests](../../../tests/CloudSharp.Core.Tests/Domain/Users/EmailAddressTests.cs) · [NormalizedNameTests](../../../tests/CloudSharp.Core.Tests/Domain/Users/NormalizedNameTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| User-01 | 유효한 이메일과 비밀번호 해시로 계정을 생성한다. 선택 입력을 생략하거나 표시 이름을 공백으로 설정한다. | UUIDv7 PublicId, `ACTIVE`, `USER`, 버전과 보안 버전 1로 생성한다. 선택값이 없으면 `null`이고 공백 표시 이름도 `null`이다. |
| User-02 | 이메일·사용자명 앞뒤에 공백과 혼합 대소문자를 넣고 이메일 320자·사용자명 30자 경계를 확인한다. 저장값의 원문·정규화 쌍도 복원한다. | 표시 값은 양끝 공백을 제거하고 조회용 값은 대문자로 정규화한다. 경계 길이와 올바른 쌍을 허용하고, 빈 값·길이 초과 이메일·정규화 불일치는 복원 시 예외로 거부한다. |
| User-03 | 빈 값·형식 오류·320자 초과 이메일, 공백·30자 초과 사용자명, 100자 초과 표시 이름, 빈 값·512자 초과 비밀번호 해시, UTC 오프셋이 아닌 시각을 전달한다. | 각각 `USER_INVALID_EMAIL`, `USER_NAME_INVALID`, `USER_DISPLAY_NAME_INVALID`, `USER_PASSWORD_INVALID`, `USER_INVALID_STATE`를 확인한다. 표시 이름은 100자까지 허용한다. |
| User-04 | 저장된 User를 복원한다. 잘못된 ID·PublicId·enum·버전·표시 이름·시간 순서나 상태와 맞지 않는 정지·삭제 시각도 전달한다. | 정상 복원은 모든 필드를 보존하고 잘못된 스냅샷은 예외로 거부한다. |
| User-05 | 표시 이름을 바꾸거나 지운다. 같은 이름과 길이 제한을 넘는 이름도 적용한다. | 실제 변경은 버전과 수정 시각을 갱신한다. 같은 값은 무변경 성공이고, 길이 초과는 `USER_DISPLAY_NAME_INVALID`다. |
| User-06 | 비밀번호 해시 변경, 전체 Session 폐기용 보안 버전 증가, 시스템 Role 변경을 각각 실행한다. 같은 Role과 미정의 Role도 전달한다. | 비밀번호 변경·전체 폐기는 버전과 보안 버전을 모두 올린다. 시스템 Role 변경은 보안 버전을 보존하고 버전만 올린다. 같은 Role은 무변경 성공이고, 빈 해시·미정의 Role은 각각 `USER_PASSWORD_INVALID`, `USER_INVALID_ROLE`이다. |
| User-07 | 활성 계정을 정지·재활성화하고, 활성·정지 계정을 삭제한다. 계정 상태별 `CanLogin`도 확인한다. | 정지·삭제는 관련 시각을 기록하고 버전과 보안 버전을 올린다. 재활성화는 정지 시각을 지우고 버전만 올린다. 정지 중 삭제는 정지 시각을 지워 복원 가능한 상태를 만든다. `CanLogin`은 활성 상태에서만 참이다. |
| User-08 | 정지 계정을 다시 정지하거나 활성 계정을 활성화한다. 삭제된 계정의 상태·프로필·비밀번호·역할·보안 버전 변경도 시도한다. | 잘못된 전이는 `USER_INVALID_STATE`, 삭제된 계정의 변경은 `USER_DELETED`로 거부한다. |

## User Session과 토큰 해시

[UserSessionTests](../../../tests/CloudSharp.Core.Tests/Domain/Sessions/UserSessionTests.cs) · [TokenHashTests](../../../tests/CloudSharp.Core.Tests/Common/Tokens/TokenHashTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| Session-01 | 활성 사용자에 대해 UTC 시각 `T`에 Session을 발급한다. 음수 사용자 ID, 빈 사용자 PublicId, `null` 해시, 0 보안 버전, UTC 오프셋이 아닌 시각도 전달한다. | 정상 발급은 UUIDv7 SessionId와 발급·최근 사용 시각 `T`, 유휴 만료 `T+24시간`, 절대 만료 `T+7일`을 설정한다. 잘못된 발급 인수는 예외로 거부한다. |
| Session-02 | 저장된 Session을 복원한다. 빈 SessionId, UTC 오프셋이 아닌 시각, 발급·활동·유휴·절대 만료 시각의 역순도 확인한다. | 정상 복원은 저장값을 보존하고 잘못된 스냅샷은 예외로 거부한다. |
| Session-03 | 활성 계정의 해시·보안 버전이 일치하는 Session을 검증한다. 해시 불일치, 보안 버전 불일치, 정지·삭제 계정도 준비한다. | 유효한 Session은 통과한다. 불일치는 `AUTH_SESSION_INVALID`, 비활성 계정은 `AUTH_USER_INACTIVE`다. |
| Session-04 | 유휴·절대 만료 직전, 만료 시각, 유휴 만료 이후에 `Validate`를 호출한다. | 만료 직전까지 허용하고 만료 시각부터 `AUTH_SESSION_EXPIRED`다. UTC 오프셋이 아닌 현재 시각에는 `AUTH_SESSION_INVALID_STATE`를 반환한다. |
| Session-05 | 최근 사용 후 5분 미만·정확히 5분·5분 초과 시점에서 `TouchLastSeen`을 호출한다. 유휴 만료 갱신값이 절대 만료를 넘는 경우도 준비한다. | 5분 미만은 거짓과 무변경, 5분부터는 참과 함께 최근 사용·유휴 만료를 갱신한다. 새 유휴 만료는 절대 만료를 넘지 않는다. |
| Session-06 | 시간이 역행하거나 UTC 오프셋이 아닌 시각, 유휴·절대 만료 상태에서 활동 갱신을 시도한다. | 시각 오류는 `AUTH_SESSION_INVALID_STATE`, 만료는 `AUTH_SESSION_EXPIRED`다. |
| Hash-01 | 양끝에 공백이 있는 해시, 128자 해시를 생성하고 유효한 저장 해시를 복원한다. | 공백을 제거한 해시를 저장하고 128자 경계를 허용한다. 유효한 저장값은 `TokenHash`로 복원한다. |
| Hash-02 | `null`·빈 문자열·공백·129자 이상이거나 토큰 원문 접두사를 가진 값을 해시로 전달한다. | 생성은 `TOKEN_HASH_INVALID`를 반환한다. 잘못된 저장 해시 복원은 예외로 거부한다. |

## AU-01 계정 등록

[RegistrationUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Auth/Registrations/RegistrationUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| AU-01-01 | 이메일·사용자명에 양끝 공백을 넣고, 선택값 생략과 공백 표시 이름도 등록한다. | 정규화된 이메일·사용자명으로 중복 조회하고 활성 User를 한 번 등록·저장한다. PublicId로 다시 읽어 저장 ID를 확보한 뒤 커밋하고 첫 Session을 발급한다. DTO는 사용자 정보·Bearer 토큰·두 만료 시각을 반환한다. 선택값 누락과 공백 표시 이름은 `null`이다. |
| AU-01-02 | 비밀번호 길이 8·128자와 양끝 공백이 있는 비밀번호로 등록한다. 7·129자, `null`·빈 문자열·공백 전용 비밀번호도 전달한다. | 경계값을 허용하고 원문 그대로 해시한다. 정책 위반은 `USER_PASSWORD_INVALID`이며 해시 계산, 중복 조회, 계정·Session 등록을 시작하지 않는다. |
| AU-01-03 | 형식 오류 이메일, 31자 사용자명 또는 101자 표시 이름을 보낸다. | 각각 `USER_INVALID_EMAIL`, `USER_NAME_INVALID`, `USER_DISPLAY_NAME_INVALID`다. 비밀번호 해시는 계산하지만 중복 조회나 계정·Session 등록은 하지 않는다. |
| AU-01-04 | 이메일·사용자명 중복을 각각 또는 동시에 준비한다. 사전 조회를 통과한 뒤 저장 단계에서 유일성 충돌도 주입한다. | `USER_EMAIL_CONFLICT` 또는 `USER_NAME_CONFLICT`를 반환한다. 사전 조회에서 이메일 충돌을 먼저 확인하고, 저장 충돌은 롤백한다. Session을 발급하지 않는다. |
| AU-01-05 | 중복 조회, 계정 등록, 저장 또는 저장 후 재조회 단계에 가용성 오류를 주입한다. 재조회가 미저장 ID·다른 사용자·미존재를 반환하는 경우도 준비한다. | 가용성 오류는 `DEPENDENCY_UNAVAILABLE`이다. 재조회 계약 위반은 `InvalidOperationException`을 발생시킨다. 실패한 트랜잭션을 롤백하고 계정·Session을 남기지 않는다. |
| AU-01-06 | 계정 커밋 이후 Session 저장을 실패시키거나 발급 전에 요청을 취소한다. | 저장 실패는 `DEPENDENCY_UNAVAILABLE`, 취소는 `OperationCanceledException`이다. 커밋된 계정을 보존하며 취소 시 토큰 발급을 시작하지 않는다. |
| AU-01-07 | 커밋 전 테스트 시계를 진행시키고 저장에 전달한 취소 토큰을 기록한다. 이미 취소된 요청과 예상 밖의 포트 예외도 확인한다. | Session 만료를 발급 시점의 시계로 계산한다. Unit of Work에 트랜잭션 토큰을 전달하고 취소·예상 밖 예외를 전파한다. |
| AU-01-08 | 계정·Session에 등록한 값과 성공 DTO의 필드, 입력·결과의 `ToString()`을 확인한다. | 저장값에는 비밀번호·토큰의 해시를 전달한다. 응답 사용자 정보는 PublicId를 사용하고 내부 ID·비밀번호 해시를 노출하지 않는다. 입력·결과 문자열의 비밀번호와 발급 토큰은 마스킹한다. |

## AU-02 로그인

[LoginUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Auth/Logins/LoginUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| AU-02-01 | 대소문자와 양끝 공백이 다른 이메일로 활성 계정에 로그인한다. 조회 이후 시계를 진행시키거나 같은 계정으로 반복 로그인한다. | 정규화된 이메일로 조회하고 저장된 보안 버전과 발급 시점의 시각을 사용한다. 매번 별도 Session을 저장하고 사용자 정보·Bearer 토큰·유휴/절대 만료 시각을 반환한다. 사용자 상태·시각·버전은 보존한다. |
| AU-02-02 | 등록 정책보다 짧은 기존 비밀번호와 양끝 공백이 있는 비밀번호를 사용한다. | 비어 있지 않고 구조 검증을 통과하면 제출 값을 그대로 검증한다. 공백 없는 비밀번호에 공백을 붙여 제출하면 `AUTH_INVALID_CREDENTIALS`다. |
| AU-02-03 | 빈 값·공백·이메일 형식 오류 `LoginId`나 빈 값·공백·128자 초과 비밀번호를 전달한다. 계정 없음·정지·삭제·비밀번호 불일치도 재현한다. | 모두 `AUTH_INVALID_CREDENTIALS`이며 토큰을 발급하거나 Session을 저장하지 않는다. 입력 형식 오류는 사용자 조회 전 거부한다. |
| AU-02-04 | 사용자 조회나 Session 저장에 가용성 오류를 주입한다. | `DEPENDENCY_UNAVAILABLE`을 반환한다. 조회 실패 시 토큰을 발급하지 않고, Session 저장 실패 시 성공 DTO를 반환하지 않는다. |
| AU-02-05 | 조회 포트가 0 이하 ID를 가진 계정을 반환하거나 예상 밖 예외를 던진다. `null` 입력과 조회 전·후 취소도 확인한다. | 저장소 계약 위반은 `InvalidOperationException`, `null` 입력은 `ArgumentNullException`이다. 예외·취소는 전파되며 취소 후 토큰을 발급하지 않는다. |
| AU-02-06 | 로그인 입력·출력 DTO와 `ToString()`을 검사한다. | 사용자 응답은 PublicId·프로필 정보와 상태를 노출한다. 입력 비밀번호와 응답 토큰은 문자열 표현에서 마스킹된다. |

## 공통 Session 발급

[SessionIssuanceServiceTests](../../../tests/CloudSharp.Core.Tests/UseCases/Auth/Sessions/SessionIssuanceServiceTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 발급-01 | 저장된 계정으로 발급을 호출하고 저장 완료를 지연한다. | 현재 UTC 시각과 사용자 ID·PublicId·보안 버전으로 Session을 만들고 해시만 저장한다. 저장에 요청 취소 토큰을 전달하고, 저장이 끝난 뒤 원문 토큰과 저장값과 일치하는 만료 시각을 반환한다. 결과 문자열은 토큰을 마스킹한다. |
| 발급-02 | `null` 사용자, 미저장 사용자 또는 이미 취소된 요청으로 호출한다. | 각각 `ArgumentNullException`, `InvalidOperationException`, `OperationCanceledException`을 전파하며 토큰 발급과 저장을 시작하지 않는다. |
| 발급-03 | 발급기가 비어 있거나 토큰 원문인 해시를 주거나 저장이 실패한다. | 잘못된 해시는 `TokenHash` 검증 오류를 전달하고 저장하지 않는다. 저장 실패는 포트가 반환한 원래 오류를 전달한다. |
| 발급-04 | 발급·저장 포트에서 예상 밖 예외 또는 저장 중 취소를 발생시킨다. | 예외·취소를 호출자에게 전파한다. |

## 보호된 요청의 Session 인증

[AuthenticateSessionUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Auth/Authentication/AuthenticateSessionUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 인증-01 | 유효한 해시의 Session으로 인증하고 최신 계정이 SYSTEM_ADMIN인 경우를 확인한다. 최근 사용 시각보다 10분 뒤에도 인증한다. | 해시로 Session을 조회하고 연결된 사용자 ID로 계정을 읽는다. 결과는 별도 SessionId, 최신 사용자 정보와 시스템 Role을 반영한다. 계정과 Session의 최근 사용·만료 시각은 유지한다. |
| 인증-02 | 빈 값 또는 토큰 원문을 해시로 입력하거나 등록되지 않은 해시로 조회한다. | `AUTH_SESSION_INVALID`이며 사용자 조회를 시작하지 않는다. 잘못된 해시 형식은 Session 조회 전 거부한다. |
| 인증-03 | Session 유휴·절대 만료, 사용자 PublicId·보안 버전 불일치, 정지·삭제된 사용자를 각각 재현한다. | 만료는 `AUTH_SESSION_EXPIRED`, 식별자·버전 불일치는 `AUTH_SESSION_INVALID`, 비활성 계정은 `AUTH_USER_INACTIVE`다. |
| 인증-04 | Session 조회·사용자 조회 실패, 예상 밖의 포트 예외, 요청 취소 및 `null` Query를 확인한다. | 가용성 오류는 `DEPENDENCY_UNAVAILABLE`이다. 예상 밖 예외·취소는 전파되고 `null` Query는 `ArgumentNullException`을 던진다. |
| 인증-05 | 인증 Query의 문자열 표현을 확인한다. | `ToString()`에서 토큰 해시를 마스킹한다. |

## US-01 내 프로필 조회

[GetProfileUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Users/Profiles/GetProfileUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| US-01-01 | 인증된 사용자 ID로 일반 사용자·SYSTEM_ADMIN·선택 프로필 필드가 없는 계정을 각각 조회한다. | 최신 프로필과 같은 스냅샷의 User 동시성 버전을 반환한다. 누락된 사용자명·표시 이름은 `null`이며 상태·수정 시각·버전과 보안 버전을 변경하지 않는다. |
| US-01-02 | 사용자 ID가 0 이하이거나 조회한 계정이 없거나 정지·삭제됐다. | `AUTH_SESSION_INVALID`를 반환한다. 0 이하 ID는 저장소 조회 전에 거부한다. |
| US-01-03 | 사용자 조회에 가용성 오류·예상 밖 예외를 주입하고 요청 취소와 `null` Query를 확인한다. | 가용성 오류는 `DEPENDENCY_UNAVAILABLE`이며 조회 트랜잭션을 롤백한다. 예상 밖 예외·취소를 전파하며 `null` Query는 `ArgumentNullException`을 던진다. |
| US-01-04 | Query·응답 DTO의 속성과 문자열 표현을 검사한다. | Query의 조회 키는 `UserId`이고 결과는 `User`와 `Version`을 담는다. 사용자 DTO는 PublicId를 사용하며 내부 ID·비밀번호 해시를 노출하지 않는다. |

## US-03 비밀번호 변경

[PasswordChangeUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Auth/PasswordChanges/PasswordChangeUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| US-03-01 | 활성 사용자와 현재 Session이 일치하고 최신 User 버전 및 올바른 현재 비밀번호를 제출한다. | 새 해시를 생성하고 조회한 이전 버전으로 User 갱신을 등록해 저장·커밋한다. User 버전·보안 버전·수정 시각을 갱신한다. 커밋 후 현재 Session의 보안 버전만 올리고 다른 Session을 제거한다. 현재 SessionId·토큰 해시·발급·최근 사용·만료 시각은 보존한다. |
| US-03-02 | 현재·새 비밀번호의 공백을 포함해 제출하고 길이 경계와 새 비밀번호가 현재와 같은 요청을 확인한다. | 제출값을 그대로 검증·해시한다. 현재 비밀번호는 128자까지, 새 비밀번호는 8~128자를 허용한다. 같은 비밀번호를 제출해도 해시 교체를 수행하고 두 버전을 올린다. |
| US-03-03 | `null`·빈 값·공백 전용 비밀번호, 현재 비밀번호 128자 초과, 새 비밀번호 범위 위반 또는 토큰 원문을 해시 필드에 전달한다. | 비밀번호는 `USER_PASSWORD_INVALID`, 잘못된 해시는 `TOKEN_HASH_INVALID`다. Session·사용자 조회를 시작하지 않는다. |
| US-03-04 | 현재 Session이 없거나 요청의 사용자 ID·SessionId와 다르다. 조회 결과의 사용자가 없거나 Session의 보안 버전이 오래된 경우도 확인한다. | `AUTH_SESSION_INVALID`를 반환한다. Session 자체가 없거나 요청의 식별자와 다르면 사용자 조회를 생략한다. |
| US-03-05 | 정지·삭제된 사용자나 유휴·절대 만료된 Session으로 변경한다. | 각각 `AUTH_USER_INACTIVE`, `AUTH_SESSION_EXPIRED`로 거부한다. 비밀번호를 검증하거나 갱신하지 않는다. |
| US-03-06 | `ExpectedVersion`과 저장 버전이 다르거나 현재 비밀번호가 틀리다. | 버전 불일치는 비밀번호 검증 전에 `PRECONDITION_FAILED`로 거부한다. 비밀번호 불일치는 `USER_PASSWORD_MISMATCH`이며 계정과 Session을 바꾸지 않는다. |
| US-03-07 | Session·User 조회, User 갱신 등록 또는 저장이 실패한다. 저장 시 동시성 충돌도 주입한다. | 가용성 오류는 `DEPENDENCY_UNAVAILABLE`, 저장 충돌은 `PRECONDITION_FAILED`다. 등록 실패 후에는 저장을 호출하지 않는다. 시작한 변경 트랜잭션을 롤백하고 Session 후처리를 실행하지 않는다. |
| US-03-08 | 계정 커밋 후 Session 후처리가 실패하고 현재 Session 삭제는 성공한다. | `DEPENDENCY_UNAVAILABLE`을 반환하고 현재 Session을 삭제한다. 새 비밀번호·보안 버전은 커밋된 상태로 유지한다. |
| US-03-09 | 후처리와 현재 Session 삭제가 모두 실패한다. 보안 버전 보정 성공, 저장 충돌 또는 보정 저장 오류를 각각 주입한다. | 새 트랜잭션에서 최신 계정의 보안 버전을 한 번 더 올린다. 동시성 충돌 시 최신 상태를 다시 읽어 재시도하고, 다른 변경으로 보안 버전이 이미 올랐으면 추가 갱신을 생략한다. 보정 실패 시 보정만 롤백한다. 응답은 `DEPENDENCY_UNAVAILABLE`이며 새 비밀번호를 보존한다. |
| US-03-10 | 시작 전에 요청을 취소하거나 User 변경을 커밋한 직후 취소한다. | 시작 전 취소는 조회·변경 없이 전파한다. 커밋 이후에는 요청 취소와 독립적으로 Session 후처리를 완료하고 성공한다. |
| US-03-11 | 커밋과 후처리 사이에 현재 Session이 사라지거나 만료된다. | 변경은 성공하고 다른 Session을 제거한다. 사라진 Session은 재생성하지 않고, 만료된 Session의 보안 버전이나 만료 시각을 갱신하지 않는다. |
| US-03-12 | `null` Command를 전달하고 정상 Command·결과의 속성과 문자열을 검사한다. | `null`은 `ArgumentNullException`이다. 성공 DTO는 User `Version`만 담는다. Command 문자열에서 비밀번호와 토큰 해시를 마스킹한다. |

[User 도메인 설계](../../../.llm/domains/user.md) · [User Session 도메인 설계](../../../.llm/domains/user-session.md) · [인증·사용자 API 명세](../../../.llm/api/auth-users.md)
