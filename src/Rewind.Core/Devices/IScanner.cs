using Rewind.Core.Scans;

namespace Rewind.Core.Devices;

/// <summary>
/// The barcode scanner as the screens see it. Scans arrive here whatever has focus (UI-4),
/// already sorted by <see cref="Barcodes.Classify"/>.
/// </summary>
public interface IScanner
{
    /// <summary>Whether the scanner can be read right now (OBS-6).</summary>
    ScannerStatus Status { get; }

    /// <summary>
    /// Raised for every scan, unknown ones included. It can arrive on a background thread, so a
    /// component moves onto its own with InvokeAsync before touching its state (UI-11).
    /// </summary>
    event Action<Scan>? Scanned;

    /// <summary>Raised when <see cref="Status"/> changes, possibly on a background thread (UI-11).</summary>
    event Action? StatusChanged;
}
