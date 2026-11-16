# User Repository

[IUserRepository](../../src/CloudSharp.Core/Abstractions/Persistence/IUserRepository.cs)를 [UserRepository](../../src/CloudSharp.Infrastructure/Persistence/Repositories/UserRepository.cs)가 EF Core로 구현한다. 포트는 모든 메서드를 `ITransactionExecutor`가 연 트랜잭션 안에서 호출하는 계약이다. [Auth 유즈케이스](../use-cases/auth-use-cases.md)

| ID | 작업 | 주요 호출 경로·입력 | 결과와 계약 | 현황 |
|---|---|---|---|---|
| RP-01 | 이메일 존재 확인 `ExistsByNormalizedEmailAsync` | AU-01 · Core에서 trim 및 대문자 정규화를 끝낸 이메일 | `users.normalized_email`과 일치하는 행의 존재 여부를 반환한다. | 구현 |
| RP-02 | 사용자명 존재 확인 `ExistsByNormalizedUserNameAsync` | AU-01 · Core에서 정규화한 사용자명 | `users.normalized_user_name`과 일치하는 행의 존재 여부를 반환한다. | 구현 |
| RP-03 | 이메일로 계정 조회 `FindByNormalizedEmailAsync` | AU-02 · 정규화된 이메일 | 일치하는 User 스냅샷 또는 `null`을 반환한다. | 구현 |
| RP-04 | 내부 ID로 계정 조회 `FindByIdAsync` | Session 인증, US-01·US-03 · 사용자 내부 ID | 저장된 프로필, 계정 상태, 버전과 보안 버전을 담은 User 스냅샷 또는 `null`을 반환한다. | 구현 |
| RP-05 | PublicId로 계정 조회 `FindByPublicIdAsync` | AU-01 저장 후 재조회, SP-10 대상 계정 확인 · 사용자 PublicId | 일치하는 User 스냅샷 또는 `null`을 반환한다. 같은 트랜잭션에서 저장한 신규 계정을 읽으면 DB가 생성한 내부 ID가 포함된다. | 구현 |
| RP-06 | 신규 계정 등록 `AddAsync` | AU-01 · 새 User 도메인 객체 | 별도 UserEntity를 만들어 INSERT를 등록한다. 입력 User의 ID는 보존하며, 실제 저장과 생성된 내부 ID 조회는 호출 측에서 수행한다. | 구현 |
| RP-07 | 계정 변경 등록 `UpdateAsync` | US-03 · 저장된 ID가 있는 User와 관찰한 이전 버전 | 같은 ID의 추적 엔티티를 재사용한다. 추적 중인 엔티티가 없으면 User를 매핑해 UPDATE 대상으로 등록한다. 현재 버전은 도메인 값, EF 원본 버전은 `expectedVersion`이다. 같은 값의 갱신도 저장 시 버전을 검사한다. 미저장 User는 `InvalidOperationException`으로 거부한다. | 구현 |

- **등록.** AU-01은 계정을 등록·저장하고 같은 트랜잭션에서 PublicId로 다시 읽은 뒤 커밋한다. 첫 Session은 커밋 후 발급한다. 필드 매핑은 [UserEntity](../../src/CloudSharp.Infrastructure/Persistence/Entities/UserEntity.cs)가 담당한다.
- **저장 충돌.** 이메일·사용자명 사전 확인 이후에도 경합이 생길 수 있다. Unit of Work는 `ux_users_normalized_email`과 `ux_users_normalized_user_name` 저장 충돌을 각각 `USER_EMAIL_CONFLICT`, `USER_NAME_CONFLICT`로 변환한다. 대상 행이 삭제되거나 버전이 바뀐 상태에서 갱신하면 저장 시 `PRECONDITION_FAILED`다. [User 스키마](../../.llm/database/identity.md)
- **통합 테스트.** 저장 전 미반영, 자동 생성 ID의 재조회, 독립 스냅샷, 같은 트랜잭션의 반복 갱신, 유일성 충돌과 외부 버전 변경을 PostgreSQL에서 검증한다. [UserRepositoryIntegrationTests](../../tests/CloudSharp.Infrastructure.Tests/Persistence/Repositories/UserRepositoryIntegrationTests.cs)

[공통 Repository 계약](persistence-repositories.md#공통-계약)
