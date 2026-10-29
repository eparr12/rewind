namespace Rewind.Core.StoreManager;

/// <summary>What the store charges, in whole cents (NET-9), as the Store Manager set it.</summary>
/// <param name="RentalPriceCents">The price of renting one movie, such as 200 for $2.00.</param>
/// <param name="LateFeeCents">The late fee an Overdue card adds to the bill, such as 100 for $1.00.</param>
public sealed record Prices(int RentalPriceCents, int LateFeeCents);
