using Rewind.Core.StoreManager;

namespace Rewind.Core.Concessions;

/// <summary>Whether a change to the concession items was saved, and if not, what to fix (NET-12).</summary>
public enum ConcessionChangeStatus
{
    /// <summary>The change is saved.</summary>
    Saved,

    /// <summary>The name is blank, and nothing is saved.</summary>
    NoName,

    /// <summary>
    /// The code isn't one Barcodes reads as a concession, CON- and 1 to 20 letters, and nothing is saved.
    /// </summary>
    NotACode,

    /// <summary>Another item has the code already, active or not, and nothing is saved.</summary>
    CodeInUse,

    /// <summary>
    /// The price isn't an amount in dollars from 0 to <see cref="StoreSettings.MaxPriceCents"/>, and nothing
    /// is saved.
    /// </summary>
    NotAPrice,
}
