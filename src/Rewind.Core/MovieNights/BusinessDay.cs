namespace Rewind.Core.MovieNights;

/// <summary>
/// One movie night, from its first scan to Close Out. A night is a row here and not a calendar date, so
/// one that runs past midnight is still one night (DATA-8).
/// </summary>
public sealed class BusinessDay
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>When the night's first scan opened it, in UTC (DATA-8).</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>When Close Out closed it, in UTC (DATA-8). Null while it's open.</summary>
    public DateTime? ClosedAt { get; set; }
}
