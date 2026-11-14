using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Rewind.Core.Catalog;
using Rewind.Core.Checkout;
using Rewind.Core.Data;
using Rewind.Core.Members;
using Rewind.Core.Scans;

namespace Rewind.Core.Returns;

/// <summary>
/// Checks returned cases in and logs who watched them (Build Plan 3.4). A return is saved at the scan, and its
/// watch log once, when the clerk answers (ADR-0012). The rules a return can break are the database's (DATA-11);
/// this checks them first, so a case scanned twice comes back as a result (NET-12), and a bug throws before the
/// database sees it.
/// </summary>
public sealed partial class ReturnDesk(
    IDbContextFactory<RewindDb> dbFactory,
    IMemberRoster roster,
    TimeProvider time,
    ILogger<ReturnDesk> logger) : IReturnDesk
{
    /// <inheritdoc />
    public async Task<CheckIn> CheckInAsync(string storeLabel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storeLabel);

        var label = Barcodes.Classify(storeLabel);
        if (label.Kind != ScanKind.StoreLabel)
        {
            return new CheckIn(CheckInStatus.NoSuchCase, null);
        }

        // ADR-0006: EF Core begins a transaction on SQLite as BEGIN IMMEDIATE, so it holds the write lock from
        // before these reads, and nothing can check the case in or out between the look and the save.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var found = await db.Cases.SingleOrDefaultAsync(c => c.StoreLabel == label.Code, cancellationToken);
        if (found is null)
        {
            return new CheckIn(CheckInStatus.NoSuchCase, null);
        }

        // Refused even with a rental open. Nothing retires a case yet, so what that rental comes to is left to
        // whatever retires one.
        if (found.Status == CaseStatus.Retired)
        {
            return new CheckIn(CheckInStatus.Retired, null);
        }

        // An open rental item is what says a case is out, whatever its status says, and DATA-11 allows one.
        var open = await db.RentalItems.SingleOrDefaultAsync(i => i.CaseId == found.Id && i.ReturnedAt == null, cancellationToken);
        if (open is not null)
        {
            return await ReturnAsync(db, transaction, found, open, cancellationToken);
        }

        return found.Status == CaseStatus.CheckedOut
            ? await ReshelveAsync(db, transaction, found, cancellationToken)
            : new CheckIn(CheckInStatus.AlreadyOnShelf, await LastRentalToAskAsync(db, found, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<WatchLogStatus> LogWhoWatchedAsync(int rentalItemId, IReadOnlyList<Watcher> watchers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(watchers);
        ThrowIfBadWatchers(watchers);
        if (watchers.Count == 0)
        {
            return WatchLogStatus.NoWatchers; // nothing to save, so nothing to look up
        }

        // BEGIN IMMEDIATE, as for a check-in, so a second answer can't land between the look and the save.
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await IsLoggedAsync(db, rentalItemId, cancellationToken))
        {
            return WatchLogStatus.AlreadyLogged;
        }

        db.WatchLog.AddRange(watchers.Select(w => new Viewing { RentalItemId = rentalItemId, MemberId = w.MemberId, Stars = w.Stars }));
        await db.SaveChangesAsync(cancellationToken); // DATA-7: all of the item's rows, or none of them
        await transaction.CommitAsync(cancellationToken);
        LogWatchLogged(logger, rentalItemId, watchers.Count);
        return WatchLogStatus.Logged;
    }

    // DATA-7: the return time and the case's status in one save. What the screen asks with is read first, inside
    // the transaction, so once the commit is in, nothing is left for a cancel to stop (NET-7).
    private async Task<CheckIn> ReturnAsync(RewindDb db, IDbContextTransaction transaction, Case found, RentalItem open, CancellationToken cancellationToken)
    {
        var rental = await RentalToAskAsync(db, open.Id, found.StoreLabel, cancellationToken);
        open.ReturnedAt = time.GetUtcNow().UtcDateTime; // NET-8, DATA-8
        found.Status = CaseStatus.OnShelf;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogCheckedIn(logger, found.StoreLabel, open.Id, open.RentalId);
        return new CheckIn(CheckInStatus.CheckedIn, rental);
    }

    // Rewind never checks a case out without a rental item, or closes one without shelving its case, so this is
    // a hand edit's. The case is in the clerk's hands, so it goes on the shelf, and no rental came back, so nobody
    // is asked. The warning is the edit's only trace.
    private async Task<CheckIn> ReshelveAsync(RewindDb db, IDbContextTransaction transaction, Case found, CancellationToken cancellationToken)
    {
        found.Status = CaseStatus.OnShelf;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogReshelved(logger, found.StoreLabel);
        return new CheckIn(CheckInStatus.Reshelved, null);
    }

    // ADR-0012: an empty watch log can be filled in later, for as long as its rental is the case's last. Last by
    // Id, the order the rentals happened in, as LateFeeDraws reads its draws, since a clock set back could put the
    // times out of order.
    private async Task<ReturnedRental?> LastRentalToAskAsync(RewindDb db, Case found, CancellationToken cancellationToken)
    {
        var last = await db.RentalItems
            .AsNoTracking()
            .Where(i => i.CaseId == found.Id)
            .OrderByDescending(i => i.Id)
            .Select(i => new { i.Id, Logged = db.WatchLog.Any(v => v.RentalItemId == i.Id) })
            .FirstOrDefaultAsync(cancellationToken);
        return last is null || last.Logged ? null : await RentalToAskAsync(db, last.Id, found.StoreLabel, cancellationToken);
    }

    // DATA-8: the night is its business day's, not the rental's own time, since a night can run past midnight. The
    // people come from the roster, so the screen lists the family as the Store Manager does, less anyone
    // deactivated.
    private async Task<ReturnedRental> RentalToAskAsync(RewindDb db, int rentalItemId, string storeLabel, CancellationToken cancellationToken)
    {
        var rental = await (
            from item in db.RentalItems.AsNoTracking()
            join sale in db.Rentals on item.RentalId equals sale.Id
            join renter in db.Members on sale.MemberId equals renter.Id
            join night in db.BusinessDays on sale.BusinessDayId equals night.Id
            join movie in db.Movies on item.MovieId equals movie.Id
            where item.Id == rentalItemId
            select new { movie.Title, movie.Year, RentedBy = renter.Name, night.OpenedAt })
            .SingleAsync(cancellationToken);
        var members = await roster.ListAsync(cancellationToken);
        return new ReturnedRental(rentalItemId, storeLabel, rental.Title, rental.Year, rental.RentedBy, rental.OpenedAt, [.. members.Where(m => m.IsActive)]);
    }

    // Bugs, not a clerk's slips: the screen offers stars in range and each member once. CK_WatchLog_Stars and the
    // unique index stay the real guard (DATA-11).
    private static void ThrowIfBadWatchers(IReadOnlyList<Watcher> watchers)
    {
        if (watchers.FirstOrDefault(w => w.Stars is < Viewing.MinStars or > Viewing.MaxStars) is { } outOfRange)
        {
            throw new ArgumentOutOfRangeException(nameof(watchers), outOfRange.Stars, $"Stars go from {Viewing.MinStars} to {Viewing.MaxStars}, or null for none.");
        }

        if (watchers.GroupBy(w => w.MemberId).FirstOrDefault(member => member.Count() > 1) is { } twice)
        {
            throw new ArgumentException($"Member {twice.Key} is on the list twice.", nameof(watchers));
        }
    }

    // ADR-0012: saved once, so any row at all means the item has its watch log. Return asks who watched only once a
    // case is back, and rental items are never deleted (DATA-4), so an item that isn't there or isn't back is a bug.
    private static async Task<bool> IsLoggedAsync(RewindDb db, int rentalItemId, CancellationToken cancellationToken)
    {
        var item = await db.RentalItems
            .AsNoTracking()
            .Where(i => i.Id == rentalItemId)
            .Select(i => new { i.ReturnedAt, Logged = db.WatchLog.Any(v => v.RentalItemId == i.Id) })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"There's no rental item {rentalItemId}, though rental items are never deleted.");
        return item.ReturnedAt is null
            ? throw new InvalidOperationException($"Rental item {rentalItemId} isn't back yet, and nobody is asked who watched until it is.")
            : item.Logged;
    }

    // Information (OBS-3): a return is a key event, and closes the rental item a checkout opened.
    [LoggerMessage(Level = LogLevel.Information, Message = "Checked in case {StoreLabel}, rental item {RentalItemId} of rental {RentalId}")]
    private static partial void LogCheckedIn(ILogger logger, string storeLabel, int rentalItemId, int rentalId);

    // Warning (OBS-3): a handled problem, and the only trace of the hand edit that made it.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Case {StoreLabel} was checked out with no rental out, which only a hand edit leaves, so it's back on the shelf")]
    private static partial void LogReshelved(ILogger logger, string storeLabel);

    // Information (OBS-3): the watch log is the heirloom, and this is the one time a rental item's is written.
    [LoggerMessage(Level = LogLevel.Information, Message = "Logged who watched rental item {RentalItemId}: watchers {Watchers}")]
    private static partial void LogWatchLogged(ILogger logger, int rentalItemId, int watchers);
}
