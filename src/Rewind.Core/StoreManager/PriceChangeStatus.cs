namespace Rewind.Core.StoreManager;

/// <summary>Whether the prices were saved, and if not, which one to fix (NET-12).</summary>
public enum PriceChangeStatus
{
    /// <summary>The prices are saved.</summary>
    Saved,

    /// <summary>
    /// The rental price isn't an amount in dollars from 0 to <see cref="StoreSettings.MaxPriceCents"/>, and
    /// nothing is saved.
    /// </summary>
    RentalPriceNotValid,

    /// <summary>
    /// The late fee isn't an amount in dollars from 0 to <see cref="StoreSettings.MaxPriceCents"/>, and
    /// nothing is saved.
    /// </summary>
    LateFeeNotValid,
}
