# Persistence

Repository ports per aggregate. EF Core implementations live in Infrastructure.

`IUserRepository` (User aggregate):

- `ExistsByNormalizedEmailAsync` / `ExistsByNormalizedUserNameAsync` take the domain-normalized (trim + uppercase) comparison value.
- `AddAsync` only tracks a new user; `SaveAsync` persists tracked changes via `IUnitOfWork` and returns the user reconstituted through `User.Reconstitute` with the DB-generated positive id.
- `SaveAsync` never commits the caller's transaction; the adapter translates email/user name unique-constraint violations into `USER_EMAIL_CONFLICT` / `USER_NAME_CONFLICT` so persistence-time conflicts match the preliminary checks.
- Known dependency outages are returned as `DEPENDENCY_UNAVAILABLE` instead of thrown.