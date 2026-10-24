using Microsoft.Extensions.Logging;

namespace Rewind.Core.Metadata;

/// <summary>
/// Keeps Rewind's lookups inside UPCitemdb's free plan, for the whole register at once (Build Plan
/// 2.2): one at a time, since the plan allows one connection, and at most
/// <see cref="BurstLimit"/> in any <see cref="BurstWindow"/>, its burst limit, which also keeps to
/// its sustainable rate of one every 10 seconds on average. A quick run of scans goes straight
/// through, and a lookup past the limit waits its turn rather than failing. After UPCitemdb
/// refuses one, nothing is sent for a while, since asking again only makes it wait longer: a
/// <see cref="Cooldown"/> after "too fast", and after "out of lookups for today" until its
/// Retry-After says. A singleton, so every screen shares one pace. UPCitemdb counts lookups by
/// internet address, though, so another computer on the store's Wi-Fi spends the same 100, which
/// only Retry-After tells Rewind.
/// </summary>
public sealed partial class UpcItemDbPacer(TimeProvider time, ILogger<UpcItemDbPacer> logger) : IDisposable
{
    /// <summary>The most calls in any <see cref="BurstWindow"/>: the free plan's burst limit of 6 lookups a minute.</summary>
    public const int BurstLimit = 6;

    /// <summary>The time the burst limit counts calls over.</summary>
    public static readonly TimeSpan BurstWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long Rewind asks nothing after UPCitemdb says too fast, or that the day's lookups are
    /// used up without saying until when: a whole burst window, which always clears the burst limit.
    /// </summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);

    /// <summary>The longest wait Rewind takes from Retry-After (SEC-5): the day's lookups come back within a day.</summary>
    public static readonly TimeSpan LongestRetryAfter = TimeSpan.FromHours(25);

    private readonly SemaphoreSlim _turn = new(1, 1);

    // Read and written only during a turn. When each recent call ended, oldest first, as
    // TimeProvider timestamps (NET-8), so a slow answer still counts for a whole window.
    private readonly Queue<long> _recentCalls = new();

    // The quiet after UPCitemdb's last refusal: when it began, as a timestamp too, so setting the
    // computer's clock neither ends it early nor stretches it, and how long it lasts. When it ends
    // by the computer's clock is only for the log.
    private long _quietSince;
    private TimeSpan _quietFor;
    private DateTimeOffset _quietUntil;
    private UpcTryLaterReason _refusal;
    private bool _quietUntilLookupsBack;

    /// <summary>
    /// Makes one lookup through <paramref name="lookup"/> once it's this one's turn and the burst
    /// limit allows it. While UPCitemdb's last refusal still holds, it answers TryLater for the same
    /// reason instead, and asks nothing.
    /// </summary>
    /// <param name="lookup">The call to UPCitemdb, such as UpcItemDbClient's lookup of one barcode.</param>
    /// <param name="waiting">
    /// Told how long the lookup must wait for the burst limit, before it waits, then
    /// <see cref="TimeSpan.Zero"/> once it's asking. Not told anything when there's no wait.
    /// </param>
    /// <param name="cancellationToken">Cancels the lookup, and its wait for a turn.</param>
    public async Task<UpcAnswer> LookupAsync(Func<CancellationToken, Task<UpcAnswer>> lookup, IProgress<TimeSpan>? waiting, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            var quietLeft = _quietFor - time.GetElapsedTime(_quietSince);
            if (quietLeft > TimeSpan.Zero)
            {
                LogQuiet(logger, _refusal, _quietUntil);
                return new UpcAnswer(UpcStatus.TryLater, null, _refusal, _quietUntilLookupsBack ? quietLeft : null);
            }

            await WaitForTheBurstLimitAsync(waiting, cancellationToken);
            var answer = await CallAsync(lookup, cancellationToken);
            return answer.Reason is { } reason and (UpcTryLaterReason.TooFast or UpcTryLaterReason.DailyLimit)
                ? Quieten(answer, reason)
                : answer;
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _turn.Dispose();

    private async Task WaitForTheBurstLimitAsync(IProgress<TimeSpan>? waiting, CancellationToken cancellationToken)
    {
        while (_recentCalls.TryPeek(out var oldest) && time.GetElapsedTime(oldest) >= BurstWindow)
        {
            _recentCalls.Dequeue();
        }

        if (_recentCalls.Count < BurstLimit)
        {
            return;
        }

        var wait = BurstWindow - time.GetElapsedTime(_recentCalls.Peek());
        var seconds = (int)Math.Ceiling(wait.TotalSeconds);
        LogWaiting(logger, seconds, BurstLimit);
        waiting?.Report(wait);
        await Task.Delay(wait, time, cancellationToken);
        _recentCalls.Dequeue(); // the oldest call is a window old now
        waiting?.Report(TimeSpan.Zero);
    }

    // A call counts from when it ends, even one that failed or was cancelled, since UPCitemdb may
    // have counted it.
    private async Task<UpcAnswer> CallAsync(Func<CancellationToken, Task<UpcAnswer>> lookup, CancellationToken cancellationToken)
    {
        try
        {
            return await lookup(cancellationToken);
        }
        finally
        {
            _recentCalls.Enqueue(time.GetTimestamp());
        }
    }

    // After "out of lookups for today", until Retry-After says, at most LongestRetryAfter, and a
    // Cooldown when it doesn't say. After "too fast", a Cooldown: Retry-After isn't read for it.
    private UpcAnswer Quieten(UpcAnswer refusal, UpcTryLaterReason reason)
    {
        TimeSpan? lookupsBack = reason == UpcTryLaterReason.DailyLimit && refusal.RetryAfter is { } wait && wait > TimeSpan.Zero
            ? (wait < LongestRetryAfter ? wait : LongestRetryAfter)
            : null;
        _refusal = reason;
        _quietSince = time.GetTimestamp();
        _quietFor = lookupsBack ?? Cooldown;
        _quietUntil = time.GetUtcNow() + _quietFor;
        _quietUntilLookupsBack = lookupsBack is not null;
        LogQuiet(logger, reason, _quietUntil);
        return refusal with { RetryAfter = lookupsBack };
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Waiting {Seconds} seconds to ask UPCitemdb, which allows {BurstLimit} lookups a minute")]
    private static partial void LogWaiting(ILogger logger, int seconds, int burstLimit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Asking UPCitemdb nothing until {Until}, after it said {Reason}")]
    private static partial void LogQuiet(ILogger logger, UpcTryLaterReason reason, DateTimeOffset until);
}
