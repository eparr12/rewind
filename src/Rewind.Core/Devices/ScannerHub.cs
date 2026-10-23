using Microsoft.Extensions.Logging;
using Rewind.Core.Scans;
using Rewind.Core.Sounds;

namespace Rewind.Core.Devices;

/// <summary>
/// Where every scan comes in. The serial reader hands it each line the scanner sends, and in
/// Debug builds the simulator hands it what was typed (TEST-5). It sorts the scan, beeps and
/// raises <see cref="Scanned"/>. It also keeps the scanner's status for the status bar (OBS-6).
/// </summary>
/// <remarks>
/// Every barcode Rewind recognizes gets the scan beep as it's read, as at a real register. A
/// screen that refuses a scan, such as Checkout with a factory UPC, adds the error chord itself.
/// </remarks>
public sealed partial class ScannerHub(ISoundPlayer sounds, ILogger<ScannerHub> logger) : IScanner
{
    // A ScannerStatus stored as an int, so the reader's thread and the screens can share it.
    // It starts at 0, Missing, until the reader finds the scanner.
    private int _status;

    /// <inheritdoc />
    public ScannerStatus Status => (ScannerStatus)Volatile.Read(ref _status);

    /// <inheritdoc />
    public event Action<Scan>? Scanned;

    /// <inheritdoc />
    public event Action? StatusChanged;

    /// <summary>
    /// Takes one scan as the scanner sent it: sorts it, plays the scan beep, or the error chord
    /// for a code Rewind doesn't know, which is also logged (HW-5), then raises <see cref="Scanned"/>.
    /// </summary>
    public void Receive(string raw)
    {
        var scan = Barcodes.Classify(raw);
        if (scan.Kind == ScanKind.Unknown)
        {
            LogUnknown(logger, scan.Code);
            sounds.Play(Sound.Error);
        }
        else
        {
            LogScanned(logger, scan.Kind, scan.Code);
            sounds.Play(Sound.Scan);
        }

        Scanned?.Invoke(scan);
    }

    /// <summary>
    /// Records whether the scanner can be read, and raises <see cref="StatusChanged"/> when that
    /// changes.
    /// </summary>
    /// <returns>True when the status changed.</returns>
    public bool SetStatus(ScannerStatus status)
    {
        if (Interlocked.Exchange(ref _status, (int)status) == (int)status)
        {
            return false;
        }

        StatusChanged?.Invoke();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scanned {Kind} {Code}")]
    private static partial void LogScanned(ILogger logger, ScanKind kind, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored an unknown barcode: {Code}")]
    private static partial void LogUnknown(ILogger logger, string code);
}
