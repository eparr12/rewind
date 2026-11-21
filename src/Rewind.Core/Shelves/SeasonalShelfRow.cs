namespace Rewind.Core.Shelves;

/// <summary>A seasonal shelf as the Store Manager shows it.</summary>
/// <param name="Id">
/// The database key, which the Store Manager hands back to rename, re-date, deactivate or reactivate the shelf.
/// </param>
/// <param name="Name">The name, such as Halloween.</param>
/// <param name="Season">When it's in season, such as October 1 to October 31.</param>
/// <param name="IsActive">False once the shelf is deactivated.</param>
/// <param name="InSeason">
/// Whether its season takes in tonight's day, active or not. <see cref="IDisplayShelves.ListSeasonalAsync"/> says
/// which day that is.
/// </param>
public sealed record SeasonalShelfRow(int Id, string Name, Season Season, bool IsActive, bool InSeason);
