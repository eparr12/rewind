namespace Rewind.Core.Intake;

/// <summary>How adding the clerk's pick went (NET-12).</summary>
public enum AddStatus
{
    /// <summary>The movie is in the collection now, with its cast, genres and posters.</summary>
    Added,

    /// <summary>The collection already had the movie, so nothing was fetched or saved.</summary>
    AlreadyOwned,

    /// <summary>TMDB has no such movie, or nothing Rewind can use for it, such as a title.</summary>
    NotFound,

    /// <summary>Rewind has no token for TMDB, or TMDB refused it.</summary>
    NotSetUp,

    /// <summary>TMDB didn't answer, or answered with trouble. Trying again later may work.</summary>
    TryLater,
}
