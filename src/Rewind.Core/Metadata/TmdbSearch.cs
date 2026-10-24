namespace Rewind.Core.Metadata;

/// <summary>What a TMDB search found.</summary>
/// <param name="Status">Whether TMDB answered (NET-12).</param>
/// <param name="Matches">The matches, best first. Empty when TMDB found none or didn't answer.</param>
public sealed record TmdbSearch(TmdbStatus Status, IReadOnlyList<TmdbMatch> Matches);
