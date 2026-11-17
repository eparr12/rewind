namespace Rewind.Core.Data;

/// <summary>
/// The last backup, as the status bar shows it (OBS-6): when the newest was made, and word of each new one. The backups
/// folder is the record of that (ADR-0013), so it's read from the newest backup's name, and kept nowhere else.
/// <see cref="Backups"/> is the one there is.
/// </summary>
public interface ILastBackup
{
    /// <summary>
    /// When the newest backup was made, as its name says, in the register's own local time: 10:15 PM on October 23,
    /// 2026 for rewind-2026-10-23-2215.db. Null before the first backup. A backups folder that can't be read, such as
    /// after its permissions were changed by hand, answers null too, and logs a warning, so whatever shows the answer
    /// never fails over it (GEN-7).
    /// </summary>
    DateTime? NewestMadeAt();

    /// <summary>
    /// Raised each time a backup is made, before a migration or at Close Out, once it's whole and has its name, so
    /// <see cref="NewestMadeAt"/> finds it from then on. It comes on the background thread that made the backup, so a
    /// component moves onto its own with InvokeAsync before touching its state (UI-11).
    /// </summary>
    event Action? BackedUp;
}
