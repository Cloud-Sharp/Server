# CloudSharp.Infrastructure

EF Core / Redis / Storage / Auth / Messaging adapters. References `CloudSharp.Core` only — must not reference API DTOs, Minimal API handlers, or HTTP models.

## Layout

- `DependencyInjection/` — `AddInfrastructure` composition root
- `Persistence/` — DbContext, Entities, Configurations, Migrations, Repositories, Transactions, Outbox, Seeds
- `Auth/` — Sessions, Passwords, Tokens, RateLimiting (Redis-backed)
- `Storage/` — Local FS object storage, path normalization, quarantine, reconciliation
- `Uploads/Tus/` — tus protocol adapter
- `Downloads/` — Grants, Streaming
- `Messaging/` — RedisStreams, OutboxRelay, DeadLetter
- `Processing/` — Metadata, Thumbnails, FileScanning, AiMetadata
- `Notifications/` — notification dispatch adapters
- `Mcp/` — MCP credential adapter
- `Observability/` — metrics/tracing instrumentation
- `Options/` — typed options with startup validation

## Rules

- Same concept has different types per boundary — `SpaceEntity` here, `SpaceSummaryDto` in Core, `SpaceResponse` in API. Never share.
- Migration applied via bundle or separate job — API startup must not auto-apply migrations.
- Path normalization and quarantine enforced on every write.

Read `.llm/database/<area>.md` and `.llm/domains/<aggregate>.md` before writing entities/repositories.