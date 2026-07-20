namespace CloudSharp.Core.Common.Time;

/// <summary>
/// 현재 UTC 시각을 제공하는 좁은 시간 계약. 비즈니스 로직은 <see cref="DateTimeOffset.UtcNow"/>나
/// <see cref="TimeProvider"/>를 직접 사용하지 않고 이 계약을 통해 시각을 얻는다.
/// delay, timer, monotonic timestamp는 공개하지 않는다.
/// </summary>
public interface IClock
{
    /// <summary>
    /// 현재 UTC 시각을 반환한다. 반환값은 항상 UTC <see cref="DateTimeOffset"/>이다.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}