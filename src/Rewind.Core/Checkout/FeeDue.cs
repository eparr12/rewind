namespace Rewind.Core.Checkout;

/// <summary>The late fee an Overdue card puts on the bill, until a sale that completes has paid it (ADR-0010).</summary>
/// <param name="LineName">The name the fee line goes by, such as Late fee: Halloween (1978), or Late fee when the draw names no movie.</param>
/// <param name="PriceCents">The late fee as it is now.</param>
/// <param name="MovieTitle">The title of the movie the alert names. Null when the draw names none.</param>
/// <param name="MovieYear">The year of that movie. Null when the draw names none, or the year isn't known (DATA-3).</param>
public sealed record FeeDue(string LineName, int PriceCents, string? MovieTitle, int? MovieYear);
