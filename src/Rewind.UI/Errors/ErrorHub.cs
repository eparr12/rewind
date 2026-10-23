using Microsoft.Extensions.Logging;

namespace Rewind.UI.Errors;

/// <summary>
/// Handles the exceptions that escape Blazor (OBS-5). A screen's own exceptions never come
/// here: its ErrorBoundary logs them and shows the error dialog. Program.cs sends this hub
/// what reaches Application.ThreadException, which the shell answers with the same dialog,
/// and what reaches AppDomain.UnhandledException, which can only be logged.
/// </summary>
public sealed partial class ErrorHub(ILogger<ErrorHub> logger)
{
    /// <summary>Raised after an exception on the WinForms thread is logged.</summary>
    public event Action? Crashed;

    /// <summary>
    /// Logs an exception that reached the WinForms thread, then raises <see cref="Crashed"/>
    /// so the shell can show the error dialog and go back to Checkout.
    /// </summary>
    public void Report(Exception exception)
    {
        LogCrashed(logger, exception);
        Crashed?.Invoke();
    }

    /// <summary>
    /// Logs an exception from another thread. The process ends right after, and on the
    /// register the launcher starts Rewind again.
    /// </summary>
    public void Fatal(Exception? exception) => LogFatal(logger, exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the WinForms thread; going back to Checkout")]
    private static partial void LogCrashed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception on another thread; Rewind is closing")]
    private static partial void LogFatal(ILogger logger, Exception? exception);
}
