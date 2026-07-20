# Fakes

In-memory fakes for IObjectStore / IClock / ICurrentUser.

## Time

- `FakeClock` implements `CloudSharp.Core.Common.Time.IClock` for use in unit and integration tests.
- Construction takes an initial `DateTimeOffset` (normalized to UTC).
- `SetUtcNow(DateTimeOffset)` resets the current time (normalized to UTC).
- `Advance(TimeSpan)` moves the clock forward; negative durations throw `ArgumentOutOfRangeException`.
- Domain aggregates / value objects receive a `DateTimeOffset now` value, not `IClock` itself. UseCases take `IClock` and pass `now` down.