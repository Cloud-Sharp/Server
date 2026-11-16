# docs

Architectural decisions, security notes, verification reports.

## Project documentation

- [CloudSharp 프로젝트 종합 개요](project-overview.md) — 제품 목표, 범위, 현재 상태, 목표 아키텍처, 핵심 흐름, 운영 기준과 개발 로드맵
- [Auth 유즈케이스](auth-use-cases.md) — 인증·내 계정 API, Session 수명주기와 구현 현황
- [Auth 테스트 케이스](auth-test-cases.md) — User·Session 도메인 모델, Session 발급·인증, 등록·로그인·내 계정
- [Space 유즈케이스](space-use-cases.md) — Space·Role·Membership·Invite의 API별 행위자, 상태, 결과와 구현 경계
- [Space 테스트 케이스](space-test-cases.md) — Space 도메인 모델, 공통 인가 정책, SP-01~SP-10
- [Space Role 테스트 케이스](space-role-test-cases.md) — 관리형 Role, Permission 코드, RL-01
- [Space Membership 테스트 케이스](space-membership-test-cases.md) — Membership 상태와 Role 부여 이력, MB-01~MB-07
- [Space Invite 테스트 케이스](space-invite-test-cases.md) — 초대 도메인 모델, IN-01~IN-05와 HTTP 검증 시나리오
- [유즈케이스 로깅 데코레이터 설계](adr/use-case-logging-decorator.md) — 공통 실행 계약, 구조화 로그 정책, Transactional Outbox와의 책임 분리

