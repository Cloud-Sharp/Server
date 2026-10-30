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
- 기본 Role 매핑, OWNER 권한 합성, 권한 평가는 후속 작업으로 이 폴더에 추가한다.
- 코드 문자열 파싱은 실제 소비자가 생기는 단계에서 추가한다.
