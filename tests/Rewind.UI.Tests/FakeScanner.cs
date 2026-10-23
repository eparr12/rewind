using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.UI.Tests;

/// <summary>A scanner the test drives by hand (TEST-5).</summary>
internal sealed class FakeScanner : IScanner
{
    public ScannerStatus Status { get; private set; }

    public event Action<Scan>? Scanned;

    public event Action? StatusChanged;

    /// <summary>Raises a scan, as the scanner's thread would.</summary>
    public void Emit(Scan scan) => Scanned?.Invoke(scan);

    /// <summary>Changes the status, as the serial reader would.</summary>
    public void SetStatus(ScannerStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }
}
