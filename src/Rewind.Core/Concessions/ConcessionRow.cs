namespace Rewind.Core.Concessions;

/// <summary>A concession item as the Store Manager shows it.</summary>
/// <param name="Id">The database key, which the Store Manager hands back to rename, reprice, deactivate or reactivate the item.</param>
/// <param name="Name">The name, such as Popcorn.</param>
/// <param name="Code">The barcode on the concession menu, such as CON-POPCORN.</param>
/// <param name="PriceCents">The price in cents, such as 100 for $1.00.</param>
/// <param name="IsActive">False once the item is deactivated.</param>
public sealed record ConcessionRow(int Id, string Name, string Code, int PriceCents, bool IsActive);
