namespace Rewind.Core.Sounds;

/// <summary>
/// The register's sounds, named for what happened rather than for a file (UI-7).
/// </summary>
public enum Sound
{
    /// <summary>A barcode Rewind knows was read: the scan beep.</summary>
    Scan,

    /// <summary>A sale completes, or a case goes on the shelf at intake: the register chime.</summary>
    Chime,

    /// <summary>
    /// Something needs the clerk: the error chord. DialogService plays it for every warning or
    /// error dialog, such as a factory UPC at checkout or an overdue alert, so a screen that shows
    /// one never plays it too. Only where no dialog opens is it played directly, as ScannerHub
    /// does for an unknown barcode.
    /// </summary>
    Error,

    /// <summary>A DVD dropped into the return box: the return chime.</summary>
    Drop,

    /// <summary>The Can't Decide spinner moved on one title.</summary>
    Tick,
}
