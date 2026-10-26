namespace Rewind.Core.Labels;

/// <summary>How making a sheet went (NET-12).</summary>
public enum SheetStatus
{
    /// <summary>The sheet's numbers are spent, and the sheet is ready to print.</summary>
    Reserved,

    /// <summary>
    /// A full sheet would pass the last number its codes can carry, RV-99999 or MEM-999, so nothing was
    /// spent. Sheets are only ever full (ADR-0006).
    /// </summary>
    OutOfNumbers,
}
