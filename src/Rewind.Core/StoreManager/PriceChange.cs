namespace Rewind.Core.StoreManager;

/// <summary>What saving the prices came to.</summary>
/// <param name="Status">Whether they were saved, and if not, which one to fix.</param>
/// <param name="Prices">The prices as saved, or null when nothing was.</param>
public sealed record PriceChange(PriceChangeStatus Status, Prices? Prices);
