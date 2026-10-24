namespace Rewind.Core.Metadata;

/// <summary>A genre as TMDB names it, such as 27, Horror.</summary>
/// <param name="TmdbId">The genre's ID at TMDB, which Rewind matches genres by.</param>
/// <param name="Name">The genre's name.</param>
public sealed record TmdbGenre(int TmdbId, string Name);
