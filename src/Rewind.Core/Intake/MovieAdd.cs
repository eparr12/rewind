namespace Rewind.Core.Intake;

/// <summary>How adding the clerk's pick went, and the movie as the collection has it now.</summary>
/// <param name="Status">Whether it was added, was already there, or why TMDB couldn't help.</param>
/// <param name="Movie">
/// The movie as it is in the collection. Null unless it was <see cref="AddStatus.Added"/> or
/// <see cref="AddStatus.AlreadyOwned"/>.
/// </param>
public sealed record MovieAdd(AddStatus Status, MovieCard? Movie);
