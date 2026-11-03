namespace Rewind.Core.LateFees;

/// <summary>A member's late-fee draw for the night, as it's saved.</summary>
/// <param name="Id">The database key.</param>
/// <param name="MemberId">The member who drew.</param>
/// <param name="BusinessDayId">The movie night it was drawn on.</param>
/// <param name="Card">The card the member drew.</param>
/// <param name="FlavorMovieId">
/// The movie an Overdue card's alert names. Null for a Clear card, or when the collection has no movies.
/// </param>
/// <param name="DrawnAt">When it was drawn, in UTC (DATA-8).</param>
public sealed record LateFeeDrawRow(int Id, int MemberId, int BusinessDayId, Card Card, int? FlavorMovieId, DateTime DrawnAt);
