# Space와 Mount 권한

Space 권한은 소유권과 활성 멤버십의 Role을 기준으로 계산한다. Mount 권한은 Discord 채널 권한과 같은 방식이다. Space Role 권한이 기본값이고, 각 Mount가 Role·사용자별 허용·거부로 그 Mount 안에서만 기본값을 덮어쓴다. HTTP 경로와 작업별 구현 상태는 각 유즈케이스 문서에서 확인할 수 있다. [Space 유즈케이스](../use-cases/space-use-cases.md) · [Mount 유즈케이스](../use-cases/mount-use-cases.md)

## Space 권한 코드와 기본 부여

Space 소유자와 활성 `ADMIN`은 현재 정의된 Space 권한 15개를 모두 가진다. 아래 표의 `MEMBER`와 `VIEWER` 열은 기본 Role을 만들 때 부여되는 권한이다. 실제 인가는 활성 멤버십에 연결된 같은 Space의 삭제되지 않은 Role들이 가진 권한을 합산해 판단한다. **Mount 범위** 열의 권한은 모든 Mount의 기본값이 되며 Mount마다 덮어쓸 수 있다. [Space Role 기본값](../../src/CloudSharp.Core/Domain/Memberships/SpaceRole.cs) · [Space 권한 정책](../../src/CloudSharp.Core/Policies/Authorization/SpacePermissionPolicy.cs)

| 코드 | 허용 작업 | 기본 `MEMBER` | 기본 `VIEWER` | Mount 범위 |
| --- | --- | :-: | :-: | :-: |
| `SPACE_VIEW` | Space 정보·quota·태그 조회 | 예 | 예 | — |
| `SPACE_UPDATE` | Space 정보 수정, Space 보관·재활성화 | — | — | — |
| `FILES_READ` | 파일·폴더·검색·휴지통 조회, 미리보기·썸네일 조회 | 예 | 예 | 예 |
| `FILES_WRITE` | 파일·폴더 생성·수정, 업로드와 휴지통 작업 생성 | 예 | — | 예 |
| `FILES_DOWNLOAD` | 파일 원문 다운로드용 권한 발급 | 예 | 예 | 예 |
| `SHARES_VIEW` | 공유 링크 목록·상세 조회 | 예 | — | — |
| `SHARES_MANAGE` | 공유 링크 생성·변경·폐기 | 예 | — | — |
| `TAGS_MANAGE` | 태그 생성·변경·삭제, 파일의 수동 태그 전체 교체 | 예 | — | — |
| `INVITES_MANAGE` | Space 초대 목록·관리 상세 조회, 초대 생성·폐기 | — | — | — |
| `MEMBERS_VIEW` | Space Role 목록과 멤버 목록·상세 조회 | 예 | — | — |
| `MEMBERS_MANAGE` | 멤버 Role 변경, 멤버 정지·활성화·제거 | — | — | — |
| `UPLOADS_MANAGE` | 같은 Space에서 다른 사용자의 업로드 조회·취소 | — | — | — |
| `OPERATIONS_MANAGE` | 같은 Space에서 다른 사용자가 요청한 비동기 작업 조회·취소 | — | — | — |
| `MOUNT_MANAGE` | Space에서는 Mount 개설, Mount에서는 설정·보관·재활성화와 덮어쓰기 관리 | — | — | 예 |
| `MOUNT_VIEW` | Mount 보기와 그 안의 모든 활동의 전제 | 예 | 예 | 예 |

quota 변경, Space 삭제, 소유권 이전에는 해당 Space의 소유권이 필요하다. 본인이 요청한 업로드·비동기 작업의 조회·취소는 Space 접근 자격, 요청자 관계, 작업 상태로 판단한다. [공개 권한 코드](../../src/CloudSharp.Core/Domain/Memberships/SpacePermissionCodes.cs) · [작업별 권한 계약](../../contracts/permissions/README.md)

## Mount 유효 권한 계산

