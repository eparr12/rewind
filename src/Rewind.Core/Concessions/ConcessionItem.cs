using Rewind.Core.StoreManager;

namespace Rewind.Core.Concessions;

/// <summary>
/// A snack or a drink sold at the concession bar, with the barcode it has on the laminated concession
/// menu. Items are deactivated, never deleted, so their history stays (DATA-4).
/// </summary>
public sealed class ConcessionItem
{
    /// <summary>The longest name an item can have: room for Fountain pop or SodaStream, not a sentence.</summary>
    public const int MaxNameLength = 30;

    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>The name the register and the concession menu show, such as Popcorn, no longer than <see cref="MaxNameLength"/>.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// The barcode on the concession menu, such as CON-POPCORN. It's fixed once saved, since a printed menu
    /// carries it, and no two items share one.
    /// </summary>
    public required string Code { get; set; }

    /// <summary>The price in cents, from 0 to <see cref="StoreSettings.MaxPriceCents"/>. Each sale copies it (DATA-10).</summary>
    public int PriceCents { get; set; }

    /// <summary>False once the item is deactivated in the Store Manager.</summary>
    public bool IsActive { get; set; } = true;
}
