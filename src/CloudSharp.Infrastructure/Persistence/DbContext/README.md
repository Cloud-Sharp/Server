# DbContext

AppDbContext and EF Core model registration.

- `DbContextReadExtensions.ExecuteReadAsync` executes a query callback with the context and cancellation token and returns `Result<T>`. Only known PostgreSQL availability failures become `DEPENDENCY_UNAVAILABLE`; cancellation, invalid persisted data, SQL errors, and unexpected exceptions propagate.
- The read helper does not configure tracking, save changes, or manage transactions. Repositories define their query and materialize entities before converting them to domain snapshots.
