using Rewind.Core.MovieNights;

namespace Rewind.Core.Shelves;

/// <summary>
/// What F5's Shelves tab and F3 Ask the Clerk ask of Core about the shelves, one call per action (ARCH-5). Each
/// change that's saved is logged at Information, a line for each shelf it changes (OBS-3), and a change that changes
/// nothing saves nothing and logs nothing. Each call hands its token on, so one that's cancelled before its save
/// throws (NET-7). <see cref="DisplayShelves"/> does it.
/// </summary>
/// <remarks>
/// The Store Manager keeps the seasonal shelves: the list, and adding, renaming, re-dating, deactivating and
/// reactivating a shelf. Nothing deletes a shelf (DATA-4), and no two seasonal shelves share a name (DATA-11). A Staff
/// Picks shelf isn't the Store Manager's: every member has one from the save that adds them, and it goes by its member
/// (ADR-0015), so none of those calls lists or changes one. Ask the Clerk lists tonight's shelves and a movie's, Staff
/// Picks and seasonal alike, and puts a movie on shelves and takes it off them. Anyone at the register can put a movie
/// on any shelf, so nothing asks whose shelf it is (ADR-0015).
/// </remarks>
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

    /// <summary>
    /// The shelves to show tonight, for Ask the Clerk's list of shelves and, in Build Plan 4.3, the spinner's: each
    /// active seasonal shelf whose season takes in tonight's day, by name, then each active member's Staff Picks shelf,
    /// by the member's name, then card, as the Members tab lists the members. Names sort with their letter case folded
    /// from A to Z. A shelf with nothing on it is there too. Tonight's day is the one
    /// <see cref="ListSeasonalAsync"/> goes by, so the Halloween shelf is there until Halloween night's Close Out
    /// (ADR-0015).
    /// </summary>
    Task<IReadOnlyList<ShelfRow>> ListTonightsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Every shelf a movie can go on, for the form Ask the Clerk opens on it, each saying whether the movie sits on it:
    /// each active seasonal shelf, in season or not, with its season, then each active member's Staff Picks shelf, in
    /// <see cref="ListTonightsAsync"/>'s order. A shelf out of season is there, so a movie can go on the Christmas
    /// shelf in October. A deactivated shelf isn't, and nor is a deactivated member's, though each keeps what's on it
    /// (ADR-0015).
    /// </summary>
    /// <exception cref="InvalidOperationException">No movie has the Id.</exception>
    Task<IReadOnlyList<MovieShelfRow>> ListMovieShelvesAsync(int movieId, CancellationToken cancellationToken);

    /// <summary>
    /// Puts a movie on the shelves given, out of those <see cref="ListMovieShelvesAsync"/> gives, and takes it off the
    /// rest of them, in one save (DATA-7). It answers with the movie's shelves as they are now, as that call would give
    /// them. A shelf the movie sits on already, or one it's off and stays off, is left as it is, and an Id given twice
    /// is one shelf. A shelf that call doesn't give keeps the movie if it's on it, so a movie on a deactivated shelf,
    /// or on a deactivated member's, is there again once the shelf or the member is reactivated (ADR-0015). Each shelf
    /// the movie goes on or comes off is a line in the log.
    /// </summary>
    /// <exception cref="ArgumentNullException">The shelves' Ids are null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No movie has the Id, or a shelf's Id isn't one <see cref="ListMovieShelvesAsync"/> gives, such as a deactivated
    /// shelf's. Nothing is saved.
    /// </exception>
    /// <exception cref="Microsoft.EntityFrameworkCore.DbUpdateException">
    /// Another save put the movie on one of these shelves, or took it off one, between this call's look and its save,
    /// and nothing is saved. The form sends one save at a time, so only a bug does that (NET-12).
    /// </exception>
    Task<IReadOnlyList<MovieShelfRow>> SetMovieShelvesAsync(
        int movieId, IReadOnlyCollection<int> shelfIds, CancellationToken cancellationToken);
}
