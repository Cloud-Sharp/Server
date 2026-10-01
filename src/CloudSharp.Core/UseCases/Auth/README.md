# Auth

Login, logout, session refresh use cases.

## Registration

`RegistrationUseCase` composes the existing `User` and `UserSession` domain models:

1. Validate the password (FluentValidation) — 8–128 characters, whitespace-only rejected, no character-class rules, no trimming or normalization.
2. Hash the password and call `User.Create` with `IClock.UtcNow`; email, user name, and display name validation/normalization stay in the domain.
3. Inside `ITransactionExecutor`: check normalized email and optional user name uniqueness (email conflict first), then `AddAsync` + `SaveAsync`. Rollback on any failure.
4. Outside the transaction: issue a session token, wrap its hash via `TokenHash`, call `UserSession.Issue` with a fresh clock reading (24h idle / 7d absolute), and store it via `ISessionStore`.
5. Return the plaintext token exactly once, only after session storage succeeds.

Failure handling:

- Persistence failure: no token is issued, nothing is stored.
- Session storage failure after commit: the account is kept and the use case fails. The client recovers by logging in; there is no compensating account deletion.
- Ports report known dependency outages as `DEPENDENCY_UNAVAILABLE`; unexpected exceptions and cancellation propagate.

The future `POST /api/v2/auth/registrations` endpoint requires an `Idempotency-Key` header (see `.llm/api/common-contract.md`). The registration response contains a secret token and is therefore not replayable — a retried request with the same key must return `SECRET_RESPONSE_NOT_REPLAYABLE`, and a client that lost the response logs in instead.

## Login

`LoginUseCase` authenticates an existing account and issues a login session. `LoginCommand.LoginId` supports email addresses only; user name login is out of scope for Production v1.

1. Validate the login id through `EmailAddress.Create` and the password structurally (FluentValidation): null, whitespace-only, and longer-than-128-character passwords are rejected. The registration minimum length is not reapplied and the password is never trimmed or normalized.
2. Invalid input collapses into the single canonical `AUTH_INVALID_CREDENTIALS` error so the response does not reveal which field failed.
3. Inside `ITransactionExecutor`: look up the account by normalized email via `IUserRepository.FindByNormalizedEmailAsync`. A missing account is a successful `null` result; known dependency outages are `DEPENDENCY_UNAVAILABLE`.
4. After the transaction completes: check `User.CanLogin()` (only `ACTIVE` accounts may log in) and verify the password with `IPasswordHasher.Verify`. Missing account, suspended or deleted user, and wrong password all return the same `AUTH_INVALID_CREDENTIALS` error with the same message. Login never modifies user state, versions, or timestamps and never calls `AddAsync`/`SaveAsync`.
5. Issue the session in a private method: `ITokenIssuer.Issue(TokenKind.Session)` → `TokenHash.Create` → `UserSession.Issue` → `ISessionStore.StoreAsync`, with a fresh `IClock.UtcNow` reading (24h idle / 7d absolute). A nonpositive internal user id from the repository is a contract violation and throws; cancellation is checked before a token is issued.
6. Return the plaintext token and user info via `LoginResultDto` only after session storage succeeds. Repeated logins issue separate sessions; the 10-session limit stays enforced atomically inside the `ISessionStore` adapter.

The future `POST /api/v2/auth/sessions` endpoint maps this use case. Rate limiting (`AUTH_RATE_LIMITED`), audit events, authentication timing protections, and per-request revalidation of user status and `SecurityVersion` are added when the HTTP flow is connected, not in the use case. Session issuance is currently duplicated between registration and login; extracting a shared session issuance service is deferred to a later refactor.