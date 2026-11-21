using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rewind.Core.Data;
using Rewind.Core.Metadata;
using Rewind.Core.MovieNights;

namespace Rewind.Core.Shelves;

/// <summary>
/// Keeps the seasonal shelves for the Store Manager (Build Plan 4.2): each with a name and a season, in season from its
/// first day to its last by the movie night, deactivated when it isn't wanted, never deleted (DATA-4, ADR-0015). Reads
/// are untracked and go straight into rows (DATA-12).
/// </summary>
public sealed partial class DisplayShelves(
    IDbContextFactory<RewindDb> dbFactory,
    IBusinessDays days,
    TimeProvider time,
    ILogger<DisplayShelves> logger) : IDisplayShelves
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SeasonalShelfRow>> ListSeasonalAsync(CancellationToken cancellationToken)
    {
        var tonight = await TonightAsync(cancellationToken);

        // Name is a NOCASE column (ADR-0015), so the sort folds letter case from A to Z as the names' index does, with
        // no Collate. The Id after it never decides, since that index leaves no two names to tie, but it ends the order
        // on a key, as the other lists' orders end.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Shelves
            .AsNoTracking()
            .Where(s => s.MemberId == null)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => Row(s, tonight))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SeasonalShelfChange> AddAsync(
        string name, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fromDay);
        ArgumentNullException.ThrowIfNull(toDay);
        CheckPicked(fromMonth);
        CheckPicked(toMonth);

        if (Clean(name) is not { } cleanName)
        {
            return Refused(SeasonalShelfChangeStatus.NoName);
        }

        if (ReadSeason(fromMonth, fromDay, toMonth, toDay, out var refusal) is not { } season)
        {
            return Refused(refusal);
        }

        var tonight = await TonightAsync(cancellationToken);
        try
        {
            return await SaveNewAsync(cleanName, season, tonight, cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: the name was free when SaveNewAsync looked, and another shelf took it before the save. The
            // names' index is the real guard. Anything else it refused would be a bug.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindNamedAsync(db, cleanName, exceptShelfId: null, tonight, cancellationToken) is { } owner)
            {
                return NameInUse(owner);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SeasonalShelfChange> RenameAsync(int shelfId, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (Clean(name) is not { } cleanName)
        {
            return Refused(SeasonalShelfChangeStatus.NoName);
        }

        var tonight = await TonightAsync(cancellationToken);
        try
        {
            return await SaveNameAsync(shelfId, cleanName, tonight, cancellationToken);
        }
        catch (DbUpdateException e) when (SqliteErrors.IsUniqueIndexRefusal(e))
        {
            // DATA-11: as for AddAsync, another shelf took the name between SaveNameAsync's look and its save.
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await FindNamedAsync(db, cleanName, shelfId, tonight, cancellationToken) is { } owner)
            {
                return NameInUse(owner);
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SeasonalShelfChange> SetSeasonAsync(
        int shelfId, int fromMonth, string fromDay, int toMonth, string toDay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fromDay);
        ArgumentNullException.ThrowIfNull(toDay);
        CheckPicked(fromMonth);
        CheckPicked(toMonth);

        if (ReadSeason(fromMonth, fromDay, toMonth, toDay, out var refusal) is not { } season)
        {
            return Refused(refusal);
        }

        var tonight = await TonightAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var shelf = await FindAsync(db, shelfId, cancellationToken);
        var before = Row(shelf, tonight);
        if (before.Season == season)
        {
            return Saved(before); // OBS-3: the log is a shelf's only history, so no line for no change
        }

        shelf.FromMonth = season.FromMonth;
        shelf.FromDay = season.FromDay;
        shelf.ToMonth = season.ToMonth;
        shelf.ToDay = season.ToDay;
        await db.SaveChangesAsync(cancellationToken);

        // In words, and formatted only for a line that's written (CA1873).
        if (logger.IsEnabled(LogLevel.Information))
        {
            var newSeason = Describe(season);
            var oldSeason = Describe(before.Season);
            LogSeasonChanged(logger, before.Name, newSeason, oldSeason);
        }

        return Saved(Row(shelf, tonight));
    }

    /// <inheritdoc />
    public async Task<SeasonalShelfChange> SetActiveAsync(int shelfId, bool isActive, CancellationToken cancellationToken)
    {
        var tonight = await TonightAsync(cancellationToken);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var shelf = await FindAsync(db, shelfId, cancellationToken);
        if (shelf.IsActive == isActive)
        {
            return Saved(Row(shelf, tonight)); // as for a new season, no line for no change
        }

        // ADR-0015: only the shelf changes, so its movies stay on it while it's deactivated, and it has them again
        // once it's reactivated.
        shelf.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        var saved = Row(shelf, tonight);
        LogActiveChanged(logger, saved.Name, isActive ? "active" : "inactive");
        return Saved(saved);
    }

    private async Task<SeasonalShelfChange> SaveNewAsync(string name, Season season, DateOnly tonight, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await FindNamedAsync(db, name, exceptShelfId: null, tonight, cancellationToken) is { } owner)
        {
            return NameInUse(owner);
        }

        var shelf = new Shelf
        {
            Name = name,
            FromMonth = season.FromMonth,
            FromDay = season.FromDay,
            ToMonth = season.ToMonth,
            ToDay = season.ToDay,
        };
        db.Shelves.Add(shelf);
        await db.SaveChangesAsync(cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var described = Describe(season);
            LogAdded(logger, name, described);
        }

        return Saved(Row(shelf, tonight));
    }

    private async Task<SeasonalShelfChange> SaveNameAsync(int shelfId, string name, DateOnly tonight, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var shelf = await FindAsync(db, shelfId, cancellationToken);
        var before = Row(shelf, tonight);
        if (before.Name == name)
        {
            return Saved(before); // as for a new season, no line for no change
        }

        // The shelf's own name in another letter case is no other shelf's, so it's saved: the only row the index could
        // hold it against is this one.
        if (await FindNamedAsync(db, name, shelfId, tonight, cancellationToken) is { } owner)
        {
            return NameInUse(owner);
        }

        shelf.Name = name;
        await db.SaveChangesAsync(cancellationToken);
        LogRenamed(logger, before.Name, name);
        return Saved(Row(shelf, tonight));
    }

    // ADR-0015: a season goes by the movie night (DATA-8), so tonight's day is the one the night opened on, on the
    // register's own calendar (NET-8), for as long as the night is tonight's. Otherwise it's the register's date. Each
    // change reads it before its save, so nothing waits on the token once a change is saved (NET-7).
    private async Task<DateOnly> TonightAsync(CancellationToken cancellationToken)
    {
        var night = await days.CurrentAsync(cancellationToken);
        var local = night is null
            ? time.GetLocalNow().DateTime
            : TimeZoneInfo.ConvertTimeFromUtc(night.OpenedAt, time.LocalTimeZone);
        return DateOnly.FromDateTime(local);
    }

    // Typed by the Store Manager, so it's cleaned as a member's name is (SEC-5).
    private static string? Clean(string name) => WebAnswers.Clean(name, Shelf.MaxNameLength);

    // The months are picked from the twelve, so one outside them is a bug in the tab, not a slip.
    private static void CheckPicked(int month, [CallerArgumentExpression(nameof(month))] string? paramName = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(month, Shelf.FirstMonth, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, Shelf.LastMonth, paramName);
    }

    // The first day, then the last, as the Store Manager fills them in. Null with the status that says which day to fix
    // when one isn't a day.
    private static Season? ReadSeason(int fromMonth, string fromDay, int toMonth, string toDay, out SeasonalShelfChangeStatus refusal)
    {
        refusal = SeasonalShelfChangeStatus.NotAFromDay;
        if (ReadDay(fromMonth, fromDay) is not { } first)
        {
            return null;
        }

        refusal = SeasonalShelfChangeStatus.NotAToDay;
        return ReadDay(toMonth, toDay) is { } last ? new Season(fromMonth, first, toMonth, last) : null;
    }

    // SEC-5: typed by the Store Manager, so it's read as Core reads every typed number, and held to the days of its
    // month from the constants the CHECK is built from, so the database is never asked to refuse one. Blank is no day.
    private static int? ReadDay(int month, string typed) =>
        TypedNumbers.TryReadWholeNumber(typed, Shelf.FirstDay, Shelf.LastDayOf(month), out var day) ? day : null;

    // Seasonal shelves are never deleted (DATA-4), so an Id from the Store Manager's list is always a seasonal shelf's.
    // A Staff Picks shelf's Id is never in that list, since the shelf goes by its member (ADR-0015).
    private static async Task<Shelf> FindAsync(RewindDb db, int shelfId, CancellationToken cancellationToken) =>
        await db.Shelves.SingleOrDefaultAsync(s => s.Id == shelfId && s.MemberId == null, cancellationToken)
        ?? throw new InvalidOperationException(
            $"No seasonal shelf has Id {shelfId}: seasonal shelves are never deleted, and a Staff Picks shelf isn't the Store Manager's to change.");

    // The seasonal shelf with the name, active or not, other than the one given. Name is a NOCASE column (ADR-0015), so
    // == folds A to Z as the names' index does. A member's shelf has no name, so it's never the one found.
    private static Task<SeasonalShelfRow?> FindNamedAsync(
        RewindDb db, string name, int? exceptShelfId, DateOnly tonight, CancellationToken cancellationToken) =>
        db.Shelves
            .AsNoTracking()
            .Where(s => s.Name == name && s.Id != exceptShelfId)
            .Select(s => Row(s, tonight))
            .SingleOrDefaultAsync(cancellationToken);

    // NET-1: CK_Shelves_Kind gives every shelf without a member a name and all four numbers of its season, and only
    // those shelves are read here. A read builds each row with this as it's read, so the list's rows and a change's
    // are made alike.
    private static SeasonalShelfRow Row(Shelf shelf, DateOnly tonight)
    {
        var season = new Season(shelf.FromMonth!.Value, shelf.FromDay!.Value, shelf.ToMonth!.Value, shelf.ToDay!.Value);
        return new SeasonalShelfRow(shelf.Id, shelf.Name!, season, shelf.IsActive, season.Holds(tonight));
    }

    // For the family reading the log, such as October 1 to October 31, alike on any PC.
    private static string Describe(Season season) => string.Create(
        CultureInfo.InvariantCulture,
        $"{MonthName(season.FromMonth)} {season.FromDay} to {MonthName(season.ToMonth)} {season.ToDay}");

    private static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    private static SeasonalShelfChange Saved(SeasonalShelfRow shelf) => new(SeasonalShelfChangeStatus.Saved, shelf);

    private static SeasonalShelfChange NameInUse(SeasonalShelfRow owner) => new(SeasonalShelfChangeStatus.NameInUse, owner);

    private static SeasonalShelfChange Refused(SeasonalShelfChangeStatus status) => new(status, null);

    [LoggerMessage(Level = LogLevel.Information, Message = "Added seasonal shelf {Name}, {Season}")]
    private static partial void LogAdded(ILogger logger, string name, string season);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed seasonal shelf {OldName} to {NewName}")]
    private static partial void LogRenamed(ILogger logger, string oldName, string newName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seasonal shelf {Name} now runs {Season}, not {OldSeason}")]
    private static partial void LogSeasonChanged(ILogger logger, string name, string season, string oldSeason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seasonal shelf {Name} is {State}")]
    private static partial void LogActiveChanged(ILogger logger, string name, string state);
}
