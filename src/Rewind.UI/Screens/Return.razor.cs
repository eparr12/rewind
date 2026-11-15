using Microsoft.Extensions.Logging;
using Rewind.Core.Scans;

namespace Rewind.UI.Screens;

// The Return screen's log lines. [LoggerMessage] needs them in a .cs file: Razor's own source generator writes
// the rest of the component, and one source generator never sees another's code.
public partial class Return
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored the {Kind} {Code}: Return is still busy or a dialog is open")]
    private static partial void LogScanIgnored(ILogger logger, ScanKind kind, string code);
}
