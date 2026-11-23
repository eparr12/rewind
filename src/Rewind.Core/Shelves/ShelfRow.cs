namespace Rewind.Core.Shelves;

/// <summary>A shelf as Ask the Clerk lists it, a member's Staff Picks or a seasonal shelf.</summary>
/// <param name="Id">The database key, which a search of the catalog takes to find the movies on the shelf.</param>
/// <param name="Name">
/// The name the screens show: a seasonal shelf's own, such as Halloween, or a member's Staff Picks, such as Dad's Staff
/// Picks, as <see cref="Shelf.StaffPicksName"/> writes it.
/// </param>
public sealed record ShelfRow(int Id, string Name);
