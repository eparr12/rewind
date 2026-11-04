using Microsoft.Extensions.Logging;
using Rewind.Core.Scans;

namespace Rewind.UI.Screens;

// The Checkout screen's log lines. [LoggerMessage] needs them in a .cs file: Razor's own source generator
// writes the rest of the component, and one source generator never sees another's code.
public partial class Checkout
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored the {Kind} {Code}: Checkout is still busy, a movie is being picked, or a dialog is open")]
    private static partial void LogScanIgnored(ILogger logger, ScanKind kind, string code);
}
