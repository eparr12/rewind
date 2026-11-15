namespace Rewind.Core.Returns;

/// <summary>What logging who watched a rental item came to (NET-12).</summary>
public enum WatchLogStatus
{
    /// <summary>Everyone on the list is in the watch log, with their stars.</summary>
    Logged,

    /// <summary>The rental item has its watch log already, which is saved once (ADR-0012), so nothing is added.</summary>
    AlreadyLogged,

    /// <summary>
    /// Nobody was on the list, so nothing is saved, and the rental item's watch log stays empty, as a skip leaves
    /// it (ADR-0012).
    /// </summary>
    NoWatchers,
}
