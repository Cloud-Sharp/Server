# Entities

EF entity types per aggregate. Internal long Id keys; PublicId (UUIDv7) external.

- Keep persistence models separate from Core domain models. Dependencies point from Infrastructure to Core.
- Entities own domain conversion: `ToDomain()` reconstitutes a snapshot, `FromDomain(domain)` creates a new entity including its id, and `UpdateFrom(domain)` copies state while retaining the existing entity's primary key.
- Copy persisted values through the domain's `Reconstitute` entry points. Mapping does not run creation or business behavior, generate ids, increment versions, or replace timestamps.
- EF schema mapping belongs in `Configurations`; queries, tracking, and expected concurrency versions belong in repositories. Saving and transactions remain in UnitOfWork and TransactionExecutor.
