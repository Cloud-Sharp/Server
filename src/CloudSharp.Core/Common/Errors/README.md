# Errors

Domain error types returned via FluentResults. One error class per domain failure; no strings.

`CommonError` holds codes shared across areas, e.g. `DEPENDENCY_UNAVAILABLE` (503 at the API boundary) which ports return for known dependency outages instead of throwing.