namespace Rewind.Core.MovieNights;

/// <summary>
/// Keeps the movie night: the first member card scanned at Checkout opens a business day, and Close Out
/// closes it, or, when nobody closed it out, the card that opens the next night does (ADR-0014). A return
/// opens none and closes none. Only one is open at a time (DATA-11). <see cref="BusinessDays"/> does it.
/// </summary>
public interface IBusinessDays
{
    /// <summary>
    /// The night that's open, or, when none is, a new one opened at the clock's time. Asking again gives the
    /// same night, however late it gets, for up to 16 hours from when it opened, unless Close Out closes it
    /// first. A night open for longer is one nobody closed out, so it's closed at the clock's time and a new
    /// one opened, together in one save (DATA-7), and a Warning in the log names it and when it opened
    /// (ADR-0014).
    /// </summary>
    Task<BusinessDayRow> OpenOrCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The night that's open, while it's still tonight's, or null when none is. It's the night
    /// <see cref="OpenOrCurrentAsync"/> gives back when it has one to give, but this opens none, closes none and logs
    /// nothing: it's null before the night's first card, after its Close Out, and once a night nobody closed out is too
    /// old for OpenOrCurrentAsync to give back. It's for what goes by the night without starting one, such as which
    /// seasonal shelves are in season (ADR-0015).
    /// </summary>
    Task<BusinessDayRow?> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the open night at the clock's time. With none open, such as at a second Close Out the same
    /// night, nothing changes and <see cref="BusinessDayClosingStatus.NothingOpen"/> comes back (NET-12).
    /// </summary>
    Task<BusinessDayClosing> CloseAsync(CancellationToken cancellationToken);
}
