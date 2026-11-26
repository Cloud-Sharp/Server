# CloudSharp 문서

## 프로젝트 개요

- [CloudSharp 프로젝트 종합 개요](project-overview.md) — 제품 목표, 범위, 현재 상태, 목표 아키텍처, 핵심 흐름, 운영 기준과 개발 로드맵

## 유즈케이스

- [Auth 유즈케이스](use-cases/auth-use-cases.md) — 인증·내 계정 API, Session 수명주기와 구현 현황
- [Space 유즈케이스](use-cases/space-use-cases.md) — Space·Role·Membership·Invite의 API별 행위자, 상태, 결과와 구현 경계
- [Mount 유즈케이스](use-cases/mount-use-cases.md) — 목록·개설·설정·보관, Role·사용자별 권한과 미리보기

## 구현된 API

- [Idempotency-Key 사용법](../src/CloudSharp.Api/README.md#idempotency-key) — 요청 재시도와 새 엔드포인트 등록
- [Auth API](api/auth-api.md) — 계정 등록, 로그인
- [User API](api/user-api.md) — 내 프로필 조회, 비밀번호 변경

## API 설계

- [Space API](api/space-api.md) — Space 목록·생성·상세·수정, 보관·재활성화·삭제, quota와 소유권 이전
- [Space Invite API](api/space-invite-api.md) — 초대 목록·생성·폐기·관리 상세, 공개 조회와 수락
- [Space Membership API](api/space-membership-api.md) — 멤버 목록·상세, Role 교체, 정지·활성화·제거와 본인 탈퇴
- [Space Role API](api/space-role-api.md) — 관리형 Role 목록과 유효 Permission

## Core 테스트

- [Auth 테스트 케이스](tests/core/auth-test-cases.md) — User·Session 도메인 모델, Session 발급·인증, 등록·로그인·내 계정
- [Space 테스트 케이스](tests/core/space-test-cases.md) — Space 도메인 모델, 공통 인가 정책, SP-01~SP-10
- [Space Role 테스트 케이스](tests/core/space-role-test-cases.md) — 관리형 Role, Permission 코드, RL-01
- [Space Membership 테스트 케이스](tests/core/space-membership-test-cases.md) — Membership 상태와 Role 부여 이력, MB-01~MB-07
- [Space Invite 테스트 케이스](tests/core/space-invite-test-cases.md) — 초대 도메인 모델, IN-01~IN-05와 HTTP 검증 시나리오

## API 통합 테스트

- [Auth API 통합 테스트 케이스](tests/api-integration/auth-api-integration-test-cases.md) — 등록·로그인, 공통 HTTP 계약과 저장소 장애
- [User API 통합 테스트 케이스](tests/api-integration/user-api-integration-test-cases.md) — Session 인증, 프로필 조회, 비밀번호 변경

## Repository

- [Infrastructure Repository](repositories/persistence-repositories.md) — PostgreSQL의 공통 저장·트랜잭션 계약
- [User Repository](repositories/user-repositories.md)
- [Space Repository](repositories/space-repositories.md)
- [Space Role Repository](repositories/space-role-repositories.md)
- [Space Membership Repository](repositories/space-membership-repositories.md)
- [Space Invite Repository](repositories/space-invite-repositories.md)

## 설계·운영 문서

- [유즈케이스 로깅 데코레이터 설계](adr/use-case-logging-decorator.md) — 공통 실행 계약, 구조화 로그 정책, Transactional Outbox와의 책임 분리
- [아키텍처 결정 기록](adr/README.md)
- [제품·프로세스 결정 기록](decisions/README.md)
- [보안 문서](security/README.md)
- [출시 검증 문서](verification/README.md)
