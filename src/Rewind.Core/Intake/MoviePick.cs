namespace Rewind.Core.Intake;

/// <summary>What picking a movie from TMDB's matches came to: the movie for the case, or why there's none.</summary>
/// <param name="Status">Whether the movie is new to the collection, already in it, or why TMDB couldn't help.</param>
/// <param name="Entry">
/// The movie, ready to go in the case: a <see cref="CaseMovieEntry.Fetched"/> or a
/// <see cref="CaseMovieEntry.Owned"/>. Null unless the status is <see cref="PickStatus.Fetched"/>
/// or <see cref="PickStatus.Owned"/>.
/// </param>
public sealed record MoviePick(PickStatus Status, CaseMovieEntry? Entry);
