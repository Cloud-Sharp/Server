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