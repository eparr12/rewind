namespace Rewind.Core.Devices;

/// <summary>The PC's real drives, through DriveInfo.</summary>
public sealed class SystemDrives : IDrives
{
    /// <inheritdoc />
    public IReadOnlyList<string> TopFolders() =>
        [.. DriveInfo.GetDrives()
            // The type first: asking an offline network drive whether it's ready can stall, and asking its type can't.
            // A USB hard drive says it's fixed, like the register's own disk, so the type can't pick the backup drive.
            .Where(drive => drive.DriveType is DriveType.Fixed or DriveType.Removable)
            .Where(drive => drive.IsReady)
            .Select(drive => drive.RootDirectory.FullName)];
}
