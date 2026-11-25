namespace CloudSharp.Core.Common.Idempotency;

/// <summary>업무 결과에서 영속화와 재시도에 필요한 정보만 투영한다.</summary>
public abstract record IdempotencyRecovery
{
    private IdempotencyRecovery() { }

    public sealed record Replayable<T>(T Data) : IdempotencyRecovery where T : notnull;

    public sealed record SecretNotReplayable(Guid? ResourcePublicId) : IdempotencyRecovery;
}
