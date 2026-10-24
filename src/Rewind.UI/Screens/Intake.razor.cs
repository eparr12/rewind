using Microsoft.Extensions.Logging;

namespace Rewind.UI.Screens;

// The Intake screen's log lines. [LoggerMessage] needs them in a .cs file: Razor's own source
// generator writes the rest of the component, and one source generator never sees another's code.
public partial class Intake
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored the factory UPC {Upc}: Intake is still busy with the last DVD")]
    private static partial void LogScanIgnored(ILogger logger, string upc);
}
