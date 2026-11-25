namespace CloudSharp.Infrastructure.Persistence.Idempotency;

internal sealed partial class PostgresIdempotencyStore
{
    private const string ByKey = "scope_hash = @ScopeHash AND idempotency_key = @Key";

    private const string Owned = ByKey + """
         AND actor = @Actor AND method = @Method AND canonical_path = @CanonicalPath
         AND fingerprint = @Fingerprint AND operation_id = @OperationId
         AND attempt_id = @AttemptId AND state = 'IN_PROGRESS'
        """;

    private const string SelectSql = """
        SELECT scope_hash AS ScopeHash, idempotency_key AS Key, actor AS Actor,
               method AS Method, canonical_path AS CanonicalPath, fingerprint AS Fingerprint,
               operation_id AS OperationId, attempt_id AS AttemptId, state AS State,
               lease_expires_at AS LeaseExpiresAt, result_version AS ResultVersion,
               recovery_kind AS RecoveryKind, recovery_payload::text AS RecoveryPayload,
               completed_at AS CompletedAt, expires_at AS ExpiresAt
        FROM idempotency_requests
        WHERE

        """ + ByKey;

    private const string InsertSql = """
        INSERT INTO idempotency_requests (
            scope_hash, idempotency_key, actor, method, canonical_path, fingerprint,
            operation_id, attempt_id, state, lease_expires_at)
        VALUES (
            @ScopeHash, @Key, @Actor, @Method, @CanonicalPath, @Fingerprint,
            @OperationId, @AttemptId, 'IN_PROGRESS', @LeaseExpiresAt)
        ON CONFLICT (scope_hash, idempotency_key) DO NOTHING
        """;

    private const string ReplaceSql = """
        UPDATE idempotency_requests
        SET fingerprint = @Fingerprint, operation_id = @OperationId, attempt_id = @AttemptId,
            state = 'IN_PROGRESS', lease_expires_at = @LeaseExpiresAt,
            result_version = NULL, recovery_kind = NULL, recovery_payload = NULL,
            completed_at = NULL, expires_at = NULL
        WHERE

        """ + ByKey;

    private const string CompleteSql = """
        UPDATE idempotency_requests
        SET state = 'COMPLETED', lease_expires_at = NULL, result_version = @ResultVersion,
            recovery_kind = @RecoveryKind, recovery_payload = CAST(@RecoveryPayload AS jsonb),
            completed_at = @CompletedAt, expires_at = @ExpiresAt
        WHERE

        """ + Owned;

    private const string ReleaseSql = "DELETE FROM idempotency_requests WHERE " + Owned;
}
