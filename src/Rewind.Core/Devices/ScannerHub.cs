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
/// Every barcode Rewind recognizes gets the scan beep as it's read, as at a real register, and
/// an unknown one gets the error chord. A screen that refuses a barcode it recognizes, such as
/// Checkout with a factory UPC, shows an error dialog, which adds the chord. A screen ignores an
/// unknown barcode (HW-5) rather than show a dialog for it, which would play the chord again.
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
    /// A handler that throws is logged, and the others still get the scan.
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

        // Each handler on its own, so one that throws can't keep the scan from the rest.
        foreach (var handler in Delegate.EnumerateInvocationList(Scanned))
        {
            try
            {
                handler(scan);
            }
            catch (Exception e) // GEN-7, HW-4: unwinding into the serial reader would close the port
            {
                LogScanHandlerFailed(logger, e, scan.Kind);
            }
        }
    }

    /// <summary>
    /// Records whether the scanner can be read, and raises <see cref="StatusChanged"/> when that
    /// changes. A handler that throws is logged, as in <see cref="Receive"/>.
    /// </summary>
    /// <returns>True when the status changed.</returns>
    public bool SetStatus(ScannerStatus status)
    {
        if (Interlocked.Exchange(ref _status, (int)status) == (int)status)
        {
            return false;
        }

        foreach (var handler in Delegate.EnumerateInvocationList(StatusChanged))
        {
            try
            {
                handler();
            }
            catch (Exception e) // GEN-7, HW-4: as in Receive
            {
                LogStatusHandlerFailed(logger, e, status);
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scanned {Kind} {Code}")]
    private static partial void LogScanned(ILogger logger, ScanKind kind, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored an unknown barcode: {Code}")]
    private static partial void LogUnknown(ILogger logger, string code);

    [LoggerMessage(Level = LogLevel.Error, Message = "A screen threw while handling a {Kind} scan; the scanner carries on")]
    private static partial void LogScanHandlerFailed(ILogger logger, Exception exception, ScanKind kind);

    [LoggerMessage(Level = LogLevel.Error, Message = "A screen threw while handling the scanner going {Status}; the scanner carries on")]
    private static partial void LogStatusHandlerFailed(ILogger logger, Exception exception, ScannerStatus status);
}
