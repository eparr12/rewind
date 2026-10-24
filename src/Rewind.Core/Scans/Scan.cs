namespace Rewind.Core.Scans;

/// <summary>One barcode the scanner read, sorted by what it means to the store.</summary>
/// <param name="Kind">What kind of barcode it is.</param>
/// <param name="Code">
/// The barcode as Rewind keeps it: upper case, with a UPC as 12 or 13 digits, since a 13-digit EAN
/// that starts with 0 becomes its 12-digit UPC-A and any other stays as it is. Empty when the scan
/// was too long to be any of Rewind's barcodes.
/// </param>
public sealed record Scan(ScanKind Kind, string Code);
