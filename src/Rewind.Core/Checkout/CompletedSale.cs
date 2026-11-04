using Rewind.Core.Money;

namespace Rewind.Core.Checkout;

/// <summary>A saved sale, with its change worked out for the clerk to count back.</summary>
/// <param name="RentalId">The rental's database key.</param>
/// <param name="TotalCents">What the bill came to.</param>
/// <param name="CashTenderedCents">The cash the clerk typed.</param>
/// <param name="ChangeCents">The change to give.</param>
/// <param name="Breakdown">The change as bills and coins, from <see cref="Change.Break"/>.</param>
/// <param name="CountBack">What the clerk says, from <see cref="Change.CountBack"/>.</param>
public sealed record CompletedSale(
    int RentalId,
    int TotalCents,
    int CashTenderedCents,
    int ChangeCents,
    IReadOnlyList<(int UnitCents, int Count)> Breakdown,
    IReadOnlyList<int> CountBack);
