using Microsoft.Extensions.Logging;
using Rewind.Core.Devices;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;

namespace Rewind.Core.Tests.Devices;

public sealed class ScannerHubTests
{
    private readonly FakeSoundPlayer _sounds = new();
    private readonly ListLogger<ScannerHub> _logger = new();

    [Fact]
    public void Receive_StoreLabel_RaisesScannedWithItsKind()
    {
        var hub = NewHub();
        Scan? scanned = null;
        hub.Scanned += scan => scanned = scan;

        hub.Receive("RV-00042");

        Assert.Equal(new Scan(ScanKind.StoreLabel, "RV-00042"), scanned);
    }

    [Fact]
    public void Receive_RewindBarcode_PlaysScanBeep()
    {
        var hub = NewHub();

        hub.Receive("MEM-001");

        Assert.Equal(Sound.Scan, Assert.Single(_sounds.Played));
    }

    [Fact]
    public void Receive_UnknownCode_PlaysErrorChord()
    {
        var hub = NewHub();

        hub.Receive("HELLO");

        Assert.Equal(Sound.Error, Assert.Single(_sounds.Played));
    }

    [Fact]
    public void Receive_UnknownCode_StillRaisesScanned()
    {
        var hub = NewHub();
        Scan? scanned = null;
        hub.Scanned += scan => scanned = scan;

        hub.Receive("HELLO");

        Assert.Equal(new Scan(ScanKind.Unknown, "HELLO"), scanned);
    }

    [Fact]
    public void Receive_HandlerThrows_StillRaisesScannedForTheRest()
    {
        var hub = NewHub();
        Scan? scanned = null;
        hub.Scanned += _ => throw new InvalidOperationException("A screen's bug.");
        hub.Scanned += scan => scanned = scan;

        hub.Receive("RV-00042");

        Assert.Equal(new Scan(ScanKind.StoreLabel, "RV-00042"), scanned);
    }

    [Fact]
    public void Receive_HandlerThrows_LogsAnError()
    {
        var hub = NewHub();
        var bug = new InvalidOperationException("A screen's bug.");
        hub.Scanned += _ => throw bug;

        hub.Receive("RV-00042");

        Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception == bug);
    }

    [Fact]
    public void Status_BeforeTheReaderReports_IsMissing()
    {
        var hub = NewHub();

        Assert.Equal(ScannerStatus.Missing, hub.Status);
    }

    [Fact]
    public void SetStatus_NewStatus_RaisesStatusChanged()
    {
        var hub = NewHub();
        var changes = 0;
        hub.StatusChanged += () => changes++;

        hub.SetStatus(ScannerStatus.Ready);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void SetStatus_SameStatus_RaisesNothing()
    {
        var hub = NewHub();
        var changes = 0;
        hub.StatusChanged += () => changes++;

        hub.SetStatus(ScannerStatus.Missing);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void SetStatus_HandlerThrows_StillRaisesStatusChangedForTheRest()
    {
        var hub = NewHub();
        var changes = 0;
        hub.StatusChanged += () => throw new InvalidOperationException("A screen's bug.");
        hub.StatusChanged += () => changes++;

        hub.SetStatus(ScannerStatus.Ready);

        Assert.Equal(1, changes);
    }

    private ScannerHub NewHub() => new(_sounds, _logger);
}
