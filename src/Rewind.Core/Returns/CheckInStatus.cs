namespace Rewind.Core.Returns;

/// <summary>What a returned case's scan did, and if it did nothing, why (NET-12).</summary>
public enum CheckInStatus
{
    /// <summary>
    /// The case is back On shelf, and its rental item has its return time. A case that was On shelf already with its
    /// rental still open, which only a hand edit leaves, is checked in too, and the log has a warning.
    /// </summary>
    CheckedIn,

    /// <summary>No case has this label, or it isn't a store label. Nothing changes.</summary>
    NoSuchCase,

    /// <summary>
    /// The case is on the shelf, and no rental has it out, so nothing changes. Its last rental comes with it to
    /// ask about while that rental's watch log is empty (ADR-0012).
    /// </summary>
    AlreadyOnShelf,

    /// <summary>
    /// The case said it was checked out, but no rental had it out, which only a hand edit can leave. It's back On
    /// shelf now, nobody is asked, and the log has a warning.
    /// </summary>
    Reshelved,

    /// <summary>The case is retired, so it isn't checked in, and nothing changes.</summary>
    Retired,
}
