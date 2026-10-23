namespace Rewind.Core.Devices;

/// <summary>Whether the barcode scanner can be read, for the status bar (OBS-6).</summary>
public enum ScannerStatus
{
    /// <summary>No scanner with the configured USB IDs is plugged in.</summary>
    Missing,

    /// <summary>
    /// The scanner is plugged in, but its port won't open, usually because another program,
    /// such as PuTTY, has it open.
    /// </summary>
    Busy,

    /// <summary>The scanner's port is open and being read.</summary>
    Ready,
}
