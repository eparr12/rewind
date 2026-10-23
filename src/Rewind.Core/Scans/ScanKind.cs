namespace Rewind.Core.Scans;

/// <summary>
/// The kinds of barcode Rewind reads. The prefix alone tells them apart (Overview: Barcodes
/// and intake).
/// </summary>
public enum ScanKind
{
    /// <summary>Not one of Rewind's barcodes. The register logs it and plays the error chord (HW-5).</summary>
    Unknown,

    /// <summary>The store label on a case, such as RV-00042. Accepted at Checkout, Return and Intake.</summary>
    StoreLabel,

    /// <summary>A membership card, such as MEM-001. Accepted at Checkout.</summary>
    MemberCard,

    /// <summary>A snack on the laminated concession card, such as CON-POPCORN. Accepted at Checkout.</summary>
    Concession,

    /// <summary>A factory UPC or EAN from a DVD's case. Accepted only at Intake.</summary>
    Upc,
}
