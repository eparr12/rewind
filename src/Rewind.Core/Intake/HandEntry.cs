namespace Rewind.Core.Intake;

/// <summary>What a movie typed in by hand came to: the movie for the case, or what to fix.</summary>
/// <param name="Status">Whether the movie can go in the case, and if not, which field to fix.</param>
/// <param name="Entry">The movie, ready to go in the case. Null unless the status is <see cref="HandEntryStatus.Entered"/>.</param>
public sealed record HandEntry(HandEntryStatus Status, CaseMovieEntry.ByHand? Entry);
