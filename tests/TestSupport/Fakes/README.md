# Fakes

In-memory fakes for Core ports. No external services are used.

## Persistence

- `FakePersistenceSession` is shared by `FakeUserRepository`, `FakeUnitOfWork`, and `FakeTransactionExecutor` in write-use-case harnesses.
- Repository Add/Update calls stage changes. UnitOfWork saving validates and flushes them to a transaction overlay. Queries in that transaction can see flushed changes; committed state changes only when the executor commits.
- Rollback, exceptions, and cancellation discard only the current transaction's pending/flushed writes. Simulated external writer commits remain intact.
- Each query returns a domain copy. Update records the explicitly supplied original version; no query-selected save target is remembered.
- UnitOfWork records save calls and tokens and provides save failure/concurrency hooks. Repository hooks model staging and query failures separately.
- Registration obtains its generated id from a PublicId query after flushing. The input User is not modified to assign an id.

## Time

- `FakeClock` implements `CloudSharp.Core.Common.Time.IClock` for use in unit and integration tests.
- Construction takes an initial `DateTimeOffset` (normalized to UTC).
- `SetUtcNow(DateTimeOffset)` resets the current time (normalized to UTC).
- `Advance(TimeSpan)` moves the clock forward; negative durations throw `ArgumentOutOfRangeException`.
- Domain aggregates / value objects receive a `DateTimeOffset now` value, not `IClock` itself. UseCases take `IClock` and pass `now` down.
