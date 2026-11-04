namespace Rewind.Core.Checkout;

/// <summary>What completing a sale came to.</summary>
/// <param name="Status">Whether it completed, and if not, what to fix.</param>
/// <param name="TotalCents">What the bill came to, which a <see cref="SaleStatus.CashShort"/> dialog names.</param>
/// <param name="StoreLabel">The case that's no longer on the shelf. Null for any other status.</param>
/// <param name="Sale">The saved sale. Null unless <paramref name="Status"/> is <see cref="SaleStatus.Completed"/>.</param>
public sealed record SaleResult(SaleStatus Status, int TotalCents, string? StoreLabel, CompletedSale? Sale);
