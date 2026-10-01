# Auth

계정 인증 use case 모음. 등록·로그인·비밀번호 변경을 제공하고 로그아웃과 세션 갱신은 후속 작업이다.

## Registration

`RegistrationUseCase` composes the existing `User` and `UserSession` domain models:

1. Validate the password (FluentValidation) — 8–128 characters, whitespace-only rejected, no character-class rules, no trimming or normalization.
2. Hash the password and call `User.Create` with `IClock.UtcNow`; email, user name, and display name validation/normalization stay in the domain.
3. Inside `ITransactionExecutor`: check normalized email and optional user name uniqueness (email conflict first), then `AddAsync` + `SaveAsync`. Rollback on any failure.
4. Outside the transaction: call the shared `SessionIssuanceService` to issue and store a session with a fresh clock reading (24h idle / 7d absolute).
5. Return the plaintext token exactly once, only after session storage succeeds.

Failure handling:

- Persistence failure: no token is issued, nothing is stored.
- Session storage failure after commit: the account is kept and the use case fails. The client recovers by logging in; there is no compensating account deletion.
- Cancellation observed after commit and before token issuance: no token is issued and the committed account is kept. Cancellation during storage propagates without undoing the account commit.
- Ports report known dependency outages as `DEPENDENCY_UNAVAILABLE`; unexpected exceptions and cancellation propagate.

The future `POST /api/v2/auth/registrations` endpoint requires an `Idempotency-Key` header (see `.llm/api/common-contract.md`). The registration response contains a secret token and is therefore not replayable — a retried request with the same key must return `SECRET_RESPONSE_NOT_REPLAYABLE`, and a client that lost the response logs in instead.

## Login

`LoginUseCase` authenticates an existing account and issues a login session. `LoginCommand.LoginId` supports email addresses only; user name login is out of scope for Production v1.

1. Validate the login id through `EmailAddress.Create` and the password structurally (FluentValidation): null, whitespace-only, and longer-than-128-character passwords are rejected. The registration minimum length is not reapplied and the password is never trimmed or normalized.
2. Invalid input collapses into the single canonical `AUTH_INVALID_CREDENTIALS` error so the response does not reveal which field failed.
3. Inside `ITransactionExecutor`: look up the account by normalized email via `IUserRepository.FindByNormalizedEmailAsync`. A missing account is a successful `null` result; known dependency outages are `DEPENDENCY_UNAVAILABLE`.
4. After the transaction completes: check `User.CanLogin()` (only `ACTIVE` accounts may log in) and verify the password with `IPasswordHasher.Verify`. Missing account, suspended or deleted user, and wrong password all return the same `AUTH_INVALID_CREDENTIALS` error with the same message. Login never modifies user state, versions, or timestamps and never calls `AddAsync`/`SaveAsync`.
5. Outside the transaction, call the shared `SessionIssuanceService` to issue and store a session with a fresh clock reading (24h idle / 7d absolute).
6. Return the plaintext token and user info via `LoginResultDto` only after session storage succeeds. Repeated logins issue separate sessions; the 10-session limit stays enforced atomically inside the `ISessionStore` adapter.

The future `POST /api/v2/auth/sessions` endpoint maps this use case. Rate limiting (`AUTH_RATE_LIMITED`), audit events, authentication timing protections, and per-request revalidation of user status and `SecurityVersion` are added when the HTTP flow is connected, not in the use case.

## Shared Session Issuance

`SessionIssuanceService` composes `ITokenIssuer`, `ISessionStore`, and `IClock` for registration and login after their transactions complete:

1. Reject a nonpositive internal user id as a repository contract violation (`InvalidOperationException`), then check cancellation before issuing a token.
2. Issue via `ITokenIssuer.Issue(TokenKind.Session)`, validate the hash via `TokenHash.Create`, and call `UserSession.Issue` with the user's identity, security version, and a fresh `IClock.UtcNow` reading.
3. Store via `ISessionStore.StoreAsync`, forwarding the request cancellation token. Only the hash is stored.
4. Return `SessionIssuanceResult` with `AccessToken`, `IdleExpiresAt`, and `AbsoluteExpiresAt` only after storage succeeds. Its `ToString` redacts the token. Hash validation and storage errors are forwarded; unexpected exceptions and cancellation propagate.

Each use case retains its existing response DTO, `Bearer` token type, and user mapping. The service does not authenticate users or open a transaction.

## Password Change

`PasswordChangeUseCase` changes the password of the authenticated account and keeps only the current session. The command carries identity fields (user id, session id, token hash) that the future `PUT /api/v2/me/password` endpoint must fill from the authenticated Session context, never from the request body. The response returns only the new user `Version`, which the endpoint uses as the ETag of its 204 response.

1. Validate the passwords (FluentValidation): the new password follows the registration policy (8–128 characters, whitespace-only rejected), the current password follows login's structural rules (null, whitespace-only, >128 rejected). Neither is trimmed or normalized, the new password may equal the current one, and no password-reuse policy is applied. Token hash validation stays in `TokenHash`.
2. Look up the current session by token hash via `ISessionStore.FindByTokenHashAsync` and reject missing sessions and user id/session id mismatches with `AUTH_SESSION_INVALID`.
3. Inside `ITransactionExecutor`: load the user with the tracked `IUserRepository.FindByIdAsync`, revalidate the session with `UserSession.Validate` (account status, security version, expiration), reject a stale `ExpectedVersion` with `PRECONDITION_FAILED`, verify the current password with `IPasswordHasher.Verify` (`USER_PASSWORD_MISMATCH`), then call `User.ChangePasswordHash` and `SaveAsync` in the same transaction. `SaveAsync` enforces optimistic concurrency against the version captured at lookup and returns `PRECONDITION_FAILED` on conflict.
4. After commit: `ISessionStore.FinalizePasswordChangeAsync` performs one atomic operation that updates the current session's security version and removes all other sessions of the user. The current session's token, session id, issuance, activity, and expiration times are preserved. A session deleted or expired before finalization is never recreated — the operation reports `false`, removes the remaining sessions, and the use case still succeeds. The cleanup runs under its own 10-second deadline, independent of request cancellation.
5. Finalization failure (dependency outage or uncertain outcome): the changed password is kept, the use case attempts to delete the current session, and returns `DEPENDENCY_UNAVAILABLE`. If deletion also fails or its outcome is uncertain, a separate transaction reloads the user and — while the security version still equals this change's version — calls `RevokeAllSessions` to bump it again; the compensation retries with a fresh lookup up to three times on concurrency conflicts and leaves the version unchanged if it already moved on.

A post-commit failure therefore still leaves the password changed: the client recovers by logging in again with the new password. Session invalidation is best-effort — if both the Redis deletion and the database compensation fail, a current session whose security version was already upgraded may keep authenticating until the compensation or manual cleanup succeeds. That residual risk is accepted and documented here, not hidden: the safe failure direction is invalidating all sessions, but no invalidation can be guaranteed once both stores are unavailable. The Redis/PostgreSQL adapters and the HTTP endpoint are follow-up work; their atomicity and outage recovery must then be verified with integration tests.
