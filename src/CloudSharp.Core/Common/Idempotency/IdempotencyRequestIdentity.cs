namespace CloudSharp.Core.Common.Idempotency;

/// <summary>API 경계에서 정규화한 요청자의 작업 범위다.</summary>
public sealed record IdempotencyScope(string Actor, string Method, string CanonicalPath);

public sealed record IdempotencyRequestIdentity(
    IdempotencyScope Scope,
    string Key,
    string Fingerprint,
    string OperationId);

/// <summary>한 요청의 실행 권한을 식별한다. 선점을 인수할 때마다 새로운 ID를 발급한다.</summary>
public sealed record IdempotencyReservation(IdempotencyRequestIdentity Request, Guid AttemptId);
