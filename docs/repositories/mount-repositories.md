# Mount Repository

Mount 목록·상세·개설·사용량, 명시적 권한 부여와 미리보기에 필요한 PostgreSQL 저장소 계약이다. [Mount 유즈케이스](../use-cases/mount-use-cases.md) · [공통 Repository 계약](persistence-repositories.md#공통-계약)

## 저장소 계약

| ID    | 작업·Core 포트                                                                                                                          | 호출 경로·입력                                                        | 반환과 계약                                                                                                                                                                      | 현황  |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --- |
| RP-20 | 목록 후보 조회 · [IMountListCandidateReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountListCandidateReader.cs) | MT-01 · Space 내부 ID, 행위자, cursor, 배치 크기, 정렬 필드·방향 | 정렬된 Mount와 명시적 부여, 후보별 `CursorAfter`, `HasMore`를 반환한다. Core가 읽을 수 있는 후보를 골라 응답 페이지를 채운다. | 구현 |
| RP-21 | 부여 대상 조회 · [IMountCreationGrantTargetReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountCreationGrantTargetReader.cs) | MT-02·08·09 · Space 내부 ID, Role PublicId 목록, User PublicId 목록 | 같은 Space의 Role과 요청한 사용자 계정·해당 Space의 선택적 Membership을 PublicId별로 반환한다. Role 삭제 시각, 사용자 상태, Membership 상태를 보존해 Core가 부여와 회수의 자격을 판단할 수 있게 한다. | 구현 |
| RP-22 | Mount 개설 등록 · [IMountCreationWriter](../../src/CloudSharp.Core/Abstractions/Persistence/IMountCreationWriter.cs) | MT-02 · 새 Mount와 초기 부여 | Mount와 초기 권한 부여를 한 Unit of Work에 등록하고 등록 결과를 반환한다. | 후속 |
| RP-23 | 상세 스냅샷 조회 · [IMountReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountReader.cs) | MT-03~06 · Space 내부 ID, Mount PublicId | RP-24와 같은 조회로 Mount 상태·버전·명시적 부여를 복원한다. 범위 내 Mount가 없으면 `null`이다. | 구현 |
| RP-24 | Mount 단건 조회 · [IMountReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountReader.cs) | MT-07~10 · Space 내부 ID, Mount PublicId | 명시적 부여를 포함한 독립 `Mount` 스냅샷을 반환한다. 범위 내 Mount가 없으면 `null`이다. | 구현 |
| RP-25 | Mount 변경 등록 · [IMountMutationWriter](../../src/CloudSharp.Core/Abstractions/Persistence/IMountMutationWriter.cs)                    | MT-04~06·08~09 · 변경한 Mount와 조회에서 관찰한 이전 버전                      | Mount와 명시적 부여 행의 추가·갱신·삭제를 한 Unit of Work에 등록한다. 저장 시 버전 충돌은 `PRECONDITION_FAILED`다.                                                                                        | 후속  |
| RP-26 | 명시적 부여 페이지 조회 · [IMountGrantReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountGrantReader.cs) | MT-07 · Space·Mount 내부 ID, 대상 유형, cursor, 한도 | 지정한 유형의 저장 부여와 대상 PublicId를 대상 PublicId 오름차순으로 반환한다. 정지 사용자에게 남아 있는 부여를 포함하며, 다음 항목이 없으면 `NextCursor`는 `null`이다. | 구현 |
| RP-27 | 미리보기 대상 조회 · [IMountPreviewUserReader](../../src/CloudSharp.Core/Abstractions/Persistence/IMountPreviewUserReader.cs) | MT-10 · Space 내부 ID와 User PublicId, 또는 Space 내부 ID·cursor·배치 크기 | 단건의 사용자·Membership·Role 스냅샷이나 User PublicId 오름차순 후보 배치를 반환한다. Membership 행이 없는 Space 소유자도 조회할 수 있다. 배치는 후보별 `CursorAfter`와 `HasMore`를 포함하고, 유효 권한 비교와 페이지 채우기는 Core에서 수행한다. | 구현 |
| RP-28 | 사용량 조회 · `IMountUsageReader` | MT-11 · Space 내부 ID, Mount PublicId | 같은 Mount의 `PURGED` 이외 파일 크기를 집계한다. 파일이 없으면 `0`, 범위 내 Mount가 없으면 `null`을 반환한다. | 후속 · Content 구현 이후 |

## 조회와 cursor

- 기존 [ISpaceAuthorizationReader](../../src/CloudSharp.Core/Abstractions/Persistence/ISpaceAuthorizationReader.cs)를 재사용해 요청자의 최신 Space·Membership·Role을 읽는다. Mount 가시성, 관리 자격, 유효 권한은 Core가 판단한다. EF 조회는 공용 `ExecuteReadAsync`와 `AsNoTracking`으로 독립 스냅샷을 만든다. [Space Repository](space-repositories.md#저장소-계약)
- RP-20의 기본 정렬은 `CREATED_AT DESC`이며, 같은 생성 시각은 Mount PublicId로 정렬한다. cursor는 Space·행위자·정렬 조건에 묶는다. RP-26은 Space·Mount·대상 유형에 맞는 cursor를 확인한다. 잘못된 cursor는 `CURSOR_INVALID`를 반환한다.
- RP-27의 후보 조회는 현재 Space 내부 ID, cursor, 배치 크기를 받는다. Mount와 제안 공개 범위에 cursor를 바인딩하는 작업은 MT-10 연결 단계에서 다룬다. [공통 cursor 계약](../../.llm/api/common-contract.md#7-cursor-pagination)

## 저장 선행 조건과 트랜잭션

- Mount 상세는 `mounts`, `mount_role_grants`, `mount_user_grants`를 조회한다. Content 구현을 마친 뒤 RP-28을 추가한다. 사용량 조회는 `files`와 `directory_entries`를 같은 Mount의 엔트리 키로 연결해 `PURGED` 이외 파일의 `size_bytes`를 합산한다. [Mount 테이블](../../.llm/database/mounts.md) · [콘텐츠 테이블](../../.llm/database/content.md)
- 명시적 부여의 `permission_mask`는 Mount 전용 배치를 사용한다. 읽기는 `1`, 쓰기는 `2`, 다운로드는 `4`다. 권한 집합이 비면 대상의 행을 삭제한다. [Mount 비트 배치](../../.llm/database/README.md#mount-비트-배치)
- RP-22는 신규 Mount와 초기 권한 부여를 함께 등록한다. RP-25는 관찰한 이전 버전을 EF 동시성 원본 값으로 설정한다. 두 포트 모두 변경 등록을 담당한다. 기존 `IUnitOfWork`가 저장하고 `ITransactionExecutor`가 커밋·롤백한다. 구현체는 기존 `AddPostgreSqlPersistence`에 scoped로 연결한다. [PostgreSQL DI](../../src/CloudSharp.Infrastructure/Persistence/DbContext/PersistenceServiceCollectionExtensions.cs)
- RP-21의 직접 부여 대상 조회는 같은 Space·사용자의 자격 변경과 직렬화하고, 쓰기 트랜잭션에서 잠금을 얻은 뒤 최신 상태를 읽는다. 제거·탈퇴로 Membership이 `LEFT`가 될 때 같은 Space의 직접 부여 일괄 회수와 영향받은 Mount의 버전·수정 시각 갱신을 함께 커밋하는 연계가 남아 있다. 멤버십 정지는 저장된 부여를 보존한다. [멤버십 연동](../use-cases/mount-use-cases.md#space콘텐츠-연동과-검증-기준)
