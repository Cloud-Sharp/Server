# Memberships

Space Membership aggregate. See .llm/domains/space-membership.md

## Permission

`SpacePermission`은 Space 단위의 개별 권한 enum이다. 각 값은 독립된 권한이며 권한 간 자동 포함 관계는
없다. 기본값 `0`은 유효한 권한이 아니고 `[Flags]` 조합도 만들지 않는다.

`SpacePermissionCodes.Get(SpacePermission)`이 안정된 코드 문자열을 반환한다. 코드 문자열이 외부
API와 영속 저장에서 사용하는 식별자이며, enum 정수값이나 `ToString()` 결과에는 의존하지 않는다.
정의되지 않은 enum 값은 `ArgumentOutOfRangeException`으로 거부한다.

| 권한 | 코드 |
|---|---|
| `SpaceView` | `SPACE_VIEW` |
| `SpaceUpdate` | `SPACE_UPDATE` |
| `FilesRead` | `FILES_READ` |
| `FilesWrite` | `FILES_WRITE` |
| `FilesDownload` | `FILES_DOWNLOAD` |
| `SharesView` | `SHARES_VIEW` |
| `SharesManage` | `SHARES_MANAGE` |
| `TagsManage` | `TAGS_MANAGE` |
| `InvitesManage` | `INVITES_MANAGE` |
| `MembersView` | `MEMBERS_VIEW` |
| `MembersManage` | `MEMBERS_MANAGE` |

- `SpaceUpdate`는 Space 정보 수정 권한이며 quota 변경, Space 삭제, 소유권 이전은 포함하지 않는다.
- 기본 Role 매핑은 아래 Space Role 절을 따른다. OWNER 권한 합성과 권한 평가는 후속 작업으로 이 폴더에 추가한다.
- 코드 문자열 파싱은 실제 소비자가 생기는 단계에서 추가한다.

## Space Role

`SpaceRole`은 Space별 Role 정의와 Permission 보관을 담당한다. 기본 Role은 `SpaceRoleSystemKey`
(`Admin=1`, `Member=2`, `Viewer=3`)로 식별하며 기본값 `0`과 OWNER는 존재하지 않는다. 신규 Role은
`CreateManaged(spaceId, systemKey, now)`로만 만들고 실패는 `Result`와 `ErrorCodes.SpaceRole`로
반환한다. 복원은 `Reconstitute`가 snapshot 전체와 Permission 집합을 받으며, 잘못된 snapshot은
예외로 거부한다.

| SystemKey | 이름 | Position | 기본 Permission |
|---|---|---|---|
| `Admin` | Admin | 2 | 정의된 모든 `SpacePermission` |
| `Member` | Member | 1 | VIEWER + `FilesWrite`, `SharesView`, `SharesManage`, `TagsManage`, `MembersView` |
| `Viewer` | Viewer | 0 | `SpaceView`, `FilesRead`, `FilesDownload` |

- `Permissions`는 변경할 수 없는 `IReadOnlySet<SpacePermission>`이며, 저장된 집합을 복사해 보관한다.
  빈 집합과 중복 입력을 허용하고 정의되지 않은 enum 값은 거부한다.
- ADMIN SystemKey Role은 저장된 Permission 연결이 비어 있거나 일부여도 `Enum.GetValues`로 구성한
  정의된 전체 권한을 반환한다. 새 Permission을 추가해도 기존 ADMIN의 DB 연결을 갱신할 필요가 없다.
- MEMBER·VIEWER는 생성 시 표의 기본 권한을 부여하고, 복원 시에는 저장된 집합을 그대로 사용한다.
- ADMIN 판정은 Role 내부의 SystemKey로만 처리한다. 호출자는 Role 이름 대신 `Permissions`를 사용한다.
- Space 존재 여부, Space별 이름·SystemKey 유일성, Membership 연결, 사용자 정의 Role CRUD, 권한 평가는
  후속 작업이다.
