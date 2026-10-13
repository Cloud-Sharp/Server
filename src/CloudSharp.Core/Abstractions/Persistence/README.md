# Persistence

Repository ports per aggregate. EF Core implementations live in Infrastructure.

`IUserRepository` (User aggregate):

- `ExistsByNormalizedEmailAsync` / `ExistsByNormalizedUserNameAsync` take the domain-normalized (trim + uppercase) comparison value.
- Queries return independent domain snapshots. Missing accounts return `Ok(null)`; a domain mutation never implicitly stages an EF update.
- `AddAsync(user)` stages an insert. `UpdateAsync(user, expectedVersion)` stages an update with the version observed before domain mutation, including a version check for unchanged updates. Neither method saves or commits.
- Repository implementations hold no mutable save-target or transaction state between calls. EF change tracking belongs to the shared scoped DbContext.
- UseCases explicitly call `IUnitOfWork.SaveChangesAsync` inside `ITransactionExecutor`. The UnitOfWork saves all staged changes; the executor commits or rolls back and clears tracking when each transaction attempt ends.
- Registration reloads the saved user through `FindByPublicIdAsync` after saving and before commit to obtain its generated positive internal id. `AddAsync` does not mutate the input domain object.
- The Infrastructure UnitOfWork translates email/user name unique violations into `USER_EMAIL_CONFLICT` / `USER_NAME_CONFLICT`, concurrency into `PRECONDITION_FAILED`, and known save outages into `DEPENDENCY_UNAVAILABLE`. Unrelated integrity failure results remain unchanged.
- Queries return known dependency outages as `DEPENDENCY_UNAVAILABLE`; cancellation, unexpected exceptions, and programmer contract violations propagate.

References: [User design](../../../../.llm/domains/user.md), [Identity schema](../../../../.llm/database/identity.md), and [transaction contracts](../Transactions/README.md).
