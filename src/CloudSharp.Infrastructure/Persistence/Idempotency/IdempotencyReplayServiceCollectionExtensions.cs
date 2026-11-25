using Microsoft.Extensions.DependencyInjection;

namespace CloudSharp.Infrastructure.Persistence.Idempotency;

public static class IdempotencyReplayServiceCollectionExtensions
{
    /// <summary>
    /// 작업과 결과 버전에 해당하는 복구 DTO의 직렬화 형식을 등록한다.
    /// 해당 버전의 완료 기록이 재생될 수 있는 동안 등록을 유지한다.
    /// </summary>
    public static IServiceCollection AddIdempotencyReplayType<T>(
        this IServiceCollection services,
        string operationId,
        int resultVersion) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resultVersion);

        return services.AddSingleton(IdempotencyReplayRegistration.Create<T>(operationId, resultVersion));
    }
}
