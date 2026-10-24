using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Rewind.Core.Metadata;

namespace Rewind.Core.Tests.Metadata;

// The pacer on its own: each lookup is a fake that counts its calls, and the clock is fake, so
// nothing sleeps (TEST-4). A lookup that returns at once never waited.
public sealed class UpcItemDbPacerTests : IDisposable
{
    private static readonly UpcAnswer Found = new(UpcStatus.Found, "Tommy Boy (DVD)", null, null);
    private static readonly UpcAnswer TooFast = new(UpcStatus.TryLater, null, UpcTryLaterReason.TooFast, null);

    private readonly WatchedTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 23, 0, 0, TimeSpan.Zero));
    private readonly SettableClock _clock;
    private readonly UpcItemDbPacer _pacer;
    private int _calls;

    public UpcItemDbPacerTests()
    {
        _clock = new SettableClock(_time);
        _pacer = new UpcItemDbPacer(_clock, NullLogger<UpcItemDbPacer>.Instance);
    }

    public void Dispose() => _pacer.Dispose();

    // The free plan's burst limit is 6 lookups a minute, so a quick run of 6 scans goes straight
    // through, one after another.
    [Fact]
    public async Task LookupAsync_SixInAMinute_NoneWaits()
    {
        for (var i = 0; i < UpcItemDbPacer.BurstLimit; i++)
        {
            var lookup = LookUp(Found);
            Assert.True(lookup.IsCompletedSuccessfully);
            await lookup;
        }

        Assert.Equal(6, _calls);
    }

    [Fact]
    public async Task LookupAsync_SeventhInAMinute_WaitsUntilTheFirstIsAMinuteOld()
    {
        var ct = TestContext.Current.CancellationToken;
        await LookUp(Found);
        _time.Advance(TimeSpan.FromSeconds(20));
        await LookUpSeveralAsync(UpcItemDbPacer.BurstLimit - 1);

        var seventh = LookUp(Found);

        Assert.Equal(TimeSpan.FromSeconds(40), await _time.NextTimerAsync(ct));
        Assert.Equal(6, _calls);
        _time.Advance(TimeSpan.FromSeconds(40));
        await seventh;
        Assert.Equal(7, _calls);
    }

    // Long after a run of 6, there's no wait at all: the calls of more than a minute ago no longer
    // count.
    [Fact]
    public async Task LookupAsync_LongAfterSix_DoesNotWait()
    {
        await LookUpSeveralAsync(UpcItemDbPacer.BurstLimit);
        _time.Advance(TimeSpan.FromMinutes(10));

        var next = LookUp(Found);

        Assert.True(next.IsCompletedSuccessfully);
        await next;
    }

    // The screen says how long the wait is, then that the lookup is going ahead.
    [Fact]
    public async Task LookupAsync_WaitingForTheBurstLimit_SaysHowLong()
    {
        var ct = TestContext.Current.CancellationToken;
        var waits = new List<TimeSpan>();
        await LookUpSeveralAsync(UpcItemDbPacer.BurstLimit);
        var seventh = _pacer.LookupAsync(Call(Found), new ListProgress(waits), ct);
        await _time.NextTimerAsync(ct);

        _time.Advance(UpcItemDbPacer.BurstWindow);

        await seventh;
        Assert.Equal([UpcItemDbPacer.BurstWindow, TimeSpan.Zero], waits);
    }

    // The free plan allows one connection, so a lookup waits while another is still being answered.
    [Fact]
    public async Task LookupAsync_WhileAnotherIsBeingAnswered_WaitsItsTurn()
    {
        var answer = new TaskCompletionSource<UpcAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _pacer.LookupAsync(_ => { _calls++; return answer.Task; }, null, TestContext.Current.CancellationToken);

        var second = LookUp(Found);

        Assert.Equal(1, _calls);
        answer.SetResult(Found);
        await first;
        await second;
        Assert.Equal(2, _calls);
    }

    // Leaving the screen, or Esc, ends a lookup that's waiting for its turn, and nothing is asked.
    [Fact]
    public async Task LookupAsync_CancelledWhileWaiting_EndsAndAsksNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var stop = new CancellationTokenSource();
        await LookUpSeveralAsync(UpcItemDbPacer.BurstLimit);
        var seventh = _pacer.LookupAsync(Call(Found), null, stop.Token);
        await _time.NextTimerAsync(ct);

        await stop.CancelAsync();

        // The fake clock never moves, so only the cancellation can end the wait. The real-time
        // limit is there so a wait that ignores it fails the test instead of hanging the run.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seventh.WaitAsync(TimeSpan.FromSeconds(10), ct));
        Assert.Equal(6, _calls);
    }

    // After "too fast", asking again only makes UPCitemdb wait longer, so for a minute every lookup
    // is answered TooFast and nothing is sent, whatever Retry-After says.
    [Fact]
    public async Task LookupAsync_AfterTooFast_AsksNothingForAMinute()
    {
        await LookUp(TooFast with { RetryAfter = TimeSpan.FromHours(3) });
        _time.Advance(UpcItemDbPacer.Cooldown - TimeSpan.FromSeconds(1));

        var during = await LookUp(Found);
        _time.Advance(TimeSpan.FromSeconds(1));
        var after = await LookUp(Found);

        Assert.Equal(TooFast, during);
        Assert.Equal(Found, after);
        Assert.Equal(2, _calls);
    }

    // After "out of lookups for today", nothing is sent until Retry-After says, and every lookup
    // meanwhile says how long that is still, for the screen to say when.
    [Fact]
    public async Task LookupAsync_AfterTheDailyLimit_AsksNothingUntilRetryAfter()
    {
        var refusal = await LookUp(DailyLimit(TimeSpan.FromHours(3)));
        _time.Advance(TimeSpan.FromHours(2));

        var during = await LookUp(Found);
        _time.Advance(TimeSpan.FromHours(1));
        var after = await LookUp(Found);

        Assert.Equal(DailyLimit(TimeSpan.FromHours(3)), refusal);
        Assert.Equal(DailyLimit(TimeSpan.FromHours(1)), during);
        Assert.Equal(Found, after);
        Assert.Equal(2, _calls);
    }

    // SEC-5: Retry-After is UPCitemdb's word, so a wait longer than a day is cut to 25 hours.
    [Fact]
    public async Task LookupAsync_RetryAfterOverADay_IsCut()
    {
        var refusal = await LookUp(DailyLimit(TimeSpan.FromDays(10)));
        _time.Advance(UpcItemDbPacer.LongestRetryAfter);

        await LookUp(Found);

        Assert.Equal(UpcItemDbPacer.LongestRetryAfter, refusal.RetryAfter);
        Assert.Equal(2, _calls);
    }

    // Without a usable Retry-After, a minute's quiet, and no word of when the lookups come back.
    [Fact]
    public async Task LookupAsync_DailyLimitWithoutRetryAfter_AsksNothingForAMinute()
    {
        var refusal = await LookUp(DailyLimit(null));
        _time.Advance(UpcItemDbPacer.Cooldown - TimeSpan.FromSeconds(1));

        var during = await LookUp(Found);
        _time.Advance(TimeSpan.FromSeconds(1));
        await LookUp(Found);

        Assert.Equal(DailyLimit(null), refusal);
        Assert.Equal(DailyLimit(null), during);
        Assert.Equal(2, _calls);
    }

    // NET-8: the quiet keeps to the steady clock, as the burst limit does, so the computer's clock
    // set forward, as Windows does when it corrects the time, doesn't end it early...
    [Fact]
    public async Task LookupAsync_ClockSetForwardAfterTooFast_StillAsksNothing()
    {
        await LookUp(TooFast);
        _clock.SetBy = TimeSpan.FromHours(1);

        var during = await LookUp(Found);

        Assert.Equal(TooFast, during);
        Assert.Equal(1, _calls);
    }

    // ...and set back, doesn't keep Rewind quiet longer, or put off when the lookups come back.
    [Fact]
    public async Task LookupAsync_ClockSetBackAfterTheDailyLimit_KeepsToRetryAfter()
    {
        await LookUp(DailyLimit(TimeSpan.FromHours(3)));
        _clock.SetBy = -TimeSpan.FromHours(5);
        _time.Advance(TimeSpan.FromHours(2));

        var during = await LookUp(Found);
        _time.Advance(TimeSpan.FromHours(1));
        var after = await LookUp(Found);

        Assert.Equal(DailyLimit(TimeSpan.FromHours(1)), during);
        Assert.Equal(Found, after);
        Assert.Equal(2, _calls);
    }

    private static UpcAnswer DailyLimit(TimeSpan? retryAfter) => new(UpcStatus.TryLater, null, UpcTryLaterReason.DailyLimit, retryAfter);

    private Func<CancellationToken, Task<UpcAnswer>> Call(UpcAnswer answer) => _ =>
    {
        _calls++;
        return Task.FromResult(answer);
    };

    private Task<UpcAnswer> LookUp(UpcAnswer answer) => _pacer.LookupAsync(Call(answer), null, TestContext.Current.CancellationToken);

    private async Task LookUpSeveralAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await LookUp(Found);
        }
    }

    // Records each report as it's made. Progress<T> would post them to a thread pool thread later.
    private sealed class ListProgress(List<TimeSpan> reports) : IProgress<TimeSpan>
    {
        public void Report(TimeSpan value) => reports.Add(value);
    }

    // The computer's clock, which someone can set. Its wall time moves by SetBy, while its
    // timestamps and timers keep to the fake steady clock underneath, as Stopwatch's do.
    private sealed class SettableClock(FakeTimeProvider steady) : TimeProvider
    {
        public TimeSpan SetBy { get; set; }

        public override long TimestampFrequency => steady.TimestampFrequency;

        public override DateTimeOffset GetUtcNow() => steady.GetUtcNow() + SetBy;

        public override long GetTimestamp() => steady.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            steady.CreateTimer(callback, state, dueTime, period);
    }
}
