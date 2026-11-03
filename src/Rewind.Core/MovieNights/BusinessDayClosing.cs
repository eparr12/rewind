namespace Rewind.Core.MovieNights;

/// <summary>What closing the night came to.</summary>
/// <param name="Status">Whether there was a night to close.</param>
/// <param name="Day">The night as closed, or null when none was open.</param>
public sealed record BusinessDayClosing(BusinessDayClosingStatus Status, BusinessDayRow? Day);
