namespace CloudSharp.Infrastructure.Persistence.Entities;

/// <summary>PostgreSQL row for an idempotency reservation or its committed recovery data.</summary>
internal sealed class IdempotencyRequestEntity
{
    public byte[] ScopeHash { get; set; } = null!;
    public string Key { get; set; } = null!;
    public string Actor { get; set; } = null!;
    public string Method { get; set; } = null!;
    public string CanonicalPath { get; set; } = null!;
    public string Fingerprint { get; set; } = null!;
    public string OperationId { get; set; } = null!;
    public Guid AttemptId { get; set; }
    public string State { get; set; } = null!;
    public DateTime? LeaseExpiresAt { get; set; }
    public int? ResultVersion { get; set; }
    public string? RecoveryKind { get; set; }
    public string? RecoveryPayload { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
