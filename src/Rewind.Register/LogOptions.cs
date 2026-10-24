using Serilog.Events;

namespace Rewind.Register;

/// <summary>
/// How much Rewind writes to its log (OBS-3, NET-11). The register runs at Information; to
/// troubleshoot, such as to see every scan, set Debug, then set it back.
/// </summary>
internal sealed class LogOptions
{
    /// <summary>The settings section these options are read from.</summary>
    public const string SectionName = "Log";

    /// <summary>
    /// The least important entries Rewind's own code writes: Debug, Information, Warning or
    /// Error. Microsoft's libraries write only their warnings and errors, whatever this is.
    /// </summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;
}
