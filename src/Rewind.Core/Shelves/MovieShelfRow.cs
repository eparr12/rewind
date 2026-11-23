namespace Rewind.Core.Shelves;

/// <summary>A shelf a movie can go on, as the form Ask the Clerk opens on the movie shows it.</summary>
/// <param name="Id">The database key, which the form hands back for each shelf the movie should sit on.</param>
/// <param name="Name">The name the screens show, as <see cref="ShelfRow.Name"/> is.</param>
/// <param name="Season">
/// A seasonal shelf's season, such as October 1 to October 31, whether or not it's in season tonight. Null for a
/// member's Staff Picks, which has none.
/// </param>
/// <param name="HasTheMovie">Whether the movie sits on the shelf.</param>
public sealed record MovieShelfRow(int Id, string Name, Season? Season, bool HasTheMovie);
