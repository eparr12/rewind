namespace Rewind.Core.Checkout;

/// <summary>A case on the shelf, ready to go on the bill.</summary>
/// <param name="CaseId">The database key.</param>
/// <param name="StoreLabel">The store label on the case, such as RV-00042.</param>
/// <param name="Movies">Its movies, in order of title, then year: one, or several for a box set or a double feature.</param>
public sealed record CaseToRent(int CaseId, string StoreLabel, IReadOnlyList<MovieInCase> Movies);
