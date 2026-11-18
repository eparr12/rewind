namespace Rewind.Core.MovieNights;

/// <summary>
/// One movie night, from the first member card scanned at Checkout to Close Out, or, when nobody closed it
/// out, to the card that opened the next night (ADR-0014). A night is a row here and not a calendar date, so
/// one that runs past midnight is still one night (DATA-8). A return opens none.
/// </summary>
public sealed class BusinessDay
{
    /// <summary>The database key.</summary>
    public int Id { get; set; }

    /// <summary>When the first member card scanned at Checkout opened it, in UTC (DATA-8).</summary>
    public DateTime OpenedAt { get; set; }

    /// <summary>
    /// When Close Out closed it, or, when nobody closed it out, when the card that opened the next night was
    /// scanned (ADR-0014), in UTC (DATA-8). Null while it's open.
    /// </summary>
    public DateTime? ClosedAt { get; set; }
}
