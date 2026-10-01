# Auth

Auth ports: IPasswordHasher, ISessionTokenHasher, ICurrentUser.

`ISessionStore` (UserSession):

- Accepts only `UserSession` objects carrying the token hash; plaintext tokens never cross this port.
- The Redis adapter must store the session body, update the per-user session index, and enforce the maximum of 10 active sessions (evicting the oldest) as one atomic operation (Lua script or equivalent).
- Storage failure is a hard failure for the calling flow (registration/login must not report success); known dependency outages are returned as `DEPENDENCY_UNAVAILABLE`.