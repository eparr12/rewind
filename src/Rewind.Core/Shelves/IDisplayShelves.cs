using Rewind.Core.MovieNights;

namespace Rewind.Core.Shelves;

/// <summary>
/// What the Store Manager asks of Core to keep the seasonal shelves, one call per action (ARCH-5): the list, and
/// adding, renaming, re-dating, deactivating and reactivating a shelf. Nothing deletes a shelf (DATA-4), and no two
/// share a name (DATA-11). A Staff Picks shelf isn't the Store Manager's: every member has one from the save that
/// adds them, and it goes by its member (ADR-0015), so no call here lists or changes one. Each change that's saved is
/// one line in the log, at Information (OBS-3), and a change that changes nothing saves nothing and logs nothing. Each
/// call hands its token on, so one that's cancelled before its save throws (NET-7). <see cref="DisplayShelves"/>
/// does it, for F5's Shelves tab.
/// </summary>
public interface IDisplayShelves
{
    /// <summary>
    /// Every seasonal shelf, active or not, in order of name, then Id, each with whether it's in season tonight. Names
    /// sort with their letter case folded from A to Z, as the names' index folds it, so halloween sorts among the
    /// others. Tonight's day is the one the open night opened on, on the register's own calendar, for as long as
    /// <see cref="IBusinessDays.CurrentAsync"/> gives that night, so the Halloween shelf stays in season until
    /// Halloween night's Close Out. With no such night, it's the register's date (ADR-0015).
    /// </summary>
    Task<IReadOnlyList<SeasonalShelfRow>> ListSeasonalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds an active seasonal shelf with nothing on it, checking the name, then the first day, then the last, as the
    /// Store Manager fills them in, and asking the database nothing until all three are right. The name is cleaned as
    /// a member's is, and cut at <see cref="Shelf.MaxNameLength"/> characters. Each month is picked, from 1 for
    /// January to 12 for December. Each day is typed: digits alone, with spaces around them allowed, from 1 to its
    /// month's last day, with 29 for February, which are the days the database takes. A name another seasonal shelf
    /// has, active or not, whatever its letter case from A to Z, comes back
    /// <see cref="SeasonalShelfChangeStatus.NameInUse"/> with that shelf, so the tab can say to reactivate it, and
    /// nothing is saved. That's the answer, too, when another shelf takes the name between the look and the save.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A month is before January or after December.</exception>
    Task<SeasonalShelfChange> AddAsync(
        string name, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken);

    /// <summary>
    /// Fixes a shelf's name, cleaned as <see cref="AddAsync"/> cleans it. Renaming is for typos: a shelf for another
    /// time of year is a new shelf, since this one keeps its movies. A name that's the same once cleaned changes
    /// nothing, but the shelf's own name in another letter case, such as Halloween for halloween, is saved. A name
    /// another seasonal shelf has comes back <see cref="SeasonalShelfChangeStatus.NameInUse"/>, as for AddAsync.
    /// </summary>
    /// <exception cref="InvalidOperationException">No seasonal shelf has the Id, such as a Staff Picks shelf's.</exception>
    Task<SeasonalShelfChange> RenameAsync(int shelfId, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Changes a shelf's season, its months picked and its days typed as for <see cref="AddAsync"/>, and checked in
    /// the same order, asking the database nothing until both days are right. A season that's the same as saved
    /// changes nothing.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A month is before January or after December.</exception>
    /// <exception cref="InvalidOperationException">No seasonal shelf has the Id, such as a Staff Picks shelf's.</exception>
    Task<SeasonalShelfChange> SetSeasonAsync(
        int shelfId, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken);

    /// <summary>
    /// Deactivates a shelf, or reactivates one. A deactivated shelf keeps its name and its movies, so reactivating it
    /// brings it back with them (ADR-0015). Asking for the state a shelf is in already changes nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">No seasonal shelf has the Id, such as a Staff Picks shelf's.</exception>
    Task<SeasonalShelfChange> SetActiveAsync(int shelfId, bool isActive, CancellationToken cancellationToken);
}
