namespace Rewind.Core.MovieNights;

/// <summary>
/// What Core asks to keep the movie night: the night's first scan opens a business day, and Close Out
/// closes it. Only one is open at a time (DATA-11). <see cref="BusinessDays"/> does it.
/// </summary>
public interface IBusinessDays
{
    /// <summary>
    /// The night that's open, or, when none is, a new one opened at the clock's time. Until Close Out,
    /// asking again gives the same night, however late it gets.
    /// </summary>
    Task<BusinessDayRow> OpenOrCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Closes the open night at the clock's time. With none open, such as at a second Close Out the same
    /// night, nothing changes and <see cref="BusinessDayClosingStatus.NothingOpen"/> comes back (NET-12).
    /// </summary>
    Task<BusinessDayClosing> CloseAsync(CancellationToken cancellationToken);
}
