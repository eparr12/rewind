namespace Rewind.Core.Checkout;

/// <summary>Whether a scanned store label is a case that can go on the bill, and if not, why (NET-12).</summary>
public enum CaseScanStatus
{
    /// <summary>The case is on the shelf.</summary>
    Found,

    /// <summary>No case has this label.</summary>
    NoSuchCase,

    /// <summary>The case is checked out or retired.</summary>
    NotOnShelf,
}
