using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Rewind.Core.Tests;

/// <summary>
/// A folder that can't be listed, though files can still be made in it, until it's disposed, which puts it back so the
/// test's folder can be deleted. On Windows, a rule that denies the test's own account the folder's listing; on Linux,
/// the folder without its read permission, which root lists anyway, so a test that uses it there skips as root.
/// </summary>
internal sealed class Unlistable(Action putBack) : IDisposable
{
    public static Unlistable Make(string folder) =>
        OperatingSystem.IsWindows() ? DenyListing(folder) : TakeAwayRead(folder);

    public void Dispose() => putBack();

    [SupportedOSPlatform("windows")]
    private static Unlistable DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var rules = info.GetAccessControl();
        var denyListing = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("The test's account has no security ID."),
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);
        rules.AddAccessRule(denyListing);
        info.SetAccessControl(rules);
        return new(() =>
        {
            rules.RemoveAccessRule(denyListing);
            info.SetAccessControl(rules);
        });
    }

    [UnsupportedOSPlatform("windows")]
    private static Unlistable TakeAwayRead(string folder)
    {
        var mode = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, mode & ~(UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead));
        return new(() => File.SetUnixFileMode(folder, mode));
    }
}
