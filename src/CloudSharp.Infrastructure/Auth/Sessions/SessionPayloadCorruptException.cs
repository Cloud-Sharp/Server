namespace CloudSharp.Infrastructure.Auth.Sessions;

/// <summary>
/// Redis Session payload의 필수 field 누락이나 복원 불가능한 값 손상을 나타낸다.
/// Redis 연결·timeout 같은 dependency 장애와 구분하기 위한 전용 예외다.
/// </summary>
internal sealed class SessionPayloadCorruptException : InvalidOperationException
{
    internal SessionPayloadCorruptException(string message)
        : base(message)
    {
    }
}
