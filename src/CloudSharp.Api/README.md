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

## Idempotency-Key

### Call an opted-in endpoint

Generate one key per intended operation, for example a UUID. Send one `Idempotency-Key` value containing 16–128 printable ASCII characters.

```http
POST /api/v2/spaces HTTP/1.1
Host: api.example.test
Authorization: Bearer <sessionToken>
Content-Type: application/json
Content-Length: 21
Idempotency-Key: 019ab720-28bd-7691-80e5-3d2a5b52af61

{"name":"Team files"}
```

If the response is lost, retry with the same key, method, path, media type and inputs in the same caller scope. For authenticated routes, use a valid Session for the same caller. Registration uses the normalized email as its anonymous scope. Use a new key for a new operation. Completed records support recovery for 24 hours after completion. After that window, reconcile the original outcome before sending another creation request.

| Response | What to do |
| --- | --- |
| `400 REQUEST_VALIDATION_FAILED` with `INVALID_IDEMPOTENCY_KEY` | Send one key in the required format. |
| `409 IDEMPOTENCY_REQUEST_IN_PROGRESS` | Wait briefly and retry with the same key and inputs. |
| `409 IDEMPOTENCY_KEY_REUSED` | Restore the original inputs to retry that operation. Use a new key for a different operation. |
| `409 SECRET_RESPONSE_NOT_REPLAYABLE` | The operation completed. Use the public ID returned in the error and follow the endpoint's recovery instructions. |
| `503 DEPENDENCY_UNAVAILABLE` | Retry the same operation with the same key and inputs once the dependency recovers. |

Space creation replays the original `201` body, `Location` and `ETag`. Invite acceptance replays the original `200` body. Registration and invite creation return the original `userPublicId` or `invitePublicId` in a `409 SECRET_RESPONSE_NOT_REPLAYABLE` response. Log in after registration to get a Session. Use the invite ID to find or revoke the existing invite. See the endpoint contracts for details. [Registration](../../docs/api/auth-api.md#au-01-계정-등록) · [Space creation](../../docs/api/space-api.md#sp-02-space-생성) · [Invite creation and acceptance](../../docs/api/space-invite-api.md#생성수락의-멱등-키)

### Opt in a route

1. Build an `IdempotencyEndpointPolicy.Create<TTransactionResult>` with a stable operation ID and result version, a request scope, a fingerprint projection, a recovery projection, and an HTTP replay mapper. Scope the key to the method, canonical path and caller or anonymous partition. Project the bound input values that affect the operation. The shared helper includes the media type in the fingerprint. Hash capability tokens used in route scopes. [Invite policies](Endpoints/Invites/InviteIdempotency.cs) · [Scope and fingerprint helpers](Endpoints/Idempotency/IdempotencyHttp.cs)
2. Add `.RequireIdempotency(policy)` to the route after any endpoint filters that must run before replay, such as a resource authorization filter. The route declaration also exposes the required header in OpenAPI. The opt-in for invite acceptance looks like this. [Invite capability routes](Endpoints/Invites/InviteCapabilityEndpoints.cs)

   ```csharp
   endpoints.MapPost(InviteCapabilityProtectionMiddleware.AcceptRoute, AcceptInviteAsync)
       .RequireAuthorization(SessionAuthenticationDefaults.UserSessionOnlyPolicy)
       .RequireIdempotency(InviteIdempotency.AcceptPolicy);
   ```

3. Call the use case through its regular `ExecuteAsync`. Choose `TTransactionResult` to match the successful result of the single business `ITransactionExecutor.ExecuteAsync<TTransactionResult>` callback. Registration's callback returns a `User`, and the use case issues the Session after commit. The request-scoped context lets the shared executor save the successful business change and its recovery record in the same PostgreSQL transaction. [Registration use case](../CloudSharp.Core/UseCases/Auth/Registrations/RegistrationUseCase.cs) · [Transaction executor](../CloudSharp.Infrastructure/Persistence/Transactions/EfCoreTransactionExecutor.cs)
4. Project durable, non-secret data into `IdempotencyRecovery.Replayable<TRecovery>` and register `TRecovery` in the feature's DI registration with the policy's operation ID and result version. For a one-time secret response, return `IdempotencyRecovery.SecretNotReplayable` with the resource public ID. Its replay mapper returns the endpoint's `409` response and recovery ID. [Invite registration](DependencyInjection/InviteServiceCollectionExtensions.cs)

   ```csharp
   services.AddIdempotencyReplayType<AcceptSpaceInviteResultDto>(
       InviteIdempotency.AcceptPolicy.OperationId,
       InviteIdempotency.AcceptPolicy.ResultVersion);
   ```

Provision `idempotency_requests` in existing PostgreSQL databases before enabling opted-in routes. The EF Core configuration defines the schema, and the integration tests create it with `EnsureCreatedAsync` in their test database. [Table configuration](../CloudSharp.Infrastructure/Persistence/Configurations/IdempotencyRequestConfiguration.cs) · [Integration test setup](../../tests/CloudSharp.Api.IntegrationTests/README.md#테스트-기반)
