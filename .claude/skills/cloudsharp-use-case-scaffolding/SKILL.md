---
name: cloudsharp-use-case-scaffolding
description: >
  CloudSharp 백엔드에서 유즈케이스 인터페이스나 골격을 추가·변경할 때 사용한다.
  기존 IUseCase 계약, Command·Query·결과 DTO, FluentValidation 호출과
  도메인별 API DI 등록을 다룬다. 업무 로직·영속성·HTTP 엔드포인트 구현은 별도 범위다.
---

# CloudSharp 유즈케이스 골격 작성

본문의 경로는 저장소 루트 기준이다. 요청에서 지정한 작업 범위를 우선한다. 새 유즈케이스 골격을 요청하면 입력·결과 계약, 실행 클래스, 검증 호출, 도메인별 DI 등록까지 한 흐름으로 작성한다.

## 계약과 기준 코드

- 대상 작업의 입력, 결과, 오류 코드, 인증 주체를 기존 API·도메인 설계와 호출 코드에서 확인한다. 로컬 `.llm/index.md`와 관련 상세 설계가 있으면 함께 읽는다. 검증 한도, 정렬 허용 목록, 기본값은 확인된 계약에 맞춘다.
- 실행 계약은 `src/CloudSharp.Core/UseCases/IUseCase.cs`의 `IUseCase<TInput, TOutput>`을 재사용한다. 골격과 검증의 기준은 `src/CloudSharp.Core/UseCases/Spaces/Creations/`의 `CreateSpaceCommand`, `CreateSpaceUseCase`다.
- 동작이 구현된 사례가 필요하면 `src/CloudSharp.Core/UseCases/Auth/Registrations/RegistrationUseCase.cs`의 검증 진입점과 오류 변환을 확인한다.

## 작성 순서

1. **입력과 결과.** 변경 작업은 `Command`, 조회는 `Query`를 기능별 폴더에 둔다. 결과 `*Dto`는 해당 Core 기능의 `Dtos` 네임스페이스에 둔다. Core 입력에는 API Request·Response나 HTTP 헤더 대신 인증·인가 경계가 만든 컨텍스트와 파싱된 값을 전달한다. 부분 수정은 기존 계약의 생략과 명시적 `null` 의미를 보존한다.
2. **실행 클래스.** 같은 기능 폴더에 `public sealed` 유즈케이스 클래스를 두고 `IUseCase<TInput, TOutput>`을 구현한다. 진입점은 `Task<Result<TOutput>> ExecuteAsync(TInput input, CancellationToken cancellationToken = default)`다. 사용자가 골격만 요청한 경우 현재 Space처럼 유효한 입력은 `NotImplementedException`에 도달하도록 둔다.
3. **입력 검증.** Command·Query 옆에 `internal sealed AbstractValidator<TInput>`를 두고 `ExecuteAsync` 진입점에서 호출한다. 입력 객체 자체의 `null`은 `ArgumentNullException.ThrowIfNull`로 처리한다. 필드 실패는 공용 `ToFailureResult<TOutput>()`로 변환해 `ErrorCode`와 `PropertyName`을 보존한다. 기존 도메인 validator와 `ErrorCodes`를 우선 재사용한다. 권한, 상태 전이, 저장된 버전 일치처럼 저장 상태가 필요한 판단은 업무 로직에 둔다.
4. **API DI 등록.** `src/CloudSharp.Api/DependencyInjection/`의 해당 도메인 등록 파일에 추가한다. 로깅 적용 대상은 `UseCaseServiceCollectionExtensions.AddLoggedUseCase<TInput, TOutput, TUseCase>()`를 사용한다. 이 헬퍼가 구체 타입과 닫힌 인터페이스를 scoped로 등록하고 한 번 감싼다. 도메인 등록 파일은 `AddCloudSharpApplication`에서 조합한다.
5. **확인.** 변경된 프로젝트를 빌드하고 DI를 수정했다면 기존 API·아키텍처 검증을 실행한다. 검증 실패가 업무 처리 전에 반환되는지 확인한다. 골격을 전달할 때는 유효 입력의 구현 상태와 라우트 연결 상태를 구분해 보고한다.

이 스킬의 Codex·Claude 프로젝트 사본은 각각 `.agents/skills/cloudsharp-use-case-scaffolding/SKILL.md`, `.claude/skills/cloudsharp-use-case-scaffolding/SKILL.md`에 동일한 내용으로 유지한다.
