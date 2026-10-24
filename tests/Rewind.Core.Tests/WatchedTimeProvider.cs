using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Rewind.Core.Tests;

/// <summary>
/// Fake time (TEST-4) that also tells the test when code starts a timer, such as a Task.Delay,
/// so the test moves the clock only once a background loop is waiting on it.
/// </summary>
internal sealed class WatchedTimeProvider : FakeTimeProvider
{
    private readonly Channel<TimeSpan> _timers = Channel.CreateUnbounded<TimeSpan>();

    public WatchedTimeProvider()
    {
    }

    public WatchedTimeProvider(DateTimeOffset startDateTime)
        : base(startDateTime)
    {
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        _timers.Writer.TryWrite(dueTime);
        return timer;
    }

    /// <summary>Waits until code starts its next timer, and returns how long that timer waits.</summary>
    public ValueTask<TimeSpan> NextTimerAsync(CancellationToken cancellationToken) =>
        _timers.Reader.ReadAsync(cancellationToken);
}
