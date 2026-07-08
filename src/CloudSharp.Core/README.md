# CloudSharp.Core

Domain + UseCases + Abstractions + Policies. No infra deps — must not reference ASP.NET Core, EF Core, Redis, filesystem, or env vars.

## Layout

- `Common/` — cross-cutting primitives (Errors, Results, Time, Identifiers)
- `Domain/` — aggregates and value objects, behavior-centric, `FluentResults.Result` returns
- `Abstractions/` — ports for infra (Auth, Persistence, Storage, Messaging, Security, Processing, Observability, Transactions)
- `UseCases/` — application flow per feature (Commands/Queries/Validators/Results/Dtos/Extensions)
- `Policies/` — Authorization, Quotas, Retention, RateLimits, FileSafety

Per-feature subdirectory naming is shared with `Endpoints/` and `tests/` (`Auth`, `Spaces`, `Files`, `Uploads`, …).

## Rules (enforced by Architecture.Tests)

- Domain methods return `Result`/`Result<T>`; never throw for business failures
- `Create` vs `Reconstitute` separated; all setters `private set`
- Time injected as `DateTimeOffset now`
- Method names are behaviors (`Rename`, `MarkDeleted`), not `ChangeStatus`
- Internal `long Id` never leaves Core; `PublicId` (UUIDv7) is the external handle

Read `.llm/domains/<aggregate>.md` before writing any domain type.