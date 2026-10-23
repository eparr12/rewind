namespace Rewind.Core.Sounds;

/// <summary>
/// The register's sounds, named for what happened rather than for a file (UI-7).
/// </summary>
public enum Sound
{
    /// <summary>A scan was accepted: the scan beep.</summary>
    Scan,

    /// <summary>A sale is complete: the register chime.</summary>
    Chime,

    /// <summary>Something needs the clerk: an error or warning dialog, a factory UPC at checkout or an overdue alert.</summary>
    Error,

    /// <summary>A DVD dropped into the return box: the return chime.</summary>
    Drop,

    /// <summary>The Can't Decide spinner moved on one title.</summary>
    Tick,
}
