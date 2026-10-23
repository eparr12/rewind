namespace Rewind.Core.Scans;

/// <summary>One barcode the scanner read, sorted by what it means to the store.</summary>
/// <param name="Kind">What kind of barcode it is.</param>
/// <param name="Code">
/// The barcode as Rewind keeps it: upper case, with a UPC in its 12-digit form. Empty when the
/// scan was too long to be any of Rewind's barcodes.
/// </param>
public sealed record Scan(ScanKind Kind, string Code);
