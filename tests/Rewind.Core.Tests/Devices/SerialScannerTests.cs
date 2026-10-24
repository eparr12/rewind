using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests.Devices;

// The search loop, and a port that's gone before it opens. Reading a real port needs hardware,
// so that's tried by hand: the movie-night checklist unplugs the scanner mid-screen (TEST-9).
public sealed class SerialScannerTests
{
    // A loop that stops searching would hang the test instead of failing it.
    private const int TimeoutMilliseconds = 10_000;

    private readonly FakeDeviceLocator _locator = new();
    private readonly WatchedTimeProvider _time = new();
    private readonly ScannerHub _hub = new(new FakeSoundPlayer(), NullLogger<ScannerHub>.Instance);

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_NoScannerPluggedIn_SearchesAgainAfterRetryDelay()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scanner = NewScanner();
        await StartAndWaitToRetryAsync(scanner, ct);

        _time.Advance(SerialScanner.RetryDelay);

        Assert.Equal(2, await _locator.NextSearchAsync(ct));
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_SearchThrows_KeepsSearching()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Failure = new InvalidOperationException("The search failed.");
        using var scanner = NewScanner();
        await StartAndWaitToRetryAsync(scanner, ct);

        _time.Advance(SerialScanner.RetryDelay);

        Assert.Equal(2, await _locator.NextSearchAsync(ct));
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StopAsync_WhileWaitingToRetry_Stops()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scanner = NewScanner();
        await StartAndWaitToRetryAsync(scanner, ct);

        await scanner.StopAsync(ct);

        Assert.True(scanner.ExecuteTask is { IsCompleted: true, IsFaulted: false });
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_PortGoneBeforeItOpens_ShowsScannerMissing()
    {
        // Linux refuses every port it can't open as if another program had it.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows says when a port no longer exists.");
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = "COM255"; // the search finds it, but no such port exists
        using var scanner = NewScanner();

        await StartAndWaitToRetryAsync(scanner, ct);

        Assert.Equal(ScannerStatus.Missing, _hub.Status);
    }

    private SerialScanner NewScanner() => new(
        _locator,
        _hub,
        Options.Create(new ScannerOptions { VendorId = "0483", ProductId = "374B" }),
        _time,
        NullLogger<SerialScanner>.Instance);

    // Returns once the first search is over and the reader is waiting on the fake clock, so
    // the test never moves the clock before the reader starts waiting.
    private async Task StartAndWaitToRetryAsync(SerialScanner scanner, CancellationToken cancellationToken)
    {
        await scanner.StartAsync(cancellationToken);
        await _locator.NextSearchAsync(cancellationToken);
        await _time.NextTimerAsync(cancellationToken);
    }
}
