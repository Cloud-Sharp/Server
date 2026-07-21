# TestSupport

Core-specific test helpers (Bogus seeds for domain fixtures).

## Time

- Use `CloudSharp.TestSupport.Fakes.FakeClock` for any test that needs a controlled current time.
- For `SystemClock` tests, inject a small in-test `TimeProvider` subclass that returns a fixed `DateTimeOffset`; do not rely on the wall clock.
- Pass `DateTimeOffset now` values into domain aggregates/value objects; do not pass `IClock` into domain types.