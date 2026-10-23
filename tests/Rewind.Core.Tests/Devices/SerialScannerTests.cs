using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Devices;

namespace Rewind.Core.Tests.Devices;

// The search loop only. Opening a real port needs hardware, so that's tried by hand with the Nucleo.
public sealed class SerialScannerTests
{
    // A loop that stops searching would hang the test instead of failing it.
    private const int TimeoutMilliseconds = 10_000;

    private readonly FakeDeviceLocator _locator = new();
    private readonly WatchedTimeProvider _time = new();

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

    private SerialScanner NewScanner() => new(
        _locator,
        new ScannerHub(new FakeSoundPlayer(), NullLogger<ScannerHub>.Instance),
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
