namespace Rewind.Core.Intake;

/// <summary>How picking a movie from TMDB's matches went (NET-12). Nothing is saved either way.</summary>
public enum PickStatus
{
    /// <summary>
    /// The movie is new to the collection. Its details and posters are fetched from TMDB and held
    /// until the case's label scan saves them.
    /// </summary>
    Fetched,

    /// <summary>The collection already has the movie, so nothing was fetched, and the case joins it.</summary>
    Owned,

    /// <summary>TMDB has no such movie, or nothing Rewind can use for it, such as a title.</summary>
    NotFound,

    /// <summary>Rewind has no token for TMDB, or TMDB refused it.</summary>
    NotSetUp,

    /// <summary>TMDB didn't answer, or answered with trouble. Trying again later may work.</summary>
    TryLater,
}