1. 활성 계정의 Session을 사용하고 해당 Space의 소유자이거나 `ACTIVE` 멤버인지 확인한다. `SYSTEM_ADMIN`에도 같은 조건을 적용한다.
2. Space 소유자와 활성 `ADMIN`은 Discord의 ADMINISTRATOR처럼 덮어쓰기와 공개 범위에 관계없이 Mount 범위 권한을 모두 갖는다.
3. 그 외 멤버는 현재 Role 권한 합집합 중 Mount 범위 권한을 기본값으로 갖는다.
4. `PRIVATE` Mount는 기본값에서 `MOUNT_VIEW`를 뺀다(Discord 비공개 채널의 `@everyone` 보기 거부).
5. 현재 Role들의 덮어쓰기를 한 번에 적용한다. 거부 합집합을 빼고 허용 합집합을 더하므로 Role 간에는 허용이 이긴다.
6. 사용자 덮어쓰기를 마지막에 적용한다. 거부를 빼고 허용을 더하므로 Role 덮어쓰기보다 우선한다.
7. **암묵적 권한.** 최종 `MOUNT_VIEW`가 없으면 그 Mount의 권한을 모두 비운다. `FILES_READ`가 없으면 `FILES_DOWNLOAD`·`FILES_WRITE`도 뺀다.
8. Space가 보관 상태이면 `MOUNT_MANAGE`·`FILES_WRITE`를 뺀다. [Mount 권한 정책](../../src/CloudSharp.Core/Policies/Authorization/MountPermissionPolicy.cs)

| 상황 | 유효 권한 | 결과 |
|---|---|---|
| 기본 `VIEWER`, `PUBLIC` Mount, 덮어쓰기 없음 | `MOUNT_VIEW`, `FILES_READ`, `FILES_DOWNLOAD` | 목록·상세·파일 조회와 다운로드 |
| 기본 `MEMBER`, `PUBLIC` Mount, 사용자 거부 `FILES_WRITE` | `MOUNT_VIEW`, `FILES_READ`, `FILES_DOWNLOAD` | 이 Mount에서만 쓰기 불가 |
| 기본 `MEMBER`, `PRIVATE` Mount, 덮어쓰기 없음 | 없음 | 목록에서 제외, 단건 요청은 404 |
| 기본 `VIEWER`, `PRIVATE` Mount, Role 허용 `MOUNT_VIEW`·`FILES_WRITE` | `MOUNT_VIEW`, `FILES_READ`, `FILES_DOWNLOAD`, `FILES_WRITE` | 이 Mount에서만 편집 가능 |
| 기본 `MEMBER`, Role 허용 `MOUNT_MANAGE`, 사용자 거부 `MOUNT_VIEW` | 없음 | 관리 권한도 무효, 404 |
| 활성 `ADMIN`, 사용자 거부 `MOUNT_VIEW` | Mount 범위 권한 전체 | 덮어쓰기 우회 |

## endpoint에서 선언하는 인가

Mount 경로의 요구 권한은 endpoint가 `MountAuthorizationFilter`로 선언한다. 필터가 Space 접근 자격, Space 상태, Mount 소속, 최종 `MOUNT_VIEW`, Mount 상태, 요구 권한 순서로 평가하고, 성공하면 `AuthorizedMountContext`를 유즈케이스에 넘긴다. 유즈케이스는 권한을 다시 검사하지 않고 트랜잭션 안에서 최신 Space 상태, Mount 소속과 버전만 확인한다. [Mount 인가 필터](../../src/CloudSharp.Api/Endpoints/Mounts/MountAuthorizationFilter.cs) · [Mount 인가 정책](../../src/CloudSharp.Core/Policies/Authorization/MountAuthorizationPolicy.cs)

```csharp
mount.MapPatch("", UpdateMountAsync)
    .AddEndpointFilter(new MountAuthorizationFilter(
        [SpacePermission.MountManage], ActiveOnly, maskPermissionDenied: false));
```

