namespace Rewind.Core.Checkout;

/// <summary>A movie a case holds.</summary>
/// <param name="MovieId">The database key.</param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when it isn't known (DATA-3).</param>
public sealed record MovieInCase(int MovieId, string Title, int? Year);
