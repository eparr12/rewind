namespace Rewind.Core;

/// <summary>
/// Where Rewind keeps its files. Everything lives under one root folder, C:\Rewind on the
/// register, and the Paths:Root setting can move it (NET-11).
/// </summary>
public sealed class RewindPaths
{
    /// <summary>The settings section these paths are read from.</summary>
    public const string SectionName = "Paths";

    /// <summary>The root folder on the register.</summary>
    public const string DefaultRoot = @"C:\Rewind";

    /// <summary>The folder everything else lives under. It must be a full path.</summary>
    public string Root { get; set; } = DefaultRoot;

    /// <summary>The folder that holds the database.</summary>
    public string Data => Path.Combine(Root, "data");

    /// <summary>The folder that holds the backups.</summary>
    public string Backups => Path.Combine(Root, "backups");

    /// <summary>The folder that holds the log files (OBS-2).</summary>
    public string Logs => Path.Combine(Root, "logs");

    /// <summary>The live database file.</summary>
    public string Database => Path.Combine(Data, "rewind.db");
}
