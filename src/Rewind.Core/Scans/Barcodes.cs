using System.Text.RegularExpressions;

namespace Rewind.Core.Scans;

/// <summary>
/// Sorts what the scanner read into Rewind's kinds of barcode (Overview: Barcodes and intake).
/// </summary>
public static partial class Barcodes
{
    /// <summary>
    /// The longest scan Rewind looks at. A longer one is unknown before any pattern runs,
    /// because barcodes are untrusted input (SEC-5).
    /// </summary>
    public const int MaxLength = 32;

    /// <summary>
    /// Sorts one scan. Spaces around it are ignored and letters are made upper case, so
    /// rv-00042 is the store label RV-00042. A UPC comes back as 12 or 13 digits: a 13-digit EAN
    /// that starts with 0 becomes its 12-digit UPC-A.
    /// </summary>
    public static Scan Classify(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        // The length is checked before trimming, so a long line can't pass by being mostly spaces.
        if (raw.Length > MaxLength)
        {
            return new Scan(ScanKind.Unknown, "");
        }

        var code = raw.Trim().ToUpperInvariant();
        if (StoreLabelPattern().IsMatch(code))
        {
            return new Scan(ScanKind.StoreLabel, code);
        }

        if (MemberCardPattern().IsMatch(code))
        {
            return new Scan(ScanKind.MemberCard, code);
        }

        if (ConcessionPattern().IsMatch(code))
        {
            return new Scan(ScanKind.Concession, code);
        }

        return Upc.TryNormalize(code, out var upc)
            ? new Scan(ScanKind.Upc, upc)
            : new Scan(ScanKind.Unknown, code);
    }

    // [0-9], never \d: in .NET, \d matches every Unicode digit, such as Arabic-Indic ones (SEC-5).
    [GeneratedRegex("^RV-[0-9]{5}$")]
    private static partial Regex StoreLabelPattern();

    [GeneratedRegex("^MEM-[0-9]{3}$")]
    private static partial Regex MemberCardPattern();

    [GeneratedRegex("^CON-[A-Z]{1,20}$")]
    private static partial Regex ConcessionPattern();
}
