# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository status

This is the **CloudSharp Production v1 backend** — a fresh rebuild (`apps/backend-v2` in the design docs, but physically rooted at `E:\cloudsharp\server`). The .NET 10 solution was just scaffolded; the three projects under `src/` are empty skeletons. The authoritative design lives in `.llm/`. Read the relevant `.llm/` doc before writing any non-trivial code — the docs define aggregates, DB schema, auth model, API contracts, and pipelines; the code does not exist yet to read instead.

Do **not** copy from any prior `apps/backend` demo. The design docs explicitly forbid it; that code is reference-only for failure patterns.

## Build & run

```bash
dotnet build cloudsharp.sln                          # build all projects
dotnet run --project src/CloudSharp.Api              # run API (launchSettings.json)
dotnet test                                          # run all tests (no test projects yet)
dotnet test --filter "FullyQualifiedName~Foo"        # single test
```

Target framework is `net10.0` for every project. `Nullable` and `ImplicitUsings` are enabled globally.

## Project layout (intended — see `.llm/백엔드 프로덕션 디렉토리 구조 설계.md` for full target)

```
src/
  CloudSharp.Api/         → ASP.NET Core Minimal API host (adapter layer)
  CloudSharp.Core/        → Domain + UseCases + Abstractions + Policies (no infra deps)
  CloudSharp.Infra/       → EF Core / Redis / Storage / Auth / Messaging adapters
tests/                    → not yet created; planned split: Core.Tests, Infra.Tests,
                            Api.IntegrationTests, Worker.IntegrationTests, Architecture.Tests
contracts/, deploy/, ops/, scripts/   → not yet created
```

### Dependency rules (enforced by future Architecture.Tests)

| Project | May reference | Must not reference |
|---|---|---|
| `CloudSharp.Core` | nothing / pure libs | ASP.NET Core, EF Core, Redis, FS, env vars |
| `CloudSharp.Infra` | `CloudSharp.Core` | API DTOs, Minimal API handlers, HTTP models |
| `CloudSharp.Api` | `CloudSharp.Core`, `CloudSharp.Infra` (DI only) | repositories directly, business rules |

Layering rule: **per-boundary models, never shared.** Same concept has different types per layer — `CreateSpaceRequest` (API) / `CreateSpaceCommand` (Core) / `SpaceEntity` (Infra). Do not let one type leak across boundaries.

## Authoritative design docs (`.llm/`)

Read `.llm/index.md` first — it has a "작업별 진입 지점" table mapping each task to the 2-3 docs to read first.

Key entry points:
- **기획서** (`.llm/백엔드 프로덕션 신규 구축 기획서.md`) — single source of truth for product scope, launch gates, architecture policy. Wins on conflict.
- **디렉토리 구조** (`.llm/백엔드 프로덕션 디렉토리 구조 설계.md`) — target project/test/deploy layout.
- **`domains/`** — one file per aggregate: state-transition tables, invariants, behavior, error codes. Read before writing any domain type.
- **`database/`** — PostgreSQL table design, common physical policy, ENUM, Redis models.
- **`auth/`** — opaque Bearer token, session/MCP auth, SystemRole × SpacePermission matrix, ASP.NET filter boundaries.
- **`api/`** — `/api/v2`, `/public/v2`, `/internal/v2` contracts; common contract (auth, error, cursor, ETag, idempotency).
- **`pipeline/`** — upload (tus), download (8-step), trash flows with transaction boundaries.
- **`log/`** — structured logging standard fields and per-layer responsibilities.

Doc-priority hierarchy when conflicts arise: 기획서 → 설계 목록 → 영역별 상세. OpenAPI YAML defers to `api/` docs.

## Cross-cutting conventions

These are reinforced by the skills in `.claude/skills/` and the design docs:

- **Domain models** (`cloudsharp-domain-model` skill): behavior-centric, not anemic. Return `FluentResults.Result`/`Result<T>` — never `throw` for business failures. Value validation via `FluentValidation`; state-transition validation via in-method `if`. `Create` vs `Reconstitute` separated. All setters `private set`. Time injected as `DateTimeOffset now`. Method names are behaviors (`Rename`, `MarkDeleted`), not `ChangeStatus`. Domains do not touch DB/auth/transactions — that's UseCase scope.
- **Minimal API endpoints** (`minimal-api-endpoint` skill): endpoints are adapter layer only — HTTP ↔ command/query mapping, no business logic. .NET 10 built-in `AddValidation()` + DataAnnotations on request records; no manual validation in handlers. Request/Response DTOs in separate `Requests/`/`Responses/` folders, never inline. `ICurrentUser` for identity; never take user id from body. `MapGroup` + `RequireAuthorization` at group level. Result→HTTP via shared `ToHttpResult` mapper.
- **Unit tests** (`aspnet-unit-testing` skill): NUnit + Bogus + FluentResults. Name pattern `MethodName_ShouldExpectedResult`; conditions via `[TestCase]`/`[TestCaseSource]`, not in name. Success and failure as separate methods. `Assert.That` only. Seeded Bogus. No DB/Redis/FS/network — those go in `*.IntegrationTests`.
- **Integration tests** (`api-integration-tests` skill): real PostgreSQL/Redis + ASP.NET Core test host via Testcontainers. Verify API contract, EF persistence, Redis integration. Docker is required; never skip-pass when Docker is missing.

## Identifier exposure rule

Internal `long Id` is never exposed in API responses or external events. Use `PublicId` (UUIDv7) as the external identifier. Token/password/cookie/raw-body never appear in examples, logs, or docs.

## ENUM policy

Adding ENUM values is allowed. Renaming or removing values is forbidden without a compatibility migration.

## Skills available in this repo

When the user's request matches one of these, the harness invokes the skill — follow its instructions:

- `cloudsharp-domain-model` — writing/reviewing Core domain types (Entity/Aggregate/Value Object/state transitions)
- `minimal-api-endpoint` — writing/reviewing Minimal API endpoints
- `aspnet-unit-testing` — writing NUnit unit tests for Core (domain/usecase/validator)
- `api-integration-tests` — writing integration tests in `CloudSharp.Api.IntegrationTests`

Built-in: `init`, `review`, `security-review`, `simplify`, `loop`, `update-config`, `keybindings-help`, `fewer-permission-prompts`, `claude-api`.