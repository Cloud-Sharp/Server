# CloudSharp Production v1 Backend

Production v1 backend rebuild. Authoritative design lives in [`.llm/`](.llm/index.md) — read the relevant doc before writing non-trivial code.

전체 제품 목표, 범위, 현재 구현 상태와 목표 아키텍처는 [`docs/project-overview.md`](docs/project-overview.md)에서 확인할 수 있습니다.

## Build & run

```bash
dotnet build cloudsharp.sln                          # build all projects
dotnet run --project src/CloudSharp.Api              # run API (launchSettings.json)
dotnet test                                          # run all tests
dotnet test --filter "FullyQualifiedName~Foo"        # single test
```

## Layout

| Path | Purpose |
|---|---|
| `src/CloudSharp.Api` | ASP.NET Core Minimal API host (adapter layer) |
| `src/CloudSharp.Core` | Domain + UseCases + Abstractions + Policies (no infra deps) |
| `src/CloudSharp.Infrastructure` | EF Core / Redis / Storage / Auth / Messaging adapters |
| `tests/` | Core.Tests, Infrastructure.Tests, Api.IntegrationTests, Worker.IntegrationTests, Architecture.Tests, TestSupport |
| `contracts/` | OpenAPI, event schemas, error catalog, permission matrix — implementation baseline, not output |
| `deploy/` | Docker Compose, Dockerfiles, nginx, postgres, redis, systemd units |
| `ops/` | Runbooks, backup/migration policy, dashboards, alerts |
| `scripts/` | dev / ci / release / maintenance PowerShell scripts |
| `docs/` | ADRs, decision records, security notes, verification reports |
| `benchmarks/` | upload-download, metadata-api, worker benchmarks |

## Dependency rules (enforced by Architecture.Tests)

| Project | May reference | Must not reference |
|---|---|---|
| `CloudSharp.Core` | nothing / pure libs | ASP.NET Core, EF Core, Redis, FS, env vars |
| `CloudSharp.Infrastructure` | `CloudSharp.Core` | API DTOs, Minimal API handlers, HTTP models |
| `CloudSharp.Api` | `CloudSharp.Core`, `CloudSharp.Infrastructure` (DI only) | repositories directly, business rules |

Per-boundary models, never shared. Same concept has different types per layer — `CreateSpaceRequest` (API) / `CreateSpaceCommand` (Core) / `SpaceEntity` (Infra).

## Pre-deploy verification

```bash
dotnet test                                          # all unit + integration + architecture tests
scripts/ci/verify.ps1                                # build + test + OpenAPI lint + migration check
scripts/release/smoke-test.ps1                       # post-deploy smoke test
```

See [`ops/runbooks/deploy.md`](ops/runbooks/deploy.md) for the full deploy procedure.
