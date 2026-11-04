using Microsoft.Extensions.Logging;

namespace Rewind.UI;

// The shell's log lines. [LoggerMessage] needs them in a .cs file: Razor's own source generator writes
// the rest of the component, and one source generator never sees another's code.
public partial class Shell
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Opened {Screen}")]
    private static partial void LogOpened(ILogger logger, string screen);
}
