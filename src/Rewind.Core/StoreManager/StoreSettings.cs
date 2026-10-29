namespace Rewind.Core.StoreManager;

/// <summary>
/// The store's name and prices, edited in the Store Manager, and the numbers its next sheets of store
/// labels and member cards start at: one row. Money is whole cents (NET-9, DATA-2), and each sale
/// copies the price it used (DATA-10).
/// </summary>
public sealed class StoreSettings
{
    /// <summary>The key of the only row.</summary>
    public const int SingleRowId = 1;

    /// <summary>The number of the first store label and of the first member card: RV-00001 and MEM-001.</summary>
    public const int FirstNumber = 1;

    /// <summary>
    /// The highest price the Store Manager can set, $99.99: far past any 1990s price, so 200 typed for 2.00
    /// is refused rather than saved.
    /// </summary>
    public const int MaxPriceCents = 9_999;

    /// <summary>Always <see cref="SingleRowId"/>.</summary>
    public int Id { get; set; }

    /// <summary>The name on the sign, the receipts and the pole display.</summary>
    public required string StoreName { get; set; }

    /// <summary>The price of renting one movie, in cents, from 0 to <see cref="MaxPriceCents"/>.</summary>
    public int RentalPriceCents { get; set; }

    /// <summary>The late fee an Overdue card adds to the bill, in cents, from 0 to <see cref="MaxPriceCents"/>.</summary>
    public int LateFeeCents { get; set; }

    /// <summary>
    /// The number the next sheet of store labels starts at, such as 31 for RV-00031. A sheet spends its
    /// numbers as it's made, so no two labels share one (ADR-0006).
    /// </summary>
    public int NextStoreLabelNumber { get; set; }

    /// <summary>
    /// The number the next sheet of member cards starts at, such as 11 for MEM-011. A sheet spends its
    /// numbers as it's made, so no two cards share one (ADR-0006).
    /// </summary>
    public int NextMemberCardNumber { get; set; }
}
