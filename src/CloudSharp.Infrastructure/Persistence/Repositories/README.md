# Repositories

Repository implementations of Core persistence ports.

- Queries return independent Core domain snapshots, not EF entities.
- `AddAsync` and `UpdateAsync(user, expectedVersion)` stage changes only. Update copies the domain state into an EF entity and retains the supplied original version for the save-time concurrency check, even for unchanged updates.
- Do not keep mutable current-user, save-target, or transaction fields. Use the shared scoped DbContext for EF tracking.
- UseCases call `IUnitOfWork.SaveChangesAsync`; repositories never save or commit. Saving errors are translated by the UnitOfWork.
- After inserting and saving, registration queries by PublicId within the same transaction to restore the DB-generated internal id.
- The executor clears EF tracking at the end of each transaction attempt. Do not clear tracking inside repository methods, which would discard changes staged by other repositories.

References: [Core persistence contracts](../../../CloudSharp.Core/Abstractions/Persistence/README.md), [User design](../../../../.llm/domains/user.md), [Identity schema](../../../../.llm/database/identity.md), and [transactions](../Transactions/README.md).
