using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.UI.Tests;

public sealed class ScanSimulatorTests : BunitContext
{
    private readonly ScannerHub _hub = new(new FakeSoundPlayer(), NullLogger<ScannerHub>.Instance);

    public ScanSimulatorTests() => Services.AddSingleton(_hub);

    [Fact]
    public async Task Submit_TypedLabel_ArrivesAsAStoreLabelScan()
    {
        Scan? scanned = null;
        _hub.Scanned += scan => scanned = scan;
        var cut = Render<ScanSimulator>();

        await cut.Find("input").InputAsync("rv-00042");
        await cut.Find("form").SubmitAsync();

        Assert.Equal(new Scan(ScanKind.StoreLabel, "RV-00042"), scanned);
    }

    [Fact]
    public async Task Submit_TypedScan_ClearsTheBox()
    {
        var cut = Render<ScanSimulator>();

        await cut.Find("input").InputAsync("MEM-001");
        await cut.Find("form").SubmitAsync();

        Assert.Equal("", cut.Find("input").GetAttribute("value") ?? "");
    }

    [Fact]
    public async Task Submit_EmptyBox_SendsNothing()
    {
        var scans = 0;
        _hub.Scanned += _ => scans++;
        var cut = Render<ScanSimulator>();

        await cut.Find("form").SubmitAsync();

        Assert.Equal(0, scans);
    }
}
