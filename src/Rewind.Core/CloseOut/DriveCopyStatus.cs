namespace Rewind.Core.CloseOut;

/// <summary>Whether each backup drive has every backup now, and if not, why (NET-12).</summary>
public enum DriveCopyStatus
{
    /// <summary>Each backup drive was given the backups it was missing, and has every one now.</summary>
    Copied,

    /// <summary>Each backup drive had every backup already, so nothing was written.</summary>
    NothingToCopy,

    /// <summary>
    /// No backup drive is plugged in: no ready drive has REWIND-BACKUP.txt in its top folder. The register's own disk
    /// never counts. Nothing was written.
    /// </summary>
    NoDrive,

    /// <summary>
    /// A backup drive couldn't be written to, such as one that's full, read-only or pulled out partway, so it may
    /// still be missing some backups. Any other backup drive was still given what it was missing.
    /// </summary>
    Unwritable,
}
