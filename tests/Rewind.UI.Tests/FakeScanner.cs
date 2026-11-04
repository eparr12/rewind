using Rewind.Core.Devices;
using Rewind.Core.Scans;

namespace Rewind.UI.Tests;

/// <summary>A scanner the test drives by hand (TEST-5).</summary>
internal sealed class FakeScanner : IScanner
{
    private ScannerStatus _status;
    private Exception? _statusFailure;

    public ScannerStatus Status => _statusFailure is null ? _status : throw _statusFailure;

    public event Action<Scan>? Scanned;

    public event Action? StatusChanged;

    /// <summary>Whether anything still listens for scans, such as a screen that never stopped.</summary>
    public bool IsListenedTo => Scanned is not null;

    /// <summary>Raises a scan, as the scanner's thread would.</summary>
    public void Emit(Scan scan) => Scanned?.Invoke(scan);

    /// <summary>
    /// Takes the listeners there are now, for a scan the scanner's thread raised just before one of them
    /// stopped listening, which reaches that listener after it stopped.
    /// </summary>
    public Action<Scan> RaisedBeforeStopping()
    {
        var listeners = Scanned;
        return scan => listeners?.Invoke(scan);
    }

    /// <summary>Changes the status, as the serial reader would.</summary>
    public void SetStatus(ScannerStatus status)
    {
        _status = status;
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Changes the status to one that throws when it's read, so whoever draws it fails to draw, as with
    /// a status that a listener's text has no branch for.
    /// </summary>
    public void SetStatusThatFails(Exception failure)
    {
        _statusFailure = failure;
        StatusChanged?.Invoke();
    }
}
