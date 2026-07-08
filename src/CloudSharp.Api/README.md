# CloudSharp.Api

ASP.NET Core Minimal API host — adapter layer only. References `CloudSharp.Core` for use cases and `CloudSharp.Infrastructure` for DI composition. Must not call repositories directly or duplicate business rules.

## Layout

- `Program.cs` — entry point
- `DependencyInjection/` — API-side service registration
- `Hosting/` — `ApiHost`, `WorkerHost`, `MigrationHost` execution modes
- `Endpoints/{Feature}/` — Minimal API endpoints, one folder per feature
- `Auth/` — Authentication, Authorization, CurrentUser, EndpointFilters
- `Contracts/` — Requests, Responses, ProblemDetails, Mapping (per-boundary DTOs)
- `OpenApi/` — OpenAPI document configuration
- `Middleware/` — request pipeline middleware
- `Realtime/` — SSE / WebSocket endpoints
- `Workers/` — hosted services for worker execution mode
- `Observability/` — API-side logging/telemetry setup
- `Configuration/` — typed configuration bindings

## Endpoint per-feature layout

```
Endpoints/{Feature}/
├── {Feature}Endpoints.cs
├── Requests/
├── Responses/
├── Validators/
├── Filters/
└── Mapping/
```

## Rules

- Endpoints map HTTP ↔ command/query; no business logic
- .NET 10 built-in `AddValidation()` + DataAnnotations on request records; no manual validation in handlers
- `ICurrentUser` for identity; never take user id from body
- `MapGroup` + `RequireAuthorization` at group level
- Result→HTTP via shared `ToHttpResult` mapper
- Internal `long Id` never appears in responses; `PublicId` (UUIDv7) only

Read `.llm/api/<area>.md` and `.llm/auth/aspnet-filters.md` before writing endpoints.