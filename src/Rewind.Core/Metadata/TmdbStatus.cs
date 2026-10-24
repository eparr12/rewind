namespace Rewind.Core.Metadata;

/// <summary>
/// Whether TMDB answered, and if not, why, so the screen can tell the clerk. A failed lookup is an
/// outcome, never an exception (NET-12).
/// </summary>
public enum TmdbStatus
{
    /// <summary>TMDB answered.</summary>
    Ok,

    /// <summary>TMDB has no such movie, or nothing Rewind can use for it, such as a title.</summary>
    NotFound,

    /// <summary>
    /// Rewind has no token for TMDB, or TMDB refused it. The log says which, and the Store Manager
    /// sets the token up as the README says.
    /// </summary>
    NotSetUp,

    /// <summary>
    /// TMDB didn't answer in time, the register is offline, or TMDB was busy or broken. Trying
    /// again later may work.
    /// </summary>
    TryLater,
}
