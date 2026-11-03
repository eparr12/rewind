namespace Rewind.Core.MovieNights;

/// <summary>A business day as Core's services read it.</summary>
/// <param name="Id">The database key, which everything that belongs to the night points at.</param>
/// <param name="OpenedAt">When the night's first scan opened it, in UTC (DATA-8).</param>
/// <param name="ClosedAt">When Close Out closed it, in UTC (DATA-8). Null while it's open.</param>
public sealed record BusinessDayRow(int Id, DateTime OpenedAt, DateTime? ClosedAt);
