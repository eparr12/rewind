using Rewind.Core.Money;

namespace Rewind.Core.StoreManager;

/// <summary>
/// What the Store Manager asks of Core for the store's prices, one call per action (ARCH-5): reading
/// them, and saving the rental price and the late fee as they were typed. <see cref="StorePrices"/> does
/// it, and the UI's tests use a fake (ADR-0003).
/// </summary>
public interface IStorePrices
{
    /// <summary>The rental price and the late fee, as last saved.</summary>
    Task<Prices> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Saves the rental price and the late fee, each typed in dollars, such as 2.50 or $2, as
    /// <see cref="Dollars.TryParse"/> reads them. Each must come to no more than
    /// <see cref="StoreSettings.MaxPriceCents"/>, so 200 typed for 2.00 is refused. When one isn't a
    /// price, its status comes back, the rental price's first, and nothing is saved. Prices the same as
    /// those saved change nothing, and nothing is logged.
    /// </summary>
    Task<PriceChange> SaveAsync(string rentalPrice, string lateFee, CancellationToken cancellationToken);
}
