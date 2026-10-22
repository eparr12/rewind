namespace Rewind.Core.StoreManager;

/// <summary>
/// The store's name and prices: one row, edited in the Store Manager. Money is whole cents
/// (NET-9, DATA-2), and each sale copies the price it used (DATA-10).
/// </summary>
public sealed class StoreSettings
{
    /// <summary>The key of the only row.</summary>
    public const int SingleRowId = 1;

    /// <summary>Always <see cref="SingleRowId"/>.</summary>
    public int Id { get; set; }

    /// <summary>The name on the sign, the receipts and the pole display.</summary>
    public required string StoreName { get; set; }

    /// <summary>The price of renting one movie, in cents.</summary>
    public int RentalPriceCents { get; set; }

    /// <summary>The late fee an Overdue card adds to the bill, in cents.</summary>
    public int LateFeeCents { get; set; }
}
