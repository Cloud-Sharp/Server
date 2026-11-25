using CloudSharp.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudSharp.Infrastructure.Persistence.Configurations;

internal sealed class IdempotencyRequestConfiguration : IEntityTypeConfiguration<IdempotencyRequestEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyRequestEntity> builder)
    {
        builder.ToTable(
            "idempotency_requests",
            table =>
            {
                table.HasCheckConstraint("chk_idempotency_requests_scope_hash_length", "octet_length(scope_hash) = 32");
                table.HasCheckConstraint("chk_idempotency_requests_key_format", "idempotency_key ~ '^[ -~]{16,128}$'");
                table.HasCheckConstraint(
                    "chk_idempotency_requests_state",
                    """
                    (state = 'IN_PROGRESS'
                        AND lease_expires_at IS NOT NULL
                        AND result_version IS NULL
                        AND recovery_kind IS NULL
                        AND recovery_payload IS NULL
                        AND completed_at IS NULL
                        AND expires_at IS NULL)
                    OR (state = 'COMPLETED'
                        AND lease_expires_at IS NULL
                        AND result_version IS NOT NULL AND result_version > 0
                        AND recovery_kind IS NOT NULL AND recovery_kind IN ('REPLAYABLE', 'SECRET_NOT_REPLAYABLE')
                        AND recovery_payload IS NOT NULL
                        AND completed_at IS NOT NULL
                        AND expires_at IS NOT NULL AND expires_at = completed_at + INTERVAL '24 hours')
                    """);
            });

        builder.HasKey(entity => new { entity.ScopeHash, entity.Key }).HasName("pk_idempotency_requests");
        builder.Property(entity => entity.ScopeHash).HasColumnName("scope_hash").HasColumnType("bytea");
        builder.Property(entity => entity.Key).HasColumnName("idempotency_key").HasMaxLength(128).UseCollation("C");
        builder.Property(entity => entity.Actor).HasColumnName("actor").IsRequired();
        builder.Property(entity => entity.Method).HasColumnName("method").IsRequired();
        builder.Property(entity => entity.CanonicalPath).HasColumnName("canonical_path").IsRequired();
        builder.Property(entity => entity.Fingerprint).HasColumnName("fingerprint").IsRequired();
        builder.Property(entity => entity.OperationId).HasColumnName("operation_id").IsRequired();
        builder.Property(entity => entity.AttemptId).HasColumnName("attempt_id").HasColumnType("uuid");
        builder.Property(entity => entity.State).HasColumnName("state").IsRequired();
        builder.Property(entity => entity.LeaseExpiresAt).HasColumnName("lease_expires_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.ResultVersion).HasColumnName("result_version");
        builder.Property(entity => entity.RecoveryKind).HasColumnName("recovery_kind");
        builder.Property(entity => entity.RecoveryPayload).HasColumnName("recovery_payload").HasColumnType("jsonb");
        builder.Property(entity => entity.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamptz");

        builder.HasIndex(entity => entity.ExpiresAt)
            .HasDatabaseName("idx_idempotency_requests_completed_expires")
            .HasFilter("state = 'COMPLETED'");
    }
}
