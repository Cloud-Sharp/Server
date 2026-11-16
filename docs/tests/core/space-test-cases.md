# Space 테스트 케이스

[Space 유즈케이스 SP-01~SP-10](../../use-cases/space-use-cases.md#1-space)과 Space 도메인 모델, 공통 인가 정책의 Core 유닛 테스트를 정리한다.

## 공통 검증

- 유즈케이스는 `ExecuteAsync`의 결과를 확인한다. 인가 스냅샷과 저장 스냅샷이 다를 때는 최신 저장 상태·소유자·버전을 사용한다.
- 필드 검증은 `ErrorCode`와 `PropertyName`을 확인한다. 컨텍스트 누락은 `SPACE_AUTHORIZATION_CONTEXT_REQUIRED` / `Space`, 0 이하 버전은 `SPACE_EXPECTED_VERSION_INVALID` / `ExpectedVersion`이다. 각 픽스처에서 해당하는 입력을 전달하면 업무 조회나 변경 등록 전에 실패한다.
- 변경 테스트의 저장·커밋·롤백은 테스트 더블의 호출 기록으로 확인한다. 조회 시 버전이 다르면 변경을 등록하지 않는다. 저장 중 충돌을 주입한 경우에는 관찰한 이전 버전으로 변경 등록을 시도하고 `PRECONDITION_FAILED`로 롤백한다.

## 도메인 모델

[SpaceTests](../../../tests/CloudSharp.Core.Tests/Domain/Spaces/SpaceTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| Space-01 | 유효한 소유자, 이름, 설명, quota와 UTC 시각으로 생성한다. 이름 200자와 긴 설명, `null`·공백 설명도 확인한다. | UUIDv7 PublicId, `ACTIVE`, 버전 1, 사용량·예약량 0으로 생성한다. 문자열 양끝 공백을 제거하고 빈 설명은 `null`로 저장한다. |
| Space-02 | 이름 누락·공백·201자, 0 이하 소유자 ID, 음수 quota 또는 UTC 오프셋이 아닌 시각으로 생성한다. | 입력에 해당하는 도메인 오류를 반환한다. |
| Space-03 | 네 상태의 저장 스냅샷을 복원한다. 저장 전 ID 0과 정규화 전 저장 문자열도 준비한다. | ID, PublicId, 소유자, 문자열, 상태, 용량, 버전과 모든 시각을 보존한다. 잘못된 스냅샷·상태·생명주기 시각은 예외로 거부한다. |
| Space-04 | 이름·설명을 함께 수정한 뒤 같은 의미의 메타데이터·quota·소유자를 최대 버전에서도 적용한다. | 실제 수정은 정규화된 값을 저장하고 버전을 한 번 올린다. 같은 값은 성공하며 스냅샷을 유지한다. |
| Space-05 | 용량을 예약하고 확정·해제한다. 사용량과 예약량의 합과 같은 quota 및 무제한 quota로 변경한다. | 예약량과 사용량이 연산에 따라 이동하거나 감소한다. 합계와 같은 한도 및 `null`을 허용하고, 무제한이어도 카운터 오버플로는 거부한다. |
| Space-06 | `ACTIVE → ARCHIVED → ACTIVE` 후 삭제를 시작한다. 보관 상태에서 삭제를 시작하는 경우도 확인한다. | 보관·재활성화가 `ArchivedAt`을 설정·해제한다. 삭제 시작은 `DELETING`과 삭제 요청 시각을 기록하고 보관 시각을 지우며 용량 카운터를 보존한다. |
| Space-07 | `ACTIVE`·`ARCHIVED`·`DELETING`에서 예약량과 사용량을 정리한다. 용량이 남은 상태에서 삭제 완료를 요청하고, `DELETED`에서 다시 정리를 시도한다. | 정리는 보관·삭제 요청 시각을 보존한다. `DELETING`에서 두 카운터가 0이면 `DELETED`로 전이하고 완료 시각을 기록한다. 허용되지 않는 정리·완료는 실패하고 스냅샷을 유지한다. |
| Space-08 | 비활성 상태의 일반 변경, 잘못된 상태 전이·입력·시각, 최대 버전에서의 실제 변경을 시도한다. | 실패 결과를 반환하며 필드·시각·버전이 그대로다. |
| Space-09 | 활성 Space의 소유자를 변경한다. | 소유자·수정 시각·버전이 바뀌고, 식별자와 나머지 Space 정보가 보존된다. |

## 공통 Space 인가 정책

[SessionSpaceAuthorizationPolicyTests](../../../tests/CloudSharp.Core.Tests/Policies/Authorization/SessionSpaceAuthorizationPolicyTests.cs) · [SpacePermissionPolicyTests](../../../tests/CloudSharp.Core.Tests/Policies/Authorization/SpacePermissionPolicyTests.cs) · [SpaceAuthorizationQueryTests](../../../tests/CloudSharp.Core.Tests/Policies/Authorization/SpaceAuthorizationQueryTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| 인가-01 | Session OWNER, SYSTEM_ADMIN, 활성 멤버를 각각 평가한다. OWNER인 SYSTEM_ADMIN도 포함한다. | OWNER는 Membership 없이 전체 권한을 얻는다. SYSTEM_ADMIN 우회가 OWNER보다 우선한다. 활성 멤버는 할당된 유효 Role의 저장 Permission 합집합으로 평가한다. |
| 인가-02 | 기본 접근 자격이 없는 사용자 또는 필수 Permission이 부족한 멤버를 평가한다. 필수 Permission 없는 요청도 확인한다. | 기본 접근 자격은 Space 상태보다 먼저 검사한다. 활성 멤버는 유효 권한이 비어 있어도 필수 권한이 없으면 통과하며, 필요한 권한이 하나라도 빠지면 거부한다. |
| 인가-03 | 조회된 Space가 없거나 요청과 다르다. 허용 상태와 다른 Space, 삭제 중·삭제된 Space, MCP actor도 평가한다. | 미존재·다른 Space는 인가하지 않는다. 허용 상태 불일치 오류에 실제 상태가 담긴다. 삭제 중·삭제된 Space와 MCP는 해당 Session 정책에서 SYSTEM_ADMIN 또는 OWNER여도 거부한다. |
| 인가-04 | 다른 사용자·Space의 Membership, `SUSPENDED`·`LEFT`, 미할당·미조회·삭제·타 Space Role 등을 포함한다. | 유효한 할당만 권한에 기여한다. 저장되지 않은 Space와 0 이하 사용자 ID의 권한 집합은 비어 있다. OWNER는 정지 Membership이 있어도 전체 권한을 받는다. |
| 인가-05 | `Evaluate`에 `null` 인수·원소 또는 중복 저장 Role ID를 전달한다. `HasAll`에 필요한 권한을 모두 또는 일부만 가진 집합, 빈 요구 집합, 미정의 권한과 `null` 인수를 전달한다. | 평가 입력의 `null`과 중복 저장 Role ID는 예외를 발생시키며 미저장 Role의 중복은 허용한다. `HasAll`은 필요한 권한의 누락·미정의 값에 거짓, 전체 보유·빈 요구 집합에 참을 반환하고 `null`은 예외로 거부한다. |
| 인가-06 | 잘못된 권한·상태 또는 빈 허용 상태 집합으로 쿼리를 만들고, 정상 평가 후 원본 Role·권한·상태 컬렉션과 Membership을 변경한다. | 잘못된 쿼리는 예외로 거부한다. 쿼리·스냅샷·인가 컨텍스트와 평가 결과의 불변 컬렉션은 원본이 변경돼도 유지된다. |

## SP-01 접근 가능한 Space 목록 조회

[ListSpacesUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/Listings/ListSpacesUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-01-01 | 접근 가능한 후보와 비멤버·비활성 Membership·삭제 상태 후보를 여러 조회 배치에 섞는다. | 접근 가능한 후보를 먼저 추려 요청 한도까지 채운다. 반환 순서, PublicId·메타데이터·상태, OWNER·멤버의 `MyAccess`를 확인한다. |
| SP-01-02 | 다음 커서부터 계속 조회하거나, 남은 후보가 모두 비인가 상태인 페이지를 조회한다. | 커서를 이어 조회해도 중복이 없다. 더 반환할 수 있는 항목이 없으면 `nextCursor=null`이고, 처음부터 접근 가능한 후보가 없으면 빈 페이지다. |
| SP-01-03 | 읽기 포트가 빈 배치에 다음 페이지를 표시하거나 진행하지 않는 배치를 반환한다. | `InvalidOperationException`으로 잘못된 포트 응답을 감지한다. |
| SP-01-04 | 한도 0·101, 빈 문자열·공백 커서를 전달한다. | 각각 `SPACE_LIST_LIMIT_INVALID` / `Limit`, `SPACE_LIST_CURSOR_INVALID` / `Cursor`를 반환하며 후보를 읽지 않는다. |

## SP-02 Space 생성

[CreateSpaceUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/Creations/CreateSpaceUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-02-01 | 활성 Session 사용자가 유한 또는 무제한 quota로 생성한다. | 요청자가 OWNER인 `ACTIVE` Space와 루트 DirectoryEntry·Folder, ADMIN·MEMBER·VIEWER Role 및 Permission을 등록한다. 두 번 저장하고 한 번 커밋한다. 반환 DTO의 PublicId·버전 1을 확인한다. |
| SP-02-02 | 기본 리소스를 등록한 트랜잭션의 두 번째 저장에서 실패한다. | `DEPENDENCY_UNAVAILABLE`을 반환하고 커밋 없이 롤백한다. |
| SP-02-03 | MCP, 비활성 사용자, 잘못된 이름 또는 음수 quota로 생성한다. | `AUTH_TOKEN_TYPE_NOT_ALLOWED`, `AUTH_USER_INACTIVE`, `SPACE_NAME_INVALID` / `Name`, `SPACE_QUOTA_INVALID` / `AllowedBytes`에 해당하는 오류를 반환하며 변경을 등록하지 않는다. |

## SP-03 Space 상세 조회

[GetSpaceUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/Details/GetSpaceUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-03-01 | 인가 후 버전·메타데이터가 달라진 `ACTIVE`·`ARCHIVED` Space를 조회한다. | 인가된 Space ID로 읽는다. 최신 메타데이터·버전, Space·소유자·루트 폴더 PublicId와 컨텍스트의 `MyAccess`를 반환한다. |
| SP-03-02 | 조회 시 Space가 없거나 `DELETING`·`DELETED`로 바뀌었다. 컨텍스트 누락도 확인한다. | 미존재는 `AUTH_SPACE_NOT_FOUND`, 상태 거부는 실제 상태를 담은 `AUTH_SPACE_STATE_NOT_ALLOWED`다. 컨텍스트 누락 시 읽기를 시작하지 않는다. |

## SP-04 Space 정보 수정

[UpdateSpaceUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/MetadataUpdates/UpdateSpaceUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-04-01 | 이름만 수정하거나 설명을 새 값·명시적 `null`로 변경한다. | 생략한 필드는 보존한다. 명시적 `null`은 설명을 지운다. 변경값과 새 버전으로 결과를 반환하고 이전 버전으로 등록해 저장·커밋한다. |
| SP-04-02 | 정규화 결과가 기존과 같은 요청에 최신 버전·오래된 버전을 각각 사용한다. | 최신 버전의 무변경 성공은 버전·시각을 유지하고 저장하지 않는다. 오래된 버전은 무변경이어도 `PRECONDITION_FAILED`다. |
| SP-04-03 | Space가 없거나 활성 상태가 아니다. `SPACE_UPDATE`가 없는 컨텍스트, 컨텍스트 누락, 0 이하 버전, 유효하지 않은 이름도 전달한다. | `AUTH_SPACE_NOT_FOUND`, `AUTH_SPACE_STATE_NOT_ALLOWED`, `AUTH_SPACE_PERMISSION_DENIED` 또는 해당 필드 오류를 반환한다. 이름 오류는 `SPACE_NAME_INVALID` / `Name`이다. |
| SP-04-04 | 저장에서 동시성 충돌을 반환한다. | `PRECONDITION_FAILED`로 롤백한다. |

## SP-05 Space 보관

[ArchiveSpaceUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/Archives/ArchiveSpaceUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-05-01 | `SPACE_UPDATE` 권한으로 최신 버전의 `ACTIVE` Space를 보관한다. | `ARCHIVED`가 되고 보관·수정 시각 및 버전을 갱신한다. 이전 버전으로 변경을 등록하고 한 번 저장·커밋한다. |
| SP-05-02 | 최신 Space의 미존재·허용되지 않는 상태, 권한 부족, 컨텍스트 누락, 0 이하 버전을 재현한다. | 각각 미존재·상태·권한 오류 또는 공통 필드 오류를 반환하고 변경을 등록하지 않는다. |
| SP-05-03 | 오래된 버전을 보내거나 저장 시 동시성 충돌을 주입한다. | 모두 `PRECONDITION_FAILED`다. 저장 충돌은 롤백한다. |

## SP-06 Space 재활성화

[ReactivateSpaceUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/Reactivations/ReactivateSpaceUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-06-01 | `SPACE_UPDATE` 권한으로 최신 버전의 `ARCHIVED` Space를 재활성화한다. | `ACTIVE`로 전환하고 보관 시각을 지운다. 수정 시각과 버전을 갱신하고 이전 버전으로 변경을 등록해 저장·커밋한다. |
| SP-06-02 | 최신 Space의 미존재·허용되지 않는 상태, 권한 부족, 컨텍스트 누락, 0 이하 버전을 재현한다. | 각각 미존재·상태·권한 오류 또는 공통 필드 오류를 반환하고 변경을 등록하지 않는다. |
| SP-06-03 | 오래된 버전을 보내거나 저장 시 동시성 충돌을 주입한다. | 모두 `PRECONDITION_FAILED`다. 저장 충돌은 롤백한다. |

## SP-07 Space 삭제 요청

[RequestSpaceDeletionUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/DeletionRequests/RequestSpaceDeletionUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-07-01 | 현재 OWNER가 `ACTIVE`·`ARCHIVED` Space에 최신 버전으로 삭제를 요청한다. | `DELETING` 상태·접수 시각과 Space PublicId를 반환한다. 용량 카운터를 유지하고 이전 버전으로 변경을 등록해 저장·커밋한다. |
| SP-07-02 | 인가 후 소유권을 잃은 사용자나 OWNER가 아닌 SYSTEM_ADMIN이 요청한다. | `AUTH_SPACE_PERMISSION_DENIED`로 거부하고 변경을 등록하지 않는다. |
| SP-07-03 | 오래된 버전, 저장 중 충돌, 컨텍스트 누락 또는 0 이하 버전을 재현한다. | 버전 불일치·저장 충돌은 `PRECONDITION_FAILED`이고 저장 충돌은 롤백한다. 입력 오류는 해당 필드를 반환한다. |

## SP-08 Space quota 조회

[GetSpaceQuotaUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/QuotaReads/GetSpaceQuotaUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-08-01 | 인가된 Session으로 `ACTIVE`·`ARCHIVED`에서 조회하고, 인가된 MCP로도 조회한다. | 최신 스냅샷의 허용·사용·예약량 및 버전을 반환한다. 가용량은 허용량에서 사용량과 예약량을 뺀 값이며, 한도에 도달하면 0, 무제한이면 `null`이다. |
| SP-08-02 | Space 미존재, `DELETING`·`DELETED`, 읽기 실패, 컨텍스트 누락을 재현한다. | 미존재·상태·`DEPENDENCY_UNAVAILABLE` 또는 컨텍스트 오류를 반환한다. 변경을 등록하지 않는다. |

## SP-09 Space quota 변경

[ChangeSpaceQuotaUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/QuotaChanges/ChangeSpaceQuotaUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-09-01 | 현재 OWNER와 OWNER인 SYSTEM_ADMIN이 활성 Space의 한도를 변경한다. 유한 한도와 `null`을 확인한다. | 최신 소유권을 확인하고 용량 카운터를 보존하며 한도를 바꾼다. 이전 버전으로 변경을 등록하고 갱신된 quota와 버전을 반환한다. |
| SP-09-02 | 사용량과 예약량의 합 미만으로 한도를 낮추거나 같은 한도를 요청한다. | 점유량 미만은 `SPACE_QUOTA_TOO_SMALL`이다. 같은 한도는 최신 버전일 때 저장 없이 성공하고 버전·시각을 보존한다. |
| SP-09-03 | 이전 OWNER, OWNER가 아닌 SYSTEM_ADMIN, MCP, 미존재 또는 비활성 상태를 재현한다. | 권한·토큰 유형·미존재·상태 오류를 반환한다. MCP는 저장소 조회 전에 거부한다. |
| SP-09-04 | 실제 변경·무변경 요청에 오래된 버전을 보내거나 저장 충돌을 주입한다. 컨텍스트 누락, 0 이하 버전, 음수 quota도 확인한다. | 버전 불일치·충돌은 `PRECONDITION_FAILED`이며 저장 충돌은 롤백한다. 음수 quota는 `SPACE_QUOTA_INVALID` / `AllowedBytes`로 거부한다. |

## SP-10 Space 소유권 이전

[TransferSpaceOwnershipUseCaseTests](../../../tests/CloudSharp.Core.Tests/UseCases/Spaces/OwnershipTransfers/TransferSpaceOwnershipUseCaseTests.cs)

| 케이스 | 조건·실행 | 기대 결과 |
|---|---|---|
| SP-10-01 | 현재 OWNER가 같은 Space의 활성 멤버인 활성 사용자에게 이전한다. | 새 OWNER의 Membership·Role 연결 제거, 이전 OWNER의 ADMIN Membership 추가, Space 소유자 갱신을 한 트랜잭션에 등록한다. Space 버전과 결과의 접근 정보를 갱신하고 SYSTEM_ADMIN의 우회 접근을 보존한다. |
| SP-10-02 | 대상이 현재 OWNER이고 최신 버전 또는 오래된 버전을 보낸다. | 최신 버전은 저장 없이 현재 Space를 반환한다. 오래된 버전은 `PRECONDITION_FAILED`다. |
| SP-10-03 | MCP·이전 OWNER·OWNER가 아닌 SYSTEM_ADMIN, 비활성인 현재 OWNER, 미존재 Space 또는 허용하지 않는 최신 상태를 재현한다. | 토큰 유형·권한·계정 상태·Space 미존재·Space 상태에 해당하는 오류로 변경을 거부한다. |
| SP-10-04 | 대상 계정이 없거나 비활성이다. 같은 Space의 활성 Membership이 없거나 관리형 ADMIN Role이 없는 경우도 확인한다. | 부적격 대상은 `SPACE_TRANSFER_TARGET_INVALID`, ADMIN Role 누락은 `MEMBER_INVALID_ROLE`이다. 변경을 등록하지 않는다. |
| SP-10-05 | 오래된 버전, 변경 등록 실패, 저장 충돌을 재현한다. 컨텍스트 누락, 0 이하 버전, 잘못된 대상 PublicId도 전달한다. | 오래된 버전과 저장 충돌은 `PRECONDITION_FAILED`다. 등록·저장 실패는 롤백한다. 잘못된 대상 PublicId는 `SPACE_TRANSFER_TARGET_INVALID` / `NewOwnerUserPublicId`로 거부한다. |

[Space 도메인 설계](../../../.llm/domains/space.md) · [인가 계약](../../../contracts/permissions/README.md)
