namespace Rewind.Core.Checkout;

/// <summary>What a member's card came to.</summary>
/// <param name="Status">Whether it started a sale, and if not, why.</param>
/// <param name="Sale">What the open sale needs. Null unless <paramref name="Status"/> is <see cref="SaleStartStatus.Started"/>.</param>
public sealed record SaleStart(SaleStartStatus Status, OpenedSale? Sale);
