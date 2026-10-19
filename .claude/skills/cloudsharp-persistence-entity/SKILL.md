---
name: cloudsharp-persistence-entity
description: >
  CloudSharp Infrastructure의 EF Core 영속성 엔티티와 Core 도메인 간 데이터 매핑을
  작성하거나 리팩토링할 때 사용한다. ToDomain/FromDomain/UpdateFrom의 책임,
  식별자 보존, 값 객체 복원, nullable 값과 버전·시간의 무손실 전달을 다룬다.
  Core 도메인 행위·상태 전이만 작성하는 요청은 cloudsharp-domain-model 대상이다.
---

# CloudSharp 영속성 엔티티 작성

## 작업 범위와 먼저 볼 코드

대상은 `src/CloudSharp.Infrastructure/Persistence/Entities/*Entity.cs`와 그 도메인 매핑이다. Core의 `User` 같은 Aggregate와 Infrastructure의 `UserEntity`는 서로 다른 모델이다.

- `.llm/index.md`, `.llm/database/README.md`, 대상 `.llm/database/<area>.md`와 `.llm/domains/<aggregate>.md`에서 컬럼·식별자·복원 계약을 확인한다.
- 실제 대상 도메인의 `Reconstitute`, 값 객체, `Persistence/Configurations`, repository를 함께 읽는다. User의 기준 구현은 `UserEntity`, `UserConfiguration`, `UserRepository`다.
- Core 도메인 생성·행위·상태 전이를 변경할 때는 기존 `cloudsharp-domain-model` 스킬의 범위도 적용한다. 그 스킬의 도메인용 private setter·비즈니스 검증 규칙을 EF 엔티티에 그대로 적용하지 않는다.

## 책임 경계

| 위치 | 책임 |
|---|---|
| Core 도메인 | `Create`, `Reconstitute`, 값 객체, 비즈니스 규칙과 상태 전이 |
| Infrastructure 영속성 엔티티 | DB에 저장할 데이터와 도메인 간 명시적 매핑 |
| `IEntityTypeConfiguration` | 테이블·컬럼·길이·NULL·인덱스·FK·CHECK·기본값·concurrency token 등 EF 스키마 매핑 |
| Repository | 쿼리·materialization·조회 결과 없음 처리, Add/Update 등록, EF 추적과 원래 concurrency 값 설정 |
| UnitOfWork / TransactionExecutor | 저장·DB 오류 변환·commit/rollback·시도 종료 시 추적 정리 |

의존 방향은 `Infrastructure → Core`다. Core에 `UserEntity`, `DbContext`, EF attribute 또는 Infrastructure 타입을 받는 변환 메서드를 추가하지 않는다. 엔티티도 DB 조회·저장·트랜잭션·인가·상태 전이를 실행하지 않는다. EF 데이터 모델의 public setter는 허용하며, API DTO로 재사용하지 않는다.

## 엔티티의 매핑 메서드

대상 도메인이 `User`라면 다음 형태를 사용한다. repository의 `ReconstituteUser`, `ToEntity`, `CopyTo` 같은 필드별 매핑은 엔티티에 모은다.

| 메서드 | 동작과 보존 조건 |
|---|---|
| `public User ToDomain()` | 자신의 저장값으로 값 객체와 `User.Reconstitute(...)`를 호출해 독립적인 도메인 snapshot을 반환한다. 엔티티와 도메인을 변경하지 않는다. |
| `public static UserEntity FromDomain(User user)` | 새 영속성 인스턴스를 만든다. **`Id`를 포함한** 모든 저장 대상 값을 복사한다. 신규 도메인의 `Id = 0`과 기존 도메인의 생성된 Id를 모두 보존한다. |
| `public void UpdateFrom(User user)` | 기존 인스턴스의 저장 대상 값을 갱신한다. **수신 엔티티의 `Id`는 변경하지 않는다.** 추적 중인 객체를 새 인스턴스로 교체하지 않는다. |

`FromDomain`은 Id를 지정한 후 `UpdateFrom`을 재사용해 복사 목록을 한곳에 둘 수 있다. null 도메인 인수는 `ArgumentNullException.ThrowIfNull`로 거부한다. 조회 결과가 없을 때의 `null` 처리는 repository에서 `entity?.ToDomain()`처럼 처리한다.

### 복원과 복사의 세부 기준

