using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.Core.Tests.Devices;

// The search loop, and reading through fake ports (TEST-5). How a real driver behaves after an
// unplug needs hardware, so the movie-night checklist still unplugs the scanner mid-screen
// (TEST-9).
public sealed class SerialScannerTests
{
    // A loop that stops searching would hang the test instead of failing it.
    private const int TimeoutMilliseconds = 10_000;

    private const string ScannerPort = "COM7";

    private readonly FakeDeviceLocator _locator = new();
    private readonly FakeSerialPorts _ports = new();
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
        using var scanner = NewScanner(new SystemSerialPorts(NullLogger<SystemSerialPorts>.Instance));

        await StartAndWaitToRetryAsync(scanner, ct);

        Assert.Equal(ScannerStatus.Missing, _hub.Status);
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_PortHeldByAnotherProgram_ShowsScannerBusy()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = ScannerPort;
        _ports.OpenFailure = new UnauthorizedAccessException("Access to the port 'COM7' is denied.");
        using var scanner = NewScanner();

        await StartAndWaitToRetryAsync(scanner, ct);

        Assert.Equal(ScannerStatus.Busy, _hub.Status);
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_ScannerSendsLine_RaisesScanned()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = ScannerPort;
        Scan? scanned = null;
        _hub.Scanned += scan => scanned = scan;
        using var scanner = NewScanner();
        var poll = await StartReadingAsync(scanner, ct);

        _ports.Send("RV-00042\r");
        await AdvanceAsync(poll, ct);

        Assert.Equal(new Scan(ScanKind.StoreLabel, "RV-00042"), scanned);
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_ReadFails_ShowsScannerMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = ScannerPort;
        using var scanner = NewScanner();
        var poll = await StartReadingAsync(scanner, ct);

        _ports.ReadFailure = new InvalidOperationException("The port is closed."); // .NET marks some unplugged ports closed
        await AdvanceAsync(poll, ct);

        Assert.Equal(ScannerStatus.Missing, _hub.Status);
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_PortLeavesWindowsList_ShowsScannerMissing()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = ScannerPort;
        using var scanner = NewScanner();
        await StartReadingAsync(scanner, ct);

        _ports.Listed = false; // as some drivers do after an unplug, while reads still work
        await AdvanceAsync(SerialScanner.PresenceCheckInterval, ct);

        Assert.Equal(ScannerStatus.Missing, _hub.Status);
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task StartAsync_ScannerStaysPluggedIn_StaysReady()
    {
        var ct = TestContext.Current.CancellationToken;
        _locator.Port = ScannerPort;
        using var scanner = NewScanner();
        await StartReadingAsync(scanner, ct);

        await AdvanceAsync(SerialScanner.PresenceCheckInterval, ct);

        Assert.Equal(ScannerStatus.Ready, _hub.Status);
    }

    private SerialScanner NewScanner() => NewScanner(_ports);

    private SerialScanner NewScanner(ISerialPorts ports) => new(
        _locator,
        ports,
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

    // Returns once the reader has opened the port, found nothing waiting, and started waiting to
    // poll again, with how long it waits. Checking that the scanner is ready means a test that
    // expects it missing can't pass just because the port never opened.
    private async Task<TimeSpan> StartReadingAsync(SerialScanner scanner, CancellationToken cancellationToken)
    {
        await scanner.StartAsync(cancellationToken);
        var poll = await _time.NextTimerAsync(cancellationToken);
        Assert.Equal(ScannerStatus.Ready, _hub.Status);
        return poll;
    }

    // Moves the clock on, and returns once the reader is waiting on it again.
    private async Task AdvanceAsync(TimeSpan delta, CancellationToken cancellationToken)
    {
        _time.Advance(delta);
        await _time.NextTimerAsync(cancellationToken);
    }
}
