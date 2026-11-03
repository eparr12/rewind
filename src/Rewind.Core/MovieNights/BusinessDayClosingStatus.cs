namespace Rewind.Core.MovieNights;

/// <summary>Whether there was a night to close (NET-12).</summary>
public enum BusinessDayClosingStatus
{
    /// <summary>The open night is closed.</summary>
    Closed,

    /// <summary>No night was open, and nothing changed.</summary>
    NothingOpen,
}