- `ToDomain`은 신규 `Create`나 행위 메서드를 호출하지 않는다. 매퍼에 신규 입력 validation, Trim/대소문자 변환, 정규화값 재계산, 기본값 보정, 새 UUID 생성, 현재 시각 조회를 넣지 않는다. 기존 `Reconstitute`의 최소 방어 검증 계약은 그대로 사용하며, 저장 데이터 오류를 정상적인 조회 결과 없음으로 숨기지 않는다.
- 값 객체는 `Reconstitute`로 복원한다. 예를 들어 `EmailAddress.Reconstitute(Email, NormalizedEmail)`에 표시값과 저장된 비교값을 각각 전달한다. 매퍼에서 `EmailAddress.Create(Email)`로 비교값을 새로 만들지 않는다.
- 선택 값 객체의 두 컬럼은 함께 null이거나 함께 값이 있는 계약을 따른다. `UserName`/`NormalizedUserName`처럼 짝인 필드와 모든 nullable timestamp를 함께 다룬다. `UpdateFrom`은 null도 복사하여 기존 선택값을 지울 수 있어야 한다.
- `Version`, `SecurityVersion`, 상태와 모든 timestamp는 도메인/저장 snapshot의 값을 전달한다. 매핑 과정에서 버전을 증가시키거나 정지·삭제 상태를 전이시키거나 `UpdatedAt`을 갱신하지 않는다.
- `Id`, `PublicId`, 생성 시각, 값 객체의 원본·정규화값, 보안 필드, enum, 버전, 선택값까지 대상 컬럼 목록과 대조한다. `UpdateFrom`의 Id 보존 외에는 기존 매핑 계약을 임의로 바꾸지 않는다. `PublicId` 생성·불변성은 Core의 계약이다.
- 비밀번호 원문이나 token 원문을 영속성 필드로 추가하지 않는다. User는 승인된 `PasswordHash`만 전달한다.

## Repository와 연결할 때

- EF가 지원하지 않는 `ToDomain()`을 `IQueryable`의 필터나 DB projection에 넣지 않는다. 기존 조회 조건을 유지하고 materialization 후 변환한다. 독립적인 도메인 snapshot을 반환하는 조회는 기존 `AsNoTracking` 계약을 유지한다.
- Add는 `FromDomain`으로 만든 별도 엔티티만 추적한다. DB가 생성한 내부 Id를 입력 도메인 객체에 써 넣지 않는다. User 등록은 기존처럼 저장 후 같은 트랜잭션에서 PublicId로 재조회한다.
- Update는 동일 Id로 이미 추적 중인 엔티티가 있으면 `UpdateFrom`을 호출한다. 없는 경우 `FromDomain`으로 만든 엔티티를 repository가 등록한다.
- `expectedVersion`은 변경 전 관측한 값이다. repository가 EF `Version.OriginalValue`에 설정하고 필요한 UPDATE를 등록한다. 엔티티 메서드에 EF `Entry`, `OriginalValue`, `IsModified`를 옮기거나 변경된 버전에서 원래 값을 역산하지 않는다.
- 매핑 위치만 바꿀 때 repository 인터페이스, 취소 처리, 오류 처리, 저장 시점과 transaction 경계를 함께 변경하지 않는다. 스키마 변경이 없다면 migration도 추가하지 않는다.

## 완료 확인

대상 매핑 변경에 맞는 검증만 수행한다.

- DB 없이 엔티티↔도메인 round trip, 신규·기존 Id 복사, 추적 엔티티 Id 보존, 선택값 해제, 상태·버전·시간 보존을 확인한다. nullable 필드는 값이 있는 경우와 없는 경우를 다룬다.
- 기존 repository의 추적·등록·동시성 계약이 유지되는지 관련 테스트를 실행한다. SQL/스키마/저장 동작을 바꿨다면 해당 EF 설정 테스트와 실제 PostgreSQL 통합 테스트도 실행한다.
- build와 필요한 테스트 결과를 보고한다. Docker 등 실행 환경이 없으면 통과로 처리하지 말고 실행하지 못한 검증을 명시한다.

이 스킬은 `.agents/skills/cloudsharp-persistence-entity/SKILL.md`와 `.claude/skills/cloudsharp-persistence-entity/SKILL.md`에 같은 내용으로 유지한다.
