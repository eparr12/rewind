namespace Rewind.UI;

/// <summary>Which build this is, so developer tools never show on the register's Release build.</summary>
internal static class BuildInfo
{
#if DEBUG
    /// <summary>True in a Debug build.</summary>
    public static bool IsDebug => true;
#else
    /// <summary>True in a Debug build.</summary>
    public static bool IsDebug => false;
#endif
}
