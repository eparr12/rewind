using Rewind.Core.MovieNights;

namespace Rewind.Core.CloseOut;

/// <summary>
/// What a Close Out came to, for its checklist (Build Plan 3.5): whether there was a night to close, then the backup,
/// the phone catalog and the copy to the backup drive, each done or not. A step that wasn't done has a warning in the
/// log that says what went wrong. Keeping the register's newest backups isn't on it: the log has what that did.
/// </summary>
/// <param name="Night">
/// Whether a night was closed, or none was open, as on a night of returns only or at a second Close Out the same night.
/// </param>
/// <param name="Backup">The backup's file name, such as rewind-2026-10-23-2215.db, or null when it wasn't saved.</param>
/// <param name="CatalogMovies">How many movies the phone catalog lists, or null when it wasn't written.</param>
/// <param name="Copy">
/// What the copy to the backup drive answered, or null when the copy couldn't be tried, such as when the register
/// couldn't read one of its own backups.
/// </param>
public sealed record CloseOutReport(BusinessDayClosingStatus Night, string? Backup, int? CatalogMovies, DriveCopy? Copy)
{
    /// <summary>
    /// Whether everything worked: the backup was saved, the phone catalog was written, and the copy answered
    /// <see cref="DriveCopyStatus.Copied"/> or <see cref="DriveCopyStatus.NothingToCopy"/>, so each backup drive has
    /// every backup. No backup drive, or one that couldn't be written to, counts as not. Whether a night was open
    /// counts neither way.
    /// </summary>
    public bool EverythingWorked =>
        Backup is not null
        && CatalogMovies is not null
        && Copy?.Status is DriveCopyStatus.Copied or DriveCopyStatus.NothingToCopy;
}
