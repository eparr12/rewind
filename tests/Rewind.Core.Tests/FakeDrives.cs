using Rewind.Core.Devices;

namespace Rewind.Core.Tests;

/// <summary>
/// The PC's drives, as each test sets them up (TEST-5): each one plugged in is a folder under the test's own root,
/// whose path ends in a separator, as E:\ does. No test ever looks at the PC's real drives, so a backup drive left
/// plugged in during dotnet test is never written to.
/// </summary>
internal sealed class FakeDrives(string folder) : IDrives
{
    private readonly List<string> _topFolders = [];

    public IReadOnlyList<string> TopFolders() => [.. _topFolders];

    /// <summary>Plugs in a drive with nothing on it, and returns its top folder.</summary>
    public string PlugIn(string name)
    {
        var top = Path.Combine(folder, name) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(top);
        _topFolders.Add(top);
        return top;
    }

    /// <summary>Plugs in a drive with REWIND-BACKUP.txt in its top folder, and returns its top folder.</summary>
    public string PlugInBackupDrive(string name)
    {
        var top = PlugIn(name);
        File.WriteAllText(Path.Combine(top, "REWIND-BACKUP.txt"), "");
        return top;
    }

    /// <summary>Lists a drive's top folder as the test writes it, such as one plugged in already, in other capitals.</summary>
    public void Add(string topFolder) => _topFolders.Add(topFolder);
}
