# EndpointRules

Endpoint rules: adapter layer only, no repository direct call, ICurrentUser usage.

Executable endpoint rules are deferred until the first concrete endpoint and its
`ICurrentUser`/repository contracts are introduced. Layer rules already prevent
non-composition API types from depending directly on Infrastructure.
