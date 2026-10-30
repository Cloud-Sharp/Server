# Spaces

`Space`는 독립된 파일공간의 단일 소유자, 수명주기와 용량 집계를 관리한다.
제품 개념은 `.llm/기획/space.md`, 상세 설계는 `.llm/domains/space.md`를 따른다.

## 공개 행위

- `Create` / `Reconstitute`: 신규 생성과 영속 snapshot 복원을 구분한다.
- `UpdateMetadata` / `ChangeQuota`: 이름·설명과 허용 용량을 변경한다.
- `ReserveStorage` / `ConsumeReservation`: 업로드 용량을 예약하고 확정 사용량으로 전환한다.
- `ReleaseReservation` / `ReleaseUsedStorage`: 취소·만료 예약과 영구 삭제된 파일의 집계를 정리한다.
- `Archive` / `Reactivate`: 읽기 전용 보관과 재활성화를 수행한다.
- `BeginDelete` / `CompleteDelete`: 신규 쓰기를 중단하고 리소스 정리가 끝난 뒤 삭제를 완료한다.
- `TransferOwnership`: 단일 소유자를 교체한다. 같은 소유자는 멱등 성공한다.

비즈니스 실패는 `Result`와 `ErrorCodes.Space`로 반환한다. 실제 변경만 `Version`을 증가시키며,
시간은 호출자가 UTC `DateTimeOffset now`로 전달한다. 이전 `UpdatedAt`보다 과거인 변경은 거부한다.
복원 시에는 저장된 이름·설명·식별자·버전·시각을 그대로 보존하고 잘못된 snapshot에 예외를 던진다.

## 상태와 정리

일반 변경과 신규 예약·파일 확정은 ACTIVE에만 허용한다. 예약 해제와 파일 영구 삭제의 사용량 차감은
ARCHIVED와 DELETING에서도 허용해 진행 중이던 작업을 정리할 수 있다. DELETED는 모든 변경을 거부한다.
용량 증감은 양수만 받으며, 무제한 quota에서도 `long` 집계 범위를 넘길 수 없다.

재활성화나 삭제 시작 시 `ArchivedAt`을 비운다. 삭제 시작 시 용량 집계를 유지하고 `DeletingAt`을 기록한다.
사용·예약 용량이 모두 0이어야 삭제를 완료할 수 있으며, 완료 후에는 `DeletingAt`과 `DeletedAt`을 함께 보존한다.

## UseCase의 책임

- 생성할 소유자가 활성 User인지 확인하고 기본 Role·루트 폴더를 함께 생성한다.
- 요청자의 현재 권한과 Space 접근 범위를 검증한다.
- 소유권 이전 대상의 활성 계정과 같은 Space의 활성 Membership을 확인한다.
- 소유권 이전, 새 소유자 Membership 제거, 이전 소유자 ADMIN 부여를 하나의 트랜잭션으로 처리한다.
- 예약 해제·소비의 중복을 FileReservation 상태로 검증하고 예약 상태와 용량 집계를 원자적으로 저장한다.
- 조회 시 관찰한 버전으로 충돌을 검사한다. 도메인 객체만으로 동시 예약 경쟁을 제어하지 않는다.
- Space 삭제 완료 전 소속 리소스와 물리 파일의 정리가 끝났는지 확인한다.
