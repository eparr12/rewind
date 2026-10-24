using System.Threading.Channels;
using Microsoft.Extensions.Time.Testing;

namespace Rewind.Core.Tests;

/// <summary>
/// Fake time (TEST-4) that also tells the test when code starts a timer, such as a Task.Delay,
/// so the test moves the clock only once a background loop is waiting on it.
/// </summary>
internal sealed class WatchedTimeProvider : FakeTimeProvider
{
    // Real time, not fake: how long NextTimerAsync waits for code to start a timer.
    private static readonly TimeSpan NoTimer = TimeSpan.FromSeconds(10);

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

    /// <summary>
    /// Waits until code starts its next timer, and returns how long that timer waits. Code that
    /// never starts one fails the test after 10 seconds, instead of hanging the run.
    /// </summary>
    public async ValueTask<TimeSpan> NextTimerAsync(CancellationToken cancellationToken) =>
        await _timers.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(NoTimer, cancellationToken);
}
