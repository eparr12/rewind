using Rewind.Core.Metadata;

namespace Rewind.Core.Catalog;

/// <summary>A movie a search of the catalog found, however many cases it's in.</summary>
/// <param name="MovieId">The database key.</param>
/// <param name="Title">The title, such as Halloween.</param>
/// <param name="Year">The year it came out. Null when it isn't known (DATA-3).</param>
/// <param name="Rating">The US rating, one of <see cref="UsRatings.All"/>, such as R. Null when it isn't known (DATA-3).</param>
/// <param name="OnShelf">
/// Whether a case of it is on the shelf, ready to rent. False when each of its cases that isn't retired is checked out.
/// </param>
/// <param name="Thumb">
/// Its thumbnail, as <see cref="Posters"/> saved it at <see cref="PosterSize.Thumb"/>. Null when it has none, such as a
/// movie entered by hand, or when it can't be read, so the list shows the movie without it (GEN-7).
/// </param>
public sealed record MovieHit(int MovieId, string Title, int? Year, string? Rating, bool OnShelf, byte[]? Thumb);