| 결과 | HTTP |
|---|---|
| Space·Mount 미존재, 기본 접근 거부, 최종 `MOUNT_VIEW` 없음 | 404 `RESOURCE_NOT_FOUND` |
| 허용하지 않는 Space 상태 | Space filter와 같은 상태 오류(보관된 Space는 409 `SPACE_ARCHIVED`) |
| 허용하지 않는 Mount 상태 | 409 `REQUEST_CONFLICT` |
| `MOUNT_VIEW`는 있으나 요구 권한 부족 | 403 `PERMISSION_DENIED`, `maskPermissionDenied`이면 404 |

## Mount 작업별 요구 권한

| 작업 | 요구 권한 | 허용 Space 상태 |
|---|---|---|
| Mount 목록 | Space 접근 자격. 항목마다 유효 `MOUNT_VIEW`로 거른다. | `ACTIVE`, `ARCHIVED` |
| Mount 개설 | Space의 `MOUNT_MANAGE` (Space filter) | `ACTIVE` |
| Mount 상세 | `MOUNT_VIEW` | `ACTIVE`, `ARCHIVED` |
| Mount 설정 변경·보관·재활성화 | `MOUNT_MANAGE` | `ACTIVE` |
| 덮어쓰기 목록·유효 권한 미리보기 | `MOUNT_MANAGE` | `ACTIVE`, `ARCHIVED` |
| Role·사용자 덮어쓰기 교체·제거 | `MOUNT_MANAGE`와 바뀌는 권한 보유 | `ACTIVE` |
| 파일 조회·다운로드·쓰기 | `FILES_READ`·`FILES_DOWNLOAD`·`FILES_WRITE` | 조회·다운로드는 `ACTIVE`·`ARCHIVED`, 쓰기는 `ACTIVE` |

보관된 Mount도 상위 Space가 활성 상태이면 `MOUNT_MANAGE`로 설정을 변경하거나 재활성화할 수 있다. `DELETING`·`DELETED` Space는 404로 처리한다. [Mount 오류 계약](../use-cases/mount-use-cases.md#오류-계약)

## 덮어쓰기 변경 규칙

- 덮어쓰기는 대상마다 `allow`·`deny`를 담는다. 두 목록은 Mount 범위 권한만 담고 서로 겹칠 수 없다. 둘 다 비우면 덮어쓰기를 제거한다.
- 요청자는 허용·거부 상태가 바뀌는 권한을 해당 Mount에서 직접 가져야 한다(Discord 규칙). 바뀌지 않는 기존 항목은 요청자가 갖지 않아도 유지된다. 위반하면 403 `PERMISSION_DENIED`다.
- Mount 개설 시 초기 덮어쓰기는 요청자가 Space에서 가진 Mount 범위 권한 안에서만 지정할 수 있다.
- 권한 미리보기는 현재·제안 공개 범위에서 각각 유효 권한과 적용 근거(`ADMINISTRATOR`, `BASE`, `VISIBILITY`, `ROLE`, `USER`)를 반환한다. 근거는 원래 허용·거부 값이므로 `MOUNT_VIEW`를 잃은 이유도 확인할 수 있다. [덮어쓰기 변경](../../src/CloudSharp.Core/UseCases/Mounts/MountOverwriteMutationWorkflow.cs) · [권한 미리보기](../../src/CloudSharp.Core/UseCases/Mounts/PermissionPreviews/PreviewMountPermissionsUseCase.cs)

## 결과 필드

| 필드 | 의미 |
|---|---|
| `canCreateMount` | Space가 `ACTIVE`이고 요청자가 Space의 `MOUNT_MANAGE`를 가졌는지 나타낸다. Mount 목록이 비어 있어도 제공된다. |
| `myAccess.permissions` | 해당 Mount에서 요청자가 현재 행사할 수 있는 Mount 범위 권한 집합이다. |
| `myAccess.canManage` | 해당 Mount에 유효한 `MOUNT_MANAGE`가 있는지 나타낸다. |

[목록 유즈케이스](../../src/CloudSharp.Core/UseCases/Mounts/Listings/ListMountsUseCase.cs) · [Mount 접근 DTO](../../src/CloudSharp.Core/UseCases/Mounts/Dtos/MountAccessDto.cs)
