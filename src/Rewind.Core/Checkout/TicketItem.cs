namespace Rewind.Core.Checkout;

/// <summary>A case on the bill, with the movie picked from it.</summary>
/// <param name="CaseId">The case.</param>
/// <param name="StoreLabel">The store label on the case, as the bill shows it.</param>
/// <param name="MovieId">The movie picked from the case.</param>
/// <param name="Title">The movie's title, as the bill shows it.</param>
/// <param name="Year">The movie's year, as the bill shows it. Null when it isn't known (DATA-3).</param>
/// <param name="PriceCents">The rental price it was rung up at.</param>
public sealed record TicketItem(int CaseId, string StoreLabel, int MovieId, string Title, int? Year, int PriceCents);
