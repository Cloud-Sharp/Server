---
name: cloudsharp-infrastructure-integration-tests
description: >
  CloudSharp Infrastructure 어댑터의 동작을 구현하기 전에 NUnit 통합 테스트를 작성한다.
  PostgreSQL, Redis 등 실제 외부 시스템과의 포트·저장소 계약을
  서로 다른 회귀를 잡는 소수의 시나리오로 검증할 때 사용한다.
---

# CloudSharp Infrastructure 통합 테스트 우선 작성

Infrastructure 테스트의 목표는 **외부 시스템과의 계약이 실제로 지켜지는지를 적은 수의 의미 있는 통합 테스트로 검증하는 것**이다. TDD에서는 새 동작이나 변경할 동작을 구현하기 전에 기대 계약을 NUnit 통합 테스트로 작성한다.

| 검증 계층 | 검증할 계약 | 진입점과 의존성 |
|---|---|---|
| Core | 입력 검증, 인가 이후의 업무 판단, 상태 전이, 저장·롤백 요청 | 실제 `ExecuteAsync`와 포트 더블 |
| Infrastructure | SQL 조회·제약, 저장·롤백 결과, Redis TTL·원자성 등 외부 시스템이 보장하는 동작 | 실제 포트 구현과 격리된 외부 시스템 |
| API | HTTP 바인딩·인증 파이프라인, 상태 코드와 응답 형식 | 실제 API 호스트와 HTTP 요청 |

예를 들어 소유권 이전에서 Core는 유즈케이스가 저장 실패 시 롤백을 요청하는지 확인한다. Infrastructure는 PostgreSQL에서 실패 후 별도 scope에서 다시 읽었을 때 일부 변경도 남지 않았는지 확인한다. 해당 계층의 설계 기준은 [Core 유즈케이스 테스트](../cloudsharp-use-case-unit-tests/SKILL.md)와 [API 통합 테스트](../api-integration-tests/SKILL.md)를 참고한다.

## 케이스 선택

1. `CLAUDE.md`와 `.llm/index.md`에서 관련 설계를 찾는다. 변경된 Core 포트, Infrastructure 어댑터와 실제 등록 코드, 인접 테스트를 읽는다. PostgreSQL 스키마나 Redis 키·페이로드처럼 외부 시스템과 맞물리는 계약의 근거를 확인한다. 설계와 구현이 다르면 시나리오가 따르는 계약을 명시한다.
2. **기존 테스트가 놓칠 수 있는 구체적인 회귀**를 계약마다 적는다. 정상 경로는 변경된 매핑·쿼리·외부 쓰기를 입증할 수 있는 대표 시나리오부터 검토한다. 실패나 경계 사례는 실제 공급자의 제약, 정렬·커서, 경합, 만료처럼 별도의 결함을 드러낼 때 추가한다.
3. 같은 회귀를 잡는 기존 케이스를 재사용하거나 보강한다. 여러 입력이 같은 외부 동작을 거치면 그 차이를 가장 잘 드러내는 입력을 고른다. DB 없이 확인할 수 있는 순수 매핑·직렬화·오류 분류는 유닛 테스트에서 검증한다. 통합 테스트에는 실제 저장 형식이나 공급자 동작에 의존하는 시나리오를 남긴다.

케이스를 고를 때 **검증할 계약과 잡을 회귀 · 준비할 외부 상태와 호출할 포트 · 기대 결과와 독립적으로 관찰할 외부 상태 · 기존 케이스 재사용 또는 추가**를 정한다. 새 케이스마다 기존 검증과 구별되는 근거가 있어야 한다.

## 외부 결과 확인

- 제품에 쓰이는 어댑터와 공급자 설정을 사용한다. PostgreSQL과 Redis는 `tests/CloudSharp.Infrastructure.Tests/`의 인접 Testcontainers 픽스처를 참고한다. 외부 시스템의 보장은 해당 동작을 제공하는 격리 환경에서 검증한다. 대체 환경으로 실행했다면 그 환경에서 관찰한 범위를 기록한다.
- 호출 결과와 실제 반영 상태를 함께 확인한다. 저장·커밋·롤백은 새 DI scope의 `DbContext`나 독립 연결에서 읽는다. 읽기와 쓰기가 같은 매핑을 써서 오류를 가릴 수 있으면 외부 시스템에 직접 준비한 데이터나 Redis 키·TTL, 실제 DB 컬럼 값을 필요한 만큼 관찰한다. 실패가 반환되면 데이터의 미반영이나 기존 상태 보존도 계약에 따라 확인한다.
- 데이터는 테스트별로 격리하고 픽스처가 만든 자원만 정리한다. 버전 경합은 별도 scope와 명시적인 실행 순서로 재현한다. Redis 만료는 서버 시각과 TTL 허용 범위를 기준으로 삼고, 비동기 반영이 필요한 경우 제한 시간 안에서 상태를 확인한다. `EnsureCreatedAsync`로 만든 스키마는 현재 EF 모델을 검증한다. 마이그레이션 계약은 실제 마이그레이션을 적용한 DB로 검증한다.

### 저장소의 대표 사례

- PostgreSQL `SpaceRepositoryIntegrationTests.UpdateAsync_OnExternallyChangedVersion_ShouldFailWithPreconditionFailed`는 다른 scope가 버전을 갱신한 뒤 실제 저장이 오래된 변경을 거부하는지 확인한다. 여러 쓰기의 원자성이 변경 대상이면 `SpaceMembershipPersistenceIntegrationTests.DeleteAsync_OnStaleMembershipVersion_ShouldRollbackSp10ShapedTransfer`처럼 실패 후 DB의 상태를 다시 읽는다.
- Redis `RedisSessionStoreIntegrationTests.StoreAsync_ShouldKeepBodyTtlWithinAbsoluteExpiry`는 실제 Redis에 저장된 세션 TTL이 절대 만료 시점을 넘지 않는지 확인한다. 세션 제한의 원자성이 변경 대상이면 같은 픽스처의 `StoreAsync_WithConcurrentIssuance_ShouldKeepAtMostTenSessions`를 참고한다.

## 테스트 작성과 완료 기준

`tests/CloudSharp.Infrastructure.Tests/`의 인접 픽스처와 NUnit 관례를 따른다. 외부 시스템을 사용하는 통합 픽스처에는 기존 `[Category("Integration")]`를 사용한다. 포트 호출의 기대 결과와 외부 시스템에서 관찰할 상태를 단언한다. 저장소 루트에서 `<FixtureName>`을 실제 클래스 이름으로 바꿔 실행한다.

```powershell
dotnet test tests/CloudSharp.Infrastructure.Tests/CloudSharp.Infrastructure.Tests.csproj --filter 'FullyQualifiedName~<FixtureName>'
```

통합 테스트 전체가 검증 범위에 들면 `--filter 'TestCategory=Integration'`을 사용한다.

- 구현 전(Red)에는 빌드 성공, 테스트 발견, 실제 외부 시스템 준비를 확인하고 기대 계약을 충족하지 못해 실패하는지 확인한다. 빌드·픽스처·Docker·연결 오류로 실행하지 못했다면 원인과 검증하지 못한 범위를 기록한다.
- 구현 후(Green)에는 같은 테스트를 다시 실행해 포트 결과와 외부 시스템 상태가 계약을 충족하는지 확인한다.

이 스킬은 `.agents/skills/cloudsharp-infrastructure-integration-tests/SKILL.md`와 `.claude/skills/cloudsharp-infrastructure-integration-tests/SKILL.md`에 같은 내용으로 유지한다.
