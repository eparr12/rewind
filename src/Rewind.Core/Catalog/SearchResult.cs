namespace Rewind.Core.Catalog;

/// <summary>What a search of the catalog came to.</summary>
/// <param name="Status">Whether the search ran, and if not, what to fix.</param>
/// <param name="Hits">
/// The movies it found, in order, and no more than <see cref="CatalogSearch.MaxHits"/> of them. Empty when none
/// matched, and unless the status is <see cref="SearchStatus.Searched"/>.
/// </param>
/// <param name="MoreMatched">
/// Whether more movies matched than <paramref name="Hits"/> holds, so the clerk can narrow the search. False unless the
/// status is <see cref="SearchStatus.Searched"/>.
/// </param>
public sealed record SearchResult(SearchStatus Status, IReadOnlyList<MovieHit> Hits, bool MoreMatched);
