using CloudSharp.Core.Common.Time;

namespace CloudSharp.Api.IntegrationTests.TestHost;

internal sealed class MutableClock(DateTimeOffset utcNow) : IClock
{
    private readonly object _gate = new();
    private DateTimeOffset _utcNow = EnsureUtc(utcNow);

    public DateTimeOffset UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    internal void Set(DateTimeOffset value)
    {
        var utc = EnsureUtc(value);
        lock (_gate)
        {
            _utcNow = utc;
        }
    }

    internal void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    private static DateTimeOffset EnsureUtc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Clock values must be UTC.", nameof(value));
        }

        return value;
    }
}
