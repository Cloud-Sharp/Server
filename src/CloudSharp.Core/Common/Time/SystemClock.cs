namespace CloudSharp.Core.Common.Time;

/// <summary>
/// <see cref="IClock"/>의 프로덕션 구현. <see cref="TimeProvider"/>를 래핑하여 현재 UTC 시각을 반환한다.
/// 기본적으로 <see cref="TimeProvider.System"/>을 사용하며, 단위/통합 테스트에서는 제어 가능한
/// <see cref="TimeProvider"/>를 주입해 시각을 고정할 수 있다.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
}