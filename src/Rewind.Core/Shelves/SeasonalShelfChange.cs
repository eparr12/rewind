namespace Rewind.Core.Shelves;

/// <summary>What a change to the seasonal shelves came to.</summary>
/// <param name="Status">Whether it was saved, and if not, what to fix.</param>
/// <param name="Shelf">
/// The shelf as saved, or, for <see cref="SeasonalShelfChangeStatus.NameInUse"/>, the shelf that has the name. Null
/// otherwise.
/// </param>
public sealed record SeasonalShelfChange(SeasonalShelfChangeStatus Status, SeasonalShelfRow? Shelf);
