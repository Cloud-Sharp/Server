using CloudSharp.Core.Common.Time;

namespace CloudSharp.TestSupport.Fakes;

/// <summary>
/// 테스트에서 시간을 제어하기 위한 <see cref="IClock"/> 가짜 구현. 스레드 안전하며
/// 생성 시 초기 시각, <see cref="SetUtcNow"/> 재설정, <see cref="Advance"/> 전진을 지원한다.
/// 입력 시각은 항상 UTC로 정규화된다.
/// </summary>
public sealed class FakeClock : IClock
{
    private DateTimeOffset _now;
    private readonly object _gate = new();

    public FakeClock(DateTimeOffset initial)
    {
        _now = ToUtc(initial);
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _now;
            }
        }
    }

    /// <summary>
    /// 현재 시각을 지정한 값으로 재설정한다. 입력은 UTC로 정규화된다.
    /// </summary>
    public void SetUtcNow(DateTimeOffset value)
    {
        lock (_gate)
        {
            _now = ToUtc(value);
        }
    }

    /// <summary>
    /// 현재 시각을 지정한 기간만큼 앞으로 이동한다. 음수 기간은 거부한다.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "duration must be non-negative");
        }

        lock (_gate)
        {
            _now = _now.Add(duration);
        }
    }

    private static DateTimeOffset ToUtc(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero ? value : value.ToUniversalTime();
}