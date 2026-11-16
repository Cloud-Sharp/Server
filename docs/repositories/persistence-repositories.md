# Infrastructure Repository

CloudSharp PostgreSQL Repository에 적용하는 공통 저장·트랜잭션 계약이다. 도메인별 표의 **후속**은 Core 포트가 정의되어 있고 PostgreSQL 구현과 DI 등록이 남은 작업이다. Redis Session 저장은 [Auth 유즈케이스](../use-cases/auth-use-cases.md#공통-계약)에서 다룬다.

## 공통 계약

- [PostgreSQL DI](../../src/CloudSharp.Infrastructure/Persistence/DbContext/PersistenceServiceCollectionExtensions.cs)는 Repository, Unit of Work, Transaction Executor, Dapper 세션을 scoped로 등록한다. 같은 scope의 `AppDbContext`로 EF 변경 추적과 Dapper 연결·트랜잭션을 공유한다.
- EF 조회는 [공용 조회 확장](../../src/CloudSharp.Infrastructure/Persistence/DbContext/DbContextReadExtensions.cs)과 `AsNoTracking`을 사용해 독립적인 Core 스냅샷을 만든다. 단건 조회에서 행이 없으면 성공 값 `null`, 존재 확인에서 없으면 `false`다. 알려진 PostgreSQL 가용성 장애는 `DEPENDENCY_UNAVAILABLE`로 반환한다. 취소, SQL 오류, 잘못된 저장값과 예상 밖 예외는 호출자에게 전파한다.
- Repository의 EF 쓰기는 변경을 등록한다. UseCase가 [Unit of Work](../../src/CloudSharp.Infrastructure/Persistence/Transactions/EfCoreUnitOfWork.cs)에 저장을 요청하고, [Transaction Executor](../../src/CloudSharp.Infrastructure/Persistence/Transactions/EfCoreTransactionExecutor.cs)가 결과에 따라 커밋 또는 롤백한다. 실행 시도가 끝나면 Executor가 EF 추적 상태를 정리한다.
- 버전이 있는 변경에는 도메인 행위 전에 관찰한 버전을 `expectedVersion`으로 보낸다. EF 저장 시 동시성 충돌은 `PRECONDITION_FAILED`다. 도메인 상태 전이와 접근 권한 판정은 Core에서 수행한다. [트랜잭션 계약](../../src/CloudSharp.Infrastructure/Persistence/Transactions/README.md)

## 도메인별 문서

- [User Repository](user-repositories.md)
- [Space Repository](space-repositories.md)
- [Space Role Repository](space-role-repositories.md)
- [Space Membership Repository](space-membership-repositories.md)
- [Space Invite Repository](space-invite-repositories.md)
