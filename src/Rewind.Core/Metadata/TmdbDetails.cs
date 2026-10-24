namespace Rewind.Core.Metadata;

/// <summary>What TMDB said about one movie.</summary>
/// <param name="Status">Whether TMDB answered (NET-12).</param>
/// <param name="Movie">The movie's details. Null unless the status is <see cref="TmdbStatus.Ok"/>.</param>
public sealed record TmdbDetails(TmdbStatus Status, TmdbMovie? Movie);
