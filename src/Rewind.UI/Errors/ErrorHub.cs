using Microsoft.Extensions.Logging;

namespace Rewind.UI.Errors;

/// <summary>
/// Handles the exceptions that escape Blazor (OBS-5). Blazor's own never come here: a screen's
/// ErrorBoundary catches its exceptions, and App's catches the rest of the shell's. Program.cs
/// sends this hub what reaches Application.ThreadException, which only WinForms code such as
/// MainForm can throw and which the shell answers with the same dialog, and what reaches
/// AppDomain.UnhandledException, which can only be logged. Program.cs also sends it why Rewind
/// couldn't start, and shows the same reason in a plain message box.
/// </summary>
/// <remarks>
/// BlazorWebView itself never passes an exception on to either event. One that no ErrorBoundary
/// catches stops the page, unlogged, and one thrown inside InvokeAsync only faults the Task it
/// returns. So a component that discards that Task, as background events do (UI-11), passes
/// anything it catches to DispatchExceptionAsync, which hands it to the nearest ErrorBoundary.
/// </remarks>
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
    /// Logs an exception that nothing caught, such as one on a background thread. The process
    /// ends right after, and on the register the launcher starts Rewind again.
    /// </summary>
    public void Fatal(Exception? exception) => LogFatal(logger, exception);

    /// <summary>
    /// Logs why Rewind couldn't start, such as a setting that fails validation or a migration
    /// that fails. Program.cs shows the reason in a message box, then Rewind closes.
    /// </summary>
    public void CouldNotStart(Exception exception) => LogCouldNotStart(logger, exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on the WinForms thread; going back to Checkout")]
    private static partial void LogCrashed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception; Rewind is closing")]
    private static partial void LogFatal(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Rewind couldn't start; it shows why in a message box, then closes")]
    private static partial void LogCouldNotStart(ILogger logger, Exception exception);
}
