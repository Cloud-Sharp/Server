# Users

User profile / admin use cases.

## GetProfileUseCase

Serves the current-user profile query behind `GET /api/v2/me`.

- **Trusted identity boundary**: `GetProfileQuery.UserId` comes only from the verified
  Session context at the endpoint; clients cannot choose it. Authentication owns token
  type checks, expiration, SecurityVersion validation, and session activity renewal, so
  the use case performs no Redis session lookup and rechecks the loaded user's active
  state (`User.CanLogin()`) instead.
- **Execution**: reject nonpositive `UserId` with `AUTH_SESSION_INVALID` without storage
  access → load the user with `IUserRepository.FindByIdAsync` inside `ITransactionExecutor`
  (repository methods are transaction-scoped by contract) → recheck active state → map
  profile fields and `Version` from the same aggregate. Read-only: no `AddAsync`,
  `SaveAsync`, or domain mutation.
- **Failure policy**: missing, suspended, and deleted accounts all return
  `AUTH_SESSION_INVALID` without distinguishing existence or state. Repository and
  transaction failures (e.g. `DEPENDENCY_UNAVAILABLE`) are preserved, and cancellation
  and unexpected exceptions propagate.
- **Response mapping (future endpoint)**: `GetProfileResultDto.User` is the resource body
  (PublicId, email, userName, displayName, systemRole, status, creation/update timestamps —
  never the internal Id, password hash, or SecurityVersion) and `Version` becomes the ETag
  `"v{Version}"` required by `If-Match` on `PATCH /api/v2/me`. Core does not construct
  HTTP headers.