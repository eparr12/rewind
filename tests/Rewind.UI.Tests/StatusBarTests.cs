using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.UI.Tests;

public sealed class StatusBarTests : BunitContext
{
    private readonly FakeScanner _scanner = new();

    public StatusBarTests() => Services.AddSingleton<IScanner>(_scanner);

    [Fact]
    public void Render_ScannerNotFoundYet_ShowsScannerMissing()
    {
        var cut = Render<StatusBar>();

        Assert.Contains("Scanner missing", cut.Find(".status-bar").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusChanged_ScannerFound_ShowsScannerReady()
    {
        var cut = Render<StatusBar>();

        _scanner.SetStatus(ScannerStatus.Ready);

        cut.WaitForAssertion(() =>
            Assert.Contains("Scanner ready", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Scanned_StoreLabel_ShowsCodeAndKind()
    {
        var cut = Render<StatusBar>();

        _scanner.Emit(new Scan(ScanKind.StoreLabel, "RV-00042"));

        cut.WaitForAssertion(() =>
            Assert.Contains("RV-00042 · Store label", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Scanned_TooLongToKeep_ShowsUnknownBarcode()
    {
        var cut = Render<StatusBar>();

        _scanner.Emit(new Scan(ScanKind.Unknown, ""));

        cut.WaitForAssertion(() =>
            Assert.Contains("Unknown barcode", cut.Find(".status-bar").TextContent, StringComparison.Ordinal));
    }
}
