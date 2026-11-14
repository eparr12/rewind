namespace Rewind.Core.Returns;

/// <summary>
/// What F2 Return asks of Core, one call per thing the clerk does (ARCH-5): a returned case's label, then who
/// watched. The scan checks the case in at once, and who watched is a second save, which a rental item gets once
/// (ADR-0012). <see cref="ReturnDesk"/> does it.
/// </summary>
public interface IReturnDesk
{
    /// <summary>
    /// Checks in the case with this store label: its open rental item gets the clock's time as its return time,
    /// and the case goes back On shelf, in one save (DATA-7), before anyone is asked who watched. The open rental
    /// item is what says a case is out, whatever the case's status says. The label is read as a scan is, so
    /// rv-00042 is RV-00042, and a barcode that isn't a store label is no case's. A return opens no movie night,
    /// and one that comes back on a later night checks in the same way, with nothing charged. A case that's on
    /// the shelf already comes back <see cref="CheckInStatus.AlreadyOnShelf"/>, with its last rental to ask about
    /// while that rental's watch log is empty. One marked checked out with no rental out, which only a hand edit
    /// leaves, goes back on the shelf as <see cref="CheckInStatus.Reshelved"/>, with nobody to ask. A retired case
    /// is refused, and nothing changes. Once the return is saved, a cancel can't take the answer back (NET-7).
    /// </summary>
    Task<CheckIn> CheckInAsync(string storeLabel, CancellationToken cancellationToken);

    /// <summary>
    /// Saves who watched a rental item that's back, each with their stars, in one save (DATA-7). A rental item's
    /// watch log is saved once (ADR-0012): one that has rows already comes back
    /// <see cref="WatchLogStatus.AlreadyLogged"/>, and nothing is added. An empty list saves nothing, so the
    /// watch log stays empty, as a skip leaves it. Stars outside <see cref="Viewing.MinStars"/> to
    /// <see cref="Viewing.MaxStars"/>, a member on the list twice, and a rental item that isn't there or isn't
    /// back yet are bugs, not slips, so they throw.
    /// </summary>
    Task<WatchLogStatus> LogWhoWatchedAsync(int rentalItemId, IReadOnlyList<Watcher> watchers, CancellationToken cancellationToken);
}
