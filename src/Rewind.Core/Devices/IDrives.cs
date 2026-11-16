namespace Rewind.Core.Devices;

/// <summary>
/// The PC's drives, which Close Out looks for the backup drive among, so tests can stand in for drives that are
/// plugged in or not, and never write to a real one (TEST-5). <see cref="SystemDrives"/> is the real one.
/// </summary>
public interface IDrives
{
    /// <summary>
    /// The top folder of each drive that's ready, and fixed or removable, ending in a separator, such as E:\. A network
    /// drive is never listed, since one that's offline can keep Rewind waiting a long time.
    /// </summary>
    IReadOnlyList<string> TopFolders();
}
