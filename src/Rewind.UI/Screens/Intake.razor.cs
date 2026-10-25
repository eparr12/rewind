using Microsoft.Extensions.Logging;
using Rewind.Core.Scans;

namespace Rewind.UI.Screens;

// The Intake screen's log lines. [LoggerMessage] needs them in a .cs file: Razor's own source
// generator writes the rest of the component, and one source generator never sees another's code.
public partial class Intake
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored the {Kind} {Code}: Intake is still busy, or a dialog is open")]
    private static partial void LogScanIgnored(ILogger logger, ScanKind kind, string code);
}
